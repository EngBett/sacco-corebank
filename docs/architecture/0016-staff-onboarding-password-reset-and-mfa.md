# ADR 0016: Staff invitations, self-service password reset and mandatory authenticator-app 2FA

## Status
Accepted (2026-09-17)

## Context
Staff accounts were created by a System Admin who typed the new user's password and passed it on, and an admin reset a
forgotten password by typing a new one. Nothing else protected the account: a username and password were enough to sign
in to the portal, which can approve loans, journals and withdrawals.

The product owner asked for three things:
1. A manager can add a portal user. The user gets an email with a link to activate the account and choose a password.
2. Staff can reset a forgotten password themselves.
3. Every staff member must set up two-step verification (OTP) with Google or Microsoft Authenticator at their first sign-in.

Two points were settled with the product owner:
- A Branch Manager's invitation needs a System Admin's approval before the email goes out (maker-checker). Without it a
  manager could create an account more powerful than their own.
- Demo staff accounts are not pre-enrolled. Whoever signs in as a demo user first scans the QR code.

## Decision

### Invitations (maker-checker), not admin-set passwords
- **New permission** `admin.users.invite` (Branch Manager) lets a user *propose* a new staff user: user name, display name,
  email, optional phone and roles. The proposal is stored as `identity.staff_invitations` with status `PendingApproval`,
  and holders of `admin.users.manage` are notified.
- **Approval** by a *different* holder of `admin.users.manage` (`MakerChecker.EnsureDistinct`):
  - creates the `StaffUser` with no password and `ActivatedAt = null`;
  - emails the activation link;
  - moves the invitation to `Sent`.
- **Admins invite directly:** a proposal from someone who already holds `admin.users.manage` is approved in the same step.
- **Other actions:** reject (with a reason), resend (issues a new link and invalidates the old one) and revoke (deactivates
  the unactivated account and kills its link).
- **No login until activated:** a user who hasn't activated fails sign-in exactly like an unknown user.
- **Removed:** the endpoints that let an admin set a user's password (`POST /api/admin/users`, `…/reset-password`). An
  administrator can now only *send a reset link* (`…/send-password-reset`) and never knows the password.

### Single-use link tokens
- **One table:** activation and password-reset links share `identity.staff_account_tokens`.
- **Tokens:** 32 random bytes, base64url. Only the SHA-256 hash is stored, so reading the database doesn't yield working
  links.
- **Single use:** each token has a purpose and an expiry (activation 72 h, reset 60 min), and is consumed on use. Issuing
  a new token invalidates earlier unused ones of the same purpose.
- **Tenant in the link:** links carry the tenant slug (`/account/activate?tenant=demo&token=…`), because `/account/*` is
  tenant-agnostic and row-level security needs the tenant bound before the token can be looked up.
- **Forgot password:**
  - always answers the same way, whether or not the account exists;
  - is throttled per account (one email every 2 minutes);
  - doesn't lift a deactivation;
  - does clear a sign-in lockout, since proving control of the mailbox is enough to try again;
  - sends a "your password was changed" email once the reset succeeds.

### Mandatory TOTP two-step verification for staff
- **Where it's enforced:** only the interactive sign-in (`/account/login`), which is the only way staff reach the portal
  (authorization code + PKCE).
- **After a correct password:**
  - no IdentityServer session is created;
  - the browser gets a 10-minute, DataProtection-protected `sacco.mfa` cookie (path `/account`) naming the user, tenant
    and return URL;
  - the browser is sent to set-up (`/account/mfa/setup`) if the user has no authenticator, otherwise to the challenge
    (`/account/mfa`).
- **Set-up:**
  - the page shows a QR code (QRCoder, MIT, rendered as inline SVG) and the key grouped for manual entry;
  - the pending secret is kept until confirmed, so reloading the page doesn't change it;
  - confirming with a valid code enables TOTP, signs the user in and shows ten one-time recovery codes once. The codes
    are stored hashed.
- **Codes:**
  - RFC 6238 (HMAC-SHA1, 6 digits, 30 s), implemented in-house (`Totp`) and tested against the RFC vectors;
  - one step of clock drift either side is accepted;
  - the last used step is stored, so a code can't be replayed.
- **Lockout:** wrong codes count towards the same 5-attempt lockout as wrong passwords. With 2FA on, a correct *password*
  no longer resets the failure counter; only a correct code does. Otherwise alternating a known password with code
  guesses would never trigger the lockout.
- **Lost phone:** a holder of `admin.users.manage` resets a user's 2FA (`POST /api/admin/users/{id}/reset-mfa`; never
  their own). The user is emailed and enrols again at their next sign-in.
- **Password grant:** the `sacco-cli` demo/test client could sign staff in with a password alone, so it is refused for
  staff in Production. Members (mobile) are unaffected; they have their own device OTP (ADR 0008).

### Secrets at rest
- **Encryption:** authenticator secrets are encrypted with AES-256-GCM (`TotpSecretProtector`) under a key from
  `Identity:Mfa:EncryptionKeys` / `ActiveKeyId`, held outside the database. A database copy alone can't generate codes.
- **Rotation:** the stored value is prefixed with its key id; add a new key, make it active, and keep old ids listed.
- **Why not the ASP.NET key ring:** it isn't persisted durably in this deployment, so secrets encrypted with it would
  become unreadable on restart.
- **Production checks:** start-up fails without a non-`dev` active key, and without an https `Identity:Accounts:PublicOrigin`
  and a `PortalUrl`, since both go into emails.

### Pages and limits
- **Pages:** all account pages (sign-in, set-up, challenge, recovery codes, activate, forgot, reset) are server-rendered in
  the shadcn `login-03` layout (`AccountPages`), with no JavaScript and `no-referrer`, so tokens in the URL don't leak to
  other sites.
- **Rate limiting:** every credential-bearing POST uses the per-IP `account` policy
  (`RateLimiting:AccountPermitsPerMinute`, default 20).
- **Return URLs:** only same-site paths or IdentityServer-issued URLs are accepted.

## Consequences
- **Staff sign-in takes a code;** the first sign-in adds a one-off set-up. Demo users enrol on first use (see
  `backend/seed/README.md`).
- **Seed data:** one invitation proposed by the Branch Manager waits for the System Admin, so maker-checker onboarding is
  demoable. Emails go to Mailpit locally (http://localhost:8025).
- **Existing users:** the migration marks everyone who already had a password as activated. Tenant isolation is FORCEd on
  existing databases, so that backfill lifts the force for the one statement and restores it.
- **Scripts:** anything that relied on the password grant for staff in Production must move to the interactive flow.
- **Not in this change:**
  - users regenerating their own recovery codes;
  - WebAuthn/passkeys;
  - remembering a trusted browser;
  - SMS as a second factor, deliberately left out as weaker than an authenticator app.
