# 0012. Family members are invited in the app and sign in with Google

- Status: Accepted (2026-11-20)

## Context

ADR 0006 allows only GitHub accounts listed in Terraform. Most family members have no GitHub account, and
adding anyone meant a Terraform change and apply that only the operator can make. The admins (who can run
maintenance jobs) are one or two people who already use GitHub.

## Decision

- **Admins stay in configuration**: GitHub sign-in with the numeric-id allowlist (`github_users`) of ADR 0006.
  Nothing stored in the database can create an admin, so a database problem or a compromised web tier cannot
  grant admin rights or lock the admins out.
- **Family members are invited from the People page** and sign in with **Google** (OAuth authorization code +
  PKCE, scopes `openid email` only; the Google access token is discarded). Invited people are always members.
- An **invite link** carries a 256-bit random token in the URL **fragment** (never sent to a server by the
  browser, so it appears in no request line or log); the invite page sends it once in a POST body. The database
  stores only its SHA-256 hash. A link works once, for 7 days; a new link replaces the old one. An admin can limit
  an invite to one Google address, which Google must report as verified.
- Between the invite page and Google, the opened invite (member id + token hash) is kept in a 30-minute,
  Data-Protection-encrypted, `__Host-` HttpOnly cookie, and then inside the encrypted OAuth state. It is checked
  against the database again when the person returns from Google.
- Accepting links the Google account's permanent id (`sub`) to the member, once. Email addresses are never used
  to recognize anyone. One Google account belongs to at most one current member (unique index).
- Every request of a member's session re-checks the database: not removed, and still linked to the Google
  account the session was signed in with. Removal and "invite again" therefore end sessions immediately.
- Members get **negative** user ids (an identity sequence counting down from -1), so ownership columns and the
  audit trail keep one `long` user id that can never collide with a (positive) GitHub id.
- Google is **optional**: without the `Auth--GoogleClientId`/`Auth--GoogleClientSecret` secrets, the handler is
  not registered, invites cannot be created, and members' sessions end.
- The web tier's database role may read, insert and update `family_members` (never delete; removal sets
  `removed_at`); the workers have no access to it.

## Consequences

- Adding or removing a family member is a click on the People page, effective at once, and audited.
- One database lookup per request of a member's session (a primary-key lookup).
- Whoever holds an unlimited, unused invite link can accept it; mitigations: one use, 7-day expiry, the optional
  email limit, the linked account's email shown to admins, and the audit trail.
- A member's access is as strong as their Google account's security (recommend 2-Step Verification).
- Depends on Google's availability for members' sign-in (admins can still sign in with GitHub).
- A Google OAuth client must be created and kept (operations.md, "Inviting family members").
