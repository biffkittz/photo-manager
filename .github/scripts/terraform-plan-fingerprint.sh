#!/usr/bin/env bash
# Prints a fingerprint of what a saved Terraform plan would do: every resource change that is not a no-op
# (address, actions, values before and after, imports) and every changed output, as canonical JSON, hashed.
# Two plans with the same fingerprint make exactly the same changes. The JSON plan can contain sensitive values,
# so it is only ever hashed here, never printed.
#
# Usage (from the Terraform root, after init): terraform-plan-fingerprint.sh <saved plan file>
set -euo pipefail

plan="${1:?usage: terraform-plan-fingerprint.sh <saved plan file>}"

terraform show -json "$plan" |
  jq --sort-keys --compact-output '{
    resource_changes: [
      (.resource_changes // [])[]
      | select(.change.actions != ["no-op"] or .change.importing != null)
      | {
          address,
          actions: .change.actions,
          before: .change.before,
          after: .change.after,
          after_unknown: .change.after_unknown,
          importing: .change.importing
        }
    ] | sort_by(.address),
    output_changes: ((.output_changes // {}) | with_entries(select(.value.actions != ["no-op"])))
  }' |
  sha256sum |
  cut -d ' ' -f 1
