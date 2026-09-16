# ADR 0013: Nightly branded PDF digest, rendered via headless Chromium, emailed to configured recipients

## Status
Accepted (2026-09-16)

## Context
Staff and board members who don't want to log into the portal every day still need to see the SACCO's
prudential position daily: capital adequacy, liquidity, and loan portfolio quality. The request was
specific: a PDF, generated automatically every midnight, emailed to a list the admin controls, and it has
to look professional — the tenant's own branding, not a generic system report.

Two things this needed that the platform didn't have yet:
1. A way to turn HTML into a PDF.
2. A way for a module other than Notifications to send an email with an attachment, without depending on
   Notifications' internals (module boundary rule, backend/CLAUDE.md).

## Decision

### HTML-to-PDF: PuppeteerSharp (MIT wrapper around Chromium, BSD)
The brief asked for the lightest, most efficient *HTML-to-PDF* tool, so this ruled out pure C# drawing
libraries with no HTML/CSS parser (`PdfSharp`) and license-gated document builders (`QuestPDF`'s
Community license is free only under a revenue threshold — the same category of risk ADR 0003 already
flagged for MassTransit, not worth repeating for a PDF library). That leaves real HTML renderers:

- **DinkToPdf / wkhtmltopdf**: genuinely the smaller footprint (~40 MB native binary vs. Chromium's
  ~170–300 MB), but the underlying engine (Qt WebKit) has been effectively frozen since ~2020, its CSS
  support is dated, and it ships as an LGPL native binary that historically causes real Docker packaging
  pain (wrong `.so` per distro/glibc).
- **PuppeteerSharp**: heavier in raw megabytes, but it's real, current Chromium — full modern CSS with no
  compromise on the "professional" look — actively maintained, MIT-licensed, and has a well-trodden Docker
  deployment path.

Given this runs once per tenant per night (a batch job, not a hot path), the raw megabyte difference is a
one-time image-size cost, not a runtime efficiency problem — so rendering fidelity and long-term
maintenance risk won a "lightest by MB" tie-break. `HtmlToPdfRenderer` launches one browser process per
report and closes it (no shared state between tenants); production points it at the system Chromium baked
into `backend/Dockerfile` via `Reporting:DailyDigest:ChromiumExecutablePath` — the container never
downloads a browser at runtime. Local dev, where that's unset, falls back to PuppeteerSharp's own
`BrowserFetcher`, which is convenient for a laptop and wrong for anything locked down.

### Cross-module email: promote `IEmailSender` to `Sacco.Shared`
`Sacco.Modules.Reporting` needs to send an email; `Sacco.Modules.Notifications` owns email. The module
boundary rule says a module depends only on `Sacco.Shared` contracts, never another module's internals —
exactly the shape `INotifier` already solved for in-app notifications (ADR 0009). `IEmailSender` (with an
`EmailAttachment` list, so any module can attach a generated file) now lives in `Sacco.Shared.Notifications`
alongside `INotifier`; `Sacco.Modules.Notifications` implements it (`SandboxEmailSender` for demos and
tests, `SmtpEmailSender` for `Live` mode — see ADR 0012). `OutboundDispatcher`'s own per-notification emails
call the same interface with no attachments, so nothing about existing notification delivery changed.

A parallel, smaller change: `ITenantEnumerator` (already the Shared contract background jobs use to loop
over tenants, see `LendingMaintenanceService`) gained `GetBrandingAsync(tenantId)`, so a scheduled job can
put a tenant's logo and name on something it produces without depending on the Platform module directly.

### The scheduler
`DailyDigestScheduler` is `LendingMaintenanceService`'s shape exactly: a `BackgroundService`, one DI scope
per tenant, one tenant's failure never stops the others, `Reporting:DailyDigest:RunAtUtc` (default
midnight UTC) and `Enabled` (off in the seed tool and integration tests, on in the API host). A tenant with
no recipients configured is a no-op, not an error — most demo/trial tenants will have none until an admin
adds one.

### What the digest actually contains
The same figures `StatutoryReportService` already computes for on-demand viewing — financial position,
capital adequacy, liquidity, loan portfolio quality — reused with zero duplication, rendered through
`DailyDigestHtml` (a self-contained HTML string, inline CSS only, no external stylesheet or font fetch, so
rendering never depends on the tenant's own network reachability) and `HtmlToPdfRenderer`.

### Admin surface
`Permissions.Reporting.RecipientsManage` gates `GET/POST /api/reporting/daily-digest/recipients`,
`DELETE .../recipients/{id}`, `GET .../preview.pdf` (see today's PDF without emailing anyone — the demo and
QA affordance) and `POST .../run` (send it right now rather than waiting for midnight). The portal's
Admin → Daily PDF digest page wraps all four. A recipient is a plain email + name, not a staff user — a
board member who only wants the email needs no portal login.

## Consequences
- Adding an attachment to any future outbound email (a statement, an export) is now "call the Shared
  `IEmailSender` with an `EmailAttachment`" from any module — no new plumbing.
- The API host's task memory was raised (`infrastructure/modules/app/main.tf`, 1024 → 1536 MB) to give
  headless Chromium headroom above the .NET runtime's own baseline; digests render sequentially per tenant,
  never in parallel, so this is a one-off in a container that also has to run the app, not a peak concern.
- `backend/Dockerfile` installs `chromium` via apt in the `api` stage — the image is larger; this is the
  accepted trade-off above.
- A tenant only gets Live email delivery in `Development`/production once `Notifications:Email:Mode=Live`
  is configured (ADR 0012); in the default `Sandbox` mode the digest still "sends" (the sandbox sender
  returns a fake reference) so the whole flow is demoable and testable without SMTP credentials.
