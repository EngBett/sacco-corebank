# ADR 0005: Open.IdentityServer for authentication, ASP.NET Core policies for authorization

## Status
Accepted

## Context
Duende IdentityServer (the successor to IdentityServer4) is commercially licensed.
Open.IdentityServer (Rock Solid Knowledge's revival of the Apache 2.0 IdentityServer4
codebase) commits to keeping its core OAuth2/OIDC platform free and open-source permanently,
funded by optional paid add-ons (AdminUI, SAML, SCIM, etc.) rather than the core itself. We
do not currently need decoupled multi-app SSO/federation, but keeping token issuance behind a
standard OIDC server rather than hand-rolled auth is still worth the modest complexity for
future-proofing (mobile app, potential future SSO needs).

## Decision
Use Open.IdentityServer for authentication/token issuance. Authorization (what a
token-holder is allowed to do) is a separate concern, owned entirely by the backend:
permission-based, policy-driven (ASP.NET Core's built-in, free authorization framework), with
roles as configurable bundles of granular permissions. Tokens carry light role claims only;
full permission resolution happens server-side per request against a cached lookup, so
revocation is immediate rather than waiting for token expiry.

## Consequences
- No commercial licensing dependency for authentication.
- Authorization logic is fully owned and portable — not coupled to whichever IdP is in use.
- Open.IdentityServer is a young project (revived 2025/2026) with a smaller track record
  than Duende — monitor its health and keep the OIDC integration standard enough that
  swapping providers later (e.g. to OpenIddict) would not require touching the authorization
  model.
