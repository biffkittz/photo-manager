#!/usr/bin/env bash
# Fails with a clear message when GitHub Actions variables or secrets a job needs are not configured. An unset
# `vars.X` or `secrets.X` silently becomes an empty string, which otherwise surfaces later as an obscure error
# (for example azure/login: "Not all values are present").
#
# Usage: require-config.sh NAME...   Each NAME is a step environment variable holding the setting of the same
# name (env: NAME: ${{ vars.NAME }} or ${{ secrets.NAME }}). Values are never printed.
set -euo pipefail

missing=()
for name in "$@"; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done

if (( ${#missing[@]} > 0 )); then
  echo "::error::Not configured in GitHub: ${missing[*]}. See docs/deployment.md, step 2: variables go under Settings > Secrets and variables > Actions > Variables (not Secrets); write-capable secrets on the production environment."
  exit 1
fi

echo "Configured: $*"
