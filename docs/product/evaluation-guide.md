# Evaluating this platform in an afternoon

For the developer or ICT manager a SACCO asks to "look at the code before we commit". It assumes
nothing beyond Docker, .NET 10 and Node 20, and it needs no production credentials, no Safaricom
account and no conversation with a bank.

Attach this to the listing as a previewable Markdown asset: it lets a technical buyer judge the
product before asking for access, which is exactly the lead you want.

---

## 1. Start the stack (about 10 minutes)

```bash
docker compose up -d postgres mailpit mocked-sms   # database, mail catcher, SMS gateway
dotnet run --project backend/seed/Sacco.Seed       # migrate + seed a complete demo SACCO
dotnet run --project backend/src/Sacco.Api         # API, OIDC server, API reference at :5000/scalar
```

Then, in separate terminals:

```bash
cd frontend/portal      && npm install && npm run dev   # staff portal   → :3000
cd frontend/public-site && npm install && npm run dev   # public website → :3001
```

The seed is idempotent: re-run it as often as you like, or `-- --reset` to rebuild from scratch.

## 2. Sign in and look for the things that are hard to fake

Staff logins are in `backend/seed/README.md`. Sign in as `admin`, then as `manager` — you will be
asked to enrol an authenticator app on first sign-in, because that is how the system treats every
staff account.

Six things worth opening, each chosen because it is expensive to fake and easy to check:

**The trial balance** (Ledger → Trial balance). Switch between FOSA, BOSA and consolidated. Each
segment balances on its own, and the inter-segment clearing pair nets to zero. This is the test a
SACCO's auditor applies, and it is where two-system designs come apart.

**Reconciliation** (Ledger → Reconciliation). It recomputes running balances from journal lines and
reports drift. Most systems cannot answer this question about themselves.

**A loan with guarantors** (Loans → any disbursed loan). Look at the approval trail: who originated,
who appraised, who approved, who disbursed, and the rule that no two of those may be the same person
above the product threshold. Then open a guarantor's savings account and see the hold sitting against
their available balance — the exposure is arithmetic, not a note in a field.

**The audit trail** (Admin → Audit). Filter by action prefix, actor or outcome. Open an entry: it
carries the actor's name as it was at the time, the IP, the user agent and the request id. Then press
**Verify** — it recomputes the hash chain and tells you whether anything was altered. Try to edit a
row directly in `psql`; the database refuses, and the API's own database role cannot lift the guard.

**A statutory return** (Reporting). The seeded return is awaiting submission. Look at the eight
reconciliation checks and the open-items list naming every prudential figure still to be confirmed
with SASRA. Note that you cannot submit it as the user who generated it.

**A payment that arrives twice** (Payments). The seed includes a duplicate webhook delivery, a
failure and a timeout. The duplicate posted once — the provider reference is claimed in a unique
index before any ledger posting.

## 3. Read three files, in this order

- `.claude/CLAUDE.md` — the ten non-negotiables the codebase is built around. If you disagree with
  these, you will disagree with the code.
- `docs/architecture/0002-fosa-bosa-ledger-dimension.md` — why FOSA and BOSA are one ledger. The
  decision that shapes the most code.
- `docs/architecture/0019-audit-trail.md` — how the trail is made tamper-evident, and what it does
  *not* claim about entries written before chaining existed.

There are 18 of these records. They exist so whoever maintains this inherits the reasoning rather
than guessing at it — read whichever areas you will own.

## 4. Run the tests

```bash
dotnet test --project backend/tests/Sacco.UnitTests          # 174 tests
dotnet test --project backend/tests/Sacco.IntegrationTests   # 95 tests, real PostgreSQL
```

Integration tests run against a real database (Testcontainers, or set `SACCO_TEST_CONNECTION` to
reuse a server). Two worth reading rather than just running: the maker-checker tests, which assert a
403 when the same user tries to approve their own work, and the tenant isolation test, which asserts
that a connection with no tenant bound reads nothing at all.

The front ends have no automated test suite — CI covers them with lint and build. That is a real gap
and is stated in the listing rather than hidden here.

## 5. Exercise the payment providers against the mocks

```bash
docker compose --profile mocks up -d      # M-Pesa, Airtel, Equity and NCBA mock gateways
mocked-providers/e2e/run-local.sh         # drives all four paths end to end
```

This runs the real provider classes — the same code that would talk to Safaricom — against gateways
that mirror each provider's API, asserting the transaction status and the resulting ledger journal
each time. It proves the integration logic. It does not prove the live endpoint, which is why the
listing says the providers are written but not certified.

## 6. Questions to ask before you recommend it

The honest answers are in `docs/product/platform-overview.md` under "Honest limits", and they are
all in the listing too. Ask them anyway, and compare:

- Which provider integrations have actually run against a live account?
- Which regulatory figures are confirmed, and which are defaults?
- Has it run at a real SACCO?
- What happens on iOS?
- Who does the data migration from our current system?

A vendor whose answers match their documentation is telling you something useful about the rest of
the code.
