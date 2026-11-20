#!/usr/bin/env bash
# =================================================================================================
# bootstrap-keyvault.sh: populate the production Key Vaults (run by an operator, never by CI).
#
# Terraform creates the vaults but never their secrets, so secret values stay out of Terraform state and
# CI logs. Run this from a machine whose public IP is in `admin_ip_ranges` and as a user listed in
# `key_vault_admin_object_ids` (Key Vault Secrets Officer + Crypto Officer). See docs/deployment.md.
#
# Usage: scripts/bootstrap-keyvault.sh --web-vault <name> --worker-vault <name> [options]
#   --rotate-web-key       Generate a new web key; the current one becomes Crypto--WebKeyPrevious
#   --rotate-payload-key   Generate a new Temporal payload key; the current one becomes Temporal--PayloadKeyPrevious
#   --google               Also set the Google OAuth client (optional; turns on Google sign-in for family
#                          members invited from the People page). Without it, missing Google secrets are skipped.
#   --update <secret>      Re-prompt for a secret that already exists; repeat it for several, or use
#                          --update all. Secrets: Auth--GitHubClientId Auth--GitHubClientSecret
#                          Auth--GoogleClientId Auth--GoogleClientSecret
#                          Temporal--ApiKey (asks for the web key, then the worker key) Crypto--ArchivePassword
#                          Archive--AccessKeyId Archive--SecretAccessKey Gemini--ApiKey
#   -h, --help
#
# Values are taken from these environment variables when set, otherwise prompted for (input hidden):
#   ARCHIVE_PASSWORD           the protect.ps1 encryption password            (worker)
#   WASABI_ACCESS_KEY_ID       Wasabi access key (prefer a bucket-scoped key)  (worker)
#   WASABI_SECRET_ACCESS_KEY   Wasabi secret key                               (worker)
#   GEMINI_API_KEY             Gemini API key                                  (worker)
#   TEMPORAL_API_KEY_WORKER    API key of the <prefix>-worker service account  (worker)
#   TEMPORAL_API_KEY_WEB       API key of the <prefix>-web service account     (web)
#   GITHUB_CLIENT_ID           GitHub OAuth app client id                      (web)
#   GITHUB_CLIENT_SECRET       GitHub OAuth app client secret                  (web)
#   GOOGLE_CLIENT_ID           Google OAuth client id (optional, --google)     (web)
#   GOOGLE_CLIENT_SECRET       Google OAuth client secret (optional, --google) (web)
# Secret values are passed to az through permission-600 temporary files, never on a command line (which
# would expose them in shell history and process listings), and are never printed. After each one the script
# confirms how many characters it received, and offers to remove spaces or a carriage return that pasting
# (for example from Windows into WSL) can add at either end.
# After changing secrets, restart the apps: az containerapp revision restart (docs/operations.md).
# =================================================================================================
set -euo pipefail

web_vault=""; worker_vault=""; rotate_web=false; rotate_payload=false; google=false; updates=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --web-vault) web_vault="$2"; shift 2 ;;
    --worker-vault) worker_vault="$2"; shift 2 ;;
    --rotate-web-key) rotate_web=true; shift ;;
    --rotate-payload-key) rotate_payload=true; shift ;;
    --google) google=true; shift ;;
    --update) updates+=("$2"); shift 2 ;;
    -h|--help) sed -n '3,/^# =====/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$web_vault" && -n "$worker_vault" ]] || { echo "--web-vault and --worker-vault are required (terraform output key_vaults)." >&2; exit 1; }

# A misspelled --update would otherwise silently re-prompt for nothing.
updatable=(Auth--GitHubClientId Auth--GitHubClientSecret Auth--GoogleClientId Auth--GoogleClientSecret Temporal--ApiKey Crypto--ArchivePassword Archive--AccessKeyId Archive--SecretAccessKey Gemini--ApiKey)
for name in ${updates[@]+"${updates[@]}"}; do
  [[ "$name" == all ]] && continue
  printf '%s\n' "${updatable[@]}" | grep -qxF -- "$name" \
    || { echo "--update: unknown secret '$name'. Use one of: all ${updatable[*]}" >&2; exit 1; }
done

