# 0006. GitHub OAuth with a numeric-id allowlist and cookie sessions

- Status: Accepted (2026-11-19); amended by [0012](0012-google-sign-in-for-invited-members.md) (2026-11-20):
  admins still sign in this way, family members are invited in the app and sign in with Google

## Context

The site is for a handful of family members who sign in with GitHub. Login names can be changed and
re-registered by someone else; tokens kept in browser storage are readable by any injected script.

## Decision

- GitHub OAuth authorization-code flow with PKCE and an **empty scope** (identity only).
- Authorization by an allowlist of **numeric GitHub ids** with a role (Member/Admin), configured in Terraform
  (`github_users`) and re-checked on every request.
- The server keeps the session in an encrypted, `__Host-` prefixed, HttpOnly, Secure, SameSite=Lax cookie;
  the GitHub access token is discarded. Unsafe API calls need an antiforgery token sent in a header.
- Sessions slide for 8 hours of activity but end 24 hours after sign-in regardless (absolute lifetime).
- Everything a user owns (uploads, prepared downloads) and the audit trail record the numeric GitHub id; the
  login is kept for display only. (Amended 2026-11-19 after review: ownership previously used the login.)

## Consequences

- No tokens in JavaScript-accessible storage; XSS cannot exfiltrate a session token.
- Adding a user is a Terraform change (and an app restart via the new revision). Since ADR 0012 this applies
  only to admins (and any GitHub member kept in Terraform); family members are invited in the app.
- Depends on GitHub availability for sign-in; 2FA on allowlisted accounts is strongly recommended.
