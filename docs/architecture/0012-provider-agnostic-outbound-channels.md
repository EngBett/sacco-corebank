# ADR 0012: Provider-agnostic outbound email/SMS, Mailpit and a mock SMS gateway for local dev

## Status
Accepted (2026-09-15)

## Context
`Sacco.Modules.Notifications.OutboundDispatcher` already sat behind two interfaces, `ISmsSender` and `IEmailSender`
(ADR 0009), with a single hardcoded `Live` implementation each: Africa's Talking for SMS, SMTP via MailKit for email.
That was enough to demo the concept but two things were missing:

1. A SACCO's SMS gateway is a commercial choice, not a technical one — one may already run on Africa's Talking,
   another on a direct Safaricom or Airtel bulk-SMS contract, another may prefer routing notifications through
   WhatsApp Business, and a pilot integration might use Twilio. The system needs to support switching without a
   code change, and the CLAUDE.md non-negotiable that every module be demoable without production credentials
   applies here as much as anywhere.
2. `Sandbox` mode (the default) writes the outbound row and stops — nobody sees what would have been sent. That
   was fine for automated tests, but poor for local development and demos: you cannot show a stakeholder "here is
   the SMS a member would receive" without wiring up a real gateway.

## Decision
1. **`IEmailSender` stays a single interface with no provider switch**, because SMTP already *is* the
   provider-agnostic layer — `SmtpEmailSender` (MailKit, MIT) speaks the protocol, not a vendor's API. Any
   SMTP-compatible service (SES, SendGrid, a SACCO's own mail server, or a local catcher) is a
   `Notifications:Email:Smtp:Host/Port/UseStartTls` configuration change.
2. **`ISmsSender` gains a `Notifications:Sms:Provider` selector.** `NotificationsModule.RegisterLiveSmsSender`
   switches on it to register exactly one concrete sender — `AfricasTalkingSmsSender` (default), `TwilioSmsSender`,
   `WhatsAppCloudApiSmsSender`, `SafaricomSmsSender`, `AirtelSmsSender`, or `MockSmsSender` — all living in
   `Channels/SmsProviders.cs`, all implementing the same three-member interface. Adding a seventh gateway is:
   implement `ISmsSender`, add its settings block to `SmsSettings`, add one `case` to the switch. Nothing else in
   the system — `OutboundDispatcher`, the outbox endpoints, the portal — knows or needs to know which gateway is
   behind the interface. `Twilio` and `WhatsApp` are written to stable, publicly documented APIs; `Safaricom` and
   `Airtel` SMS have no public sandbox the way Daraja does, so those two are starter scaffolds modelled on their
   sibling payment integrations (`DarajaMpesaProvider`, `AirtelMoneyProvider`) — confirm the exact endpoint and
   payload against the SACCO's actual contract before go-live, exactly as every live payment provider's docstring
   already asks.
3. **A local mock SMS gateway (`mocked-providers/mocked-sms-server`) plays the same role for SMS that Mailpit
   plays for email**: a generic REST inbox (`POST /sms/send`, `GET /api/messages`) with a small polling web UI at
   `/`, requiring no vendor credentials. `MockSmsSender` talks to it exactly like any other gateway — it is
   selected via `Notifications:Sms:Provider=Mock`, not a special code path.
4. **Local dev defaults changed from `Sandbox` to `Live` pointed at these two tools.** `docker-compose.yml` starts
   `mailpit` (`axllent/mailpit`, SMTP on 1025, web UI on 8025) and `mocked-sms` alongside `postgres` with no
   profile — the same "just works" tier as the database — and the containerized `api` service's environment points
   at them by service name. `appsettings.Development.json` points at the same tools by `localhost` for the
   uncontainerized `dotnet run` workflow. Both defaults are safe because delivery is best-effort by design
   (`OutboundDispatcher.SendAsync` catches and logs, never throws): if neither tool happens to be running, sends
   fail gracefully onto the outbox row instead of breaking the workflow that raised the notification.
5. **The `Testing` environment (`ApiFactory.UseEnvironment("Testing")`) and the seed tool are untouched** — neither
   loads `appsettings.Development.json`, so both keep the code-level `Sandbox` default and stay deterministic and
   credential-free. `Sandbox` also remains the production-safe default: a tenant only gets Mailpit/mock delivery in
   `Development`; production sets `Mode=Live` with real provider credentials explicitly, per the cutover runbook.

## Consequences
- A demo now shows a real (if fake) email in Mailpit's inbox and a real (if fake) SMS in the mock gateway's inbox,
  instead of an outbox row nobody looks at — closer to what SASRA examiners or a pilot SACCO's staff would expect
  to see demonstrated.
- Switching a live tenant's SMS gateway is a configuration change (`Notifications:Sms:Provider` plus that
  provider's settings block) reviewed and deployed like any other config change — never a code change or a
  redeploy of `Sacco.Modules.Notifications`.
- `Safaricom`/`Airtel` SMS carry the same "confirm before go-live" caveat as `DarajaMpesaProvider` did before it
  was checked against a reference implementation and mock server (see `docs/integrations/payment-providers.md`);
  the same treatment — a reference doc or mock under `reference/`/`mocked-providers/` — is the natural next step
  once a SACCO actually contracts with either telco for bulk SMS.