selected_for_update() {
  local name
  for name in ${updates[@]+"${updates[@]}"}; do
    [[ "$name" == all || "$name" == "$1" ]] && return 0
  done
  return 1
}
command -v az >/dev/null || { echo "Azure CLI not found." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Run 'az login' first." >&2; exit 1; }

workdir="$(mktemp -d)"
chmod 700 "$workdir"
trap 'rm -rf "$workdir"' EXIT

# 0 when the secret exists, 1 when Key Vault says it does not. Any other failure (no permission yet, firewall,
# network) aborts the script: mistaking an error for "missing" could overwrite a key without keeping a copy.
exists() {
  local error
  if error="$(az keyvault secret show --vault-name "$1" --name "$2" --query id -o tsv 2>&1 >/dev/null)"; then
    return 0
  fi

  if grep -q -E "SecretNotFound|was not found" <<< "$error"; then
    return 1
  fi

  echo "Could not read $1/$2 (check access and the firewall): $error" >&2
  exit 1
}

# True when two secrets hold the same value (compared through private temp files; nothing is printed).
same_value() {
  local left="$workdir/left" right="$workdir/right" result=1
  rm -f "$left" "$right"
  if az keyvault secret download --vault-name "$1" --name "$2" --file "$left" --encoding utf-8 >/dev/null 2>&1 \
    && az keyvault secret download --vault-name "$3" --name "$4" --file "$right" --encoding utf-8 >/dev/null 2>&1 \
    && cmp -s "$left" "$right"; then
    result=0
  fi

  rm -f "$left" "$right"
  return $result
}

# Fail fast when either vault is unreachable (IP not allowed yet, role assignments still propagating).
for vault in "$web_vault" "$worker_vault"; do
  az keyvault secret list --vault-name "$vault" --maxresults 1 --output none \
    || { echo "Cannot access Key Vault $vault yet (admin_ip_ranges, key_vault_admin_object_ids, or wait a few minutes for role assignments)." >&2; exit 1; }
done

# Writes a value through a private temp file; never echoes it.
put() {
  local vault="$1" name="$2" value="$3" file="$workdir/value"
  ( umask 077; printf '%s' "$value" > "$file" )
  az keyvault secret set --vault-name "$vault" --name "$name" --file "$file" --encoding utf-8 --output none
  rm -f "$file"
  echo "  set $vault/$name"
}

copy() { # copies a secret between names/vaults without exposing it on the terminal
  local from_vault="$1" from_name="$2" to_vault="$3" to_name="$4" file="$workdir/copy"
  az keyvault secret download --vault-name "$from_vault" --name "$from_name" --file "$file" --encoding utf-8
  az keyvault secret set --vault-name "$to_vault" --name "$to_name" --file "$file" --encoding utf-8 --output none
  rm -f "$file"
  echo "  copied $from_vault/$from_name -> $to_vault/$to_name"
}

# Env var value or hidden prompt, printed to stdout for capture. Everything shown to the operator goes to stderr
# and reports only the length, never the value.
value_for() {
  local env_name="$1" label="$2" value="${!1:-}" trimmed answer attempt
  if [[ -n "$value" ]]; then
    echo "    using \$$env_name from the environment" >&2
  else
    for attempt in 1 2 3; do
      # IFS= keeps the input exactly as entered; whitespace is only removed after asking, below.
      IFS= read -r -s -p "  $label (hidden; paste or type, then Enter): " value </dev/tty
      echo >&2
      [[ -n "$value" ]] && break
      echo "    nothing received (attempt $attempt of 3)" >&2
    done
  fi
  [[ -n "$value" ]] || { echo "A value for $label is required; nothing was changed for it." >&2; exit 1; }

  # Spaces, tabs or a carriage return at either end are almost always paste artifacts.
  trimmed="${value#"${value%%[![:space:]]*}"}"
  trimmed="${trimmed%"${trimmed##*[![:space:]]}"}"
  if [[ "$trimmed" != "$value" ]]; then
    echo "    it starts or ends with whitespace: ${#value} characters, ${#trimmed} without it" >&2
    read -r -p "    remove the whitespace? [Y/n] " answer </dev/tty
    [[ "$answer" =~ ^[Nn] ]] || value="$trimmed"
  fi
  [[ -n "$value" ]] || { echo "A value for $label is required; nothing was changed for it." >&2; exit 1; }

  echo "    received ${#value} characters" >&2
  printf '%s' "$value"
}

# Sets a secret if it is missing (or explicitly selected with --update).
ensure() {
  local vault="$1" name="$2" env_name="$3" label="$4" value
  if exists "$vault" "$name" && ! selected_for_update "$name"; then
    echo "  keep $vault/$name"
    return
  fi

  # Assigned on its own line so that a failed prompt stops the script instead of storing an empty secret.
  value="$(value_for "$env_name" "$label")"
  put "$vault" "$name" "$value"
}

# Like ensure, for a secret the apps can run without (Google sign-in): a missing one is only set with --google or
# when named explicitly with --update.
optional() {
  local vault="$1" name="$2" env_name="$3" label="$4" value selected=false update
  for update in ${updates[@]+"${updates[@]}"}; do
    [[ "$update" == "$name" ]] && selected=true
  done
  if exists "$vault" "$name"; then
    if ! selected_for_update "$name"; then
      echo "  keep $vault/$name"
      return
    fi
  elif ! $google && ! $selected; then
    echo "  skip $vault/$name (optional; add it with --google)"
    return
  fi

  value="$(value_for "$env_name" "$label")"
  put "$vault" "$name" "$value"
}

random_key() { head -c 32 /dev/urandom | base64 | tr -d '\n'; }

# Shared random keys: identical in both vaults (the API and the workers must agree on them).
shared_key() {
  local name="$1" rotate="$2"
  if exists "$web_vault" "$name" && ! $rotate; then
    echo "  keep $web_vault/$name"
  else
    if exists "$web_vault" "$name"; then
      copy "$web_vault" "$name" "$web_vault" "${name}Previous"
      copy "$web_vault" "$name" "$worker_vault" "${name}Previous"
    fi
    put "$web_vault" "$name" "$(random_key)"
  fi

  # Copy only when the worker vault differs, so routine runs do not pile up secret versions and audit events.
  if exists "$worker_vault" "$name" && same_value "$web_vault" "$name" "$worker_vault" "$name"; then
    echo "  keep $worker_vault/$name (in sync)"
  else
    copy "$web_vault" "$name" "$worker_vault" "$name"
  fi
}

echo "Shared keys"
shared_key "Crypto--WebKey" "$rotate_web"
shared_key "Temporal--PayloadKey" "$rotate_payload"

echo "Web vault ($web_vault)"
ensure "$web_vault" "Temporal--ApiKey" TEMPORAL_API_KEY_WEB "Temporal API key (web service account)"
ensure "$web_vault" "Auth--GitHubClientId" GITHUB_CLIENT_ID "GitHub OAuth client id"
ensure "$web_vault" "Auth--GitHubClientSecret" GITHUB_CLIENT_SECRET "GitHub OAuth client secret"
optional "$web_vault" "Auth--GoogleClientId" GOOGLE_CLIENT_ID "Google OAuth client id"
optional "$web_vault" "Auth--GoogleClientSecret" GOOGLE_CLIENT_SECRET "Google OAuth client secret"
# The web app refuses to start with only one of the two (it validates both-or-neither at startup).
google_id=false; google_secret=false
exists "$web_vault" "Auth--GoogleClientId" && google_id=true
exists "$web_vault" "Auth--GoogleClientSecret" && google_secret=true
if [[ "$google_id" != "$google_secret" ]]; then
  echo "Only one of Auth--GoogleClientId and Auth--GoogleClientSecret is set; the web app will not start like this." >&2
  echo "Run again with --google to set both (or delete the one that is set)." >&2
  exit 1
fi
if az keyvault key show --vault-name "$web_vault" --name dataprotection --query key.kid -o tsv >/dev/null 2>&1; then
  echo "  keep $web_vault/keys/dataprotection"
else
  # Wraps the ASP.NET Core Data Protection key ring (session/antiforgery cookie keys); never exportable.
  az keyvault key create --vault-name "$web_vault" --name dataprotection --kty RSA --size 3072 --ops wrapKey unwrapKey --output none
  echo "  created $web_vault/keys/dataprotection"
fi

echo "Worker vault ($worker_vault)"
ensure "$worker_vault" "Crypto--ArchivePassword" ARCHIVE_PASSWORD "Archive password (protect.ps1)"
ensure "$worker_vault" "Archive--AccessKeyId" WASABI_ACCESS_KEY_ID "Wasabi access key id"
ensure "$worker_vault" "Archive--SecretAccessKey" WASABI_SECRET_ACCESS_KEY "Wasabi secret access key"
ensure "$worker_vault" "Gemini--ApiKey" GEMINI_API_KEY "Gemini API key"
ensure "$worker_vault" "Temporal--ApiKey" TEMPORAL_API_KEY_WORKER "Temporal API key (worker service account)"

cat <<EOF
Done. The apps read their secrets at startup: restart both so they use the new values
(docs/operations.md, "Restarting the apps"):
  for app in ca-photo-web ca-photo-worker; do
    az containerapp revision restart -g rg-photo-prod -n \$app \\
      --revision \$(az containerapp show -g rg-photo-prod -n \$app --query properties.latestRevisionName -o tsv)
  done
EOF
