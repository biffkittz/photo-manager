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
#   --update <secret>      Re-prompt for one secret that already exists (e.g. Gemini--ApiKey)
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
# Secret values are passed to az through permission-600 temporary files, never on a command line (which
# would expose them in shell history and process listings), and are never printed.
# After changing secrets, restart the apps: az containerapp revision restart (docs/operations.md).
# =================================================================================================
set -euo pipefail

web_vault=""; worker_vault=""; rotate_web=false; rotate_payload=false; update=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --web-vault) web_vault="$2"; shift 2 ;;
    --worker-vault) worker_vault="$2"; shift 2 ;;
    --rotate-web-key) rotate_web=true; shift ;;
    --rotate-payload-key) rotate_payload=true; shift ;;
    --update) update="$2"; shift 2 ;;
    -h|--help) sed -n '3,26p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done
[[ -n "$web_vault" && -n "$worker_vault" ]] || { echo "--web-vault and --worker-vault are required (terraform output key_vaults)." >&2; exit 1; }
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

value_for() { # env var value or hidden prompt
  local env_name="$1" label="$2" value="${!1:-}"
  if [[ -z "$value" ]]; then
    read -r -s -p "$label: " value </dev/tty; echo >&2
  fi
  [[ -n "$value" ]] || { echo "A value for $label is required." >&2; exit 1; }
  printf '%s' "$value"
}

# Sets a secret if it is missing (or explicitly selected with --update).
ensure() {
  local vault="$1" name="$2" env_name="$3" label="$4"
  if exists "$vault" "$name" && [[ "$update" != "$name" ]]; then
    echo "  keep $vault/$name"
    return
  fi
  put "$vault" "$name" "$(value_for "$env_name" "$label")"
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

echo "Done. Restart both Container Apps so they reload their secrets."
