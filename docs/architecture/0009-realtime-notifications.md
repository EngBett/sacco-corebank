# ADR 0009: In-app notifications over SignalR, authenticated with hub tickets

## Status
Accepted (2026-09-14)

## Context
Staff need to learn promptly when something needs them (a journal awaiting a checker, a loan awaiting the
committee) or when something they started concluded (approved, rejected, paid). The portal keeps API tokens
strictly server-side (ADR 0006): browser JavaScript never holds a bearer token, and a WebSocket upgrade
cannot carry custom headers anyway. Polling through the BFF would work but scales poorly and feels slow.

## Decision
1. **A `Notifications` module owns persisted, per-recipient notifications** (`notifications.notifications`,
   tenant-scoped with RLS). Domain modules raise them through the `INotifier` contract in `Sacco.Shared`
   at the same points they write the audit trail. Audiences are either explicit users or "holders of a
   permission" (resolved by the Identity module via `IUserDirectory`); the actor is always excluded from
   permission fan-out. The audit log remains the record; a notification is a nudge, never evidence.
2. **Delivery is persist-then-push.** Rows are committed first, then pushed over a SignalR hub
   (`/hubs/notifications`) to a per-(tenant, user) group. Nothing is lost when nobody is connected, and a
   live session never sees something the next page load would not confirm. SignalR ships in ASP.NET Core
   (MIT); no extra server dependency. Multi-instance deployments need a backplane (Redis) before the second
   API replica — noted in the cutover runbook.
3. **Hub tickets, not bearer tokens, open the hub.** The browser asks the BFF for a ticket; the BFF calls
   `POST /api/notifications/hub-ticket` with the user's real token; the API mints a 15-minute JWT with the
   IdentityServer signing key but audience `sacco-hub`. A second JWT bearer scheme (`HubTicket`) accepts it
   from the `access_token` query string on `/hubs/*` only. The REST API validates audience `sacco-api`, so a
   ticket is useless against it, and the API token is never accepted by the hub. Reads and acknowledgements
   (`/api/notifications/*`) stay on the REST API behind the BFF; the hub is server-to-client only.
4. **CORS is limited to the hub.** `Cors:AllowedOrigins` lists the portal origin(s) (wildcard tenant
   subdomains allowed); it is applied to the hub endpoint only, so the rest of the API stays same-origin
   through the BFF.

## Consequences
- Every new approval workflow must raise a notification for the checkers and, on decision, for the maker.
  The integration tests treat "checkers are notified, maker is not" as part of the maker-checker contract.
- The seed tool produces notifications as a side effect of driving the real workflows, so every demo role
  signs in to a realistic inbox without a separate fixture.
- Tickets are short-lived and single-purpose; leaking one exposes fifteen minutes of a user's own
  notification stream and nothing else.
