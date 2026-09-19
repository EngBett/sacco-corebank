# SASRA requirement → system mapping

Living document. Update this whenever a ledger, lending, savings, or reporting change could
affect a SASRA-facing number or governance control. This is what an examiner conversation
should be able to run off of.

| SASRA requirement | System module | Notes |
|---|---|---|
| Core capital ratio ≥ 10% of total assets | `Sacco.Modules.Reporting` (`GET /api/reporting/capital-adequacy`) | Core capital = share capital (3000) + institutional capital (3100–3300) + accumulated surplus, over consolidated total assets (clearing accounts netted). Threshold `Reporting:CoreCapitalToAssetsMinBps` |
| Institutional capital ratio ≥ 8% | `Sacco.Modules.Reporting` | Reserves + retained earnings + current surplus over total assets; `Reporting:InstitutionalCapitalToAssetsMinBps` |
| Core capital / deposits ratio ≥ 8% | `Sacco.Modules.Reporting` | Deposits = 2000 + 2100 + 2200; `Reporting:CoreCapitalToDepositsMinBps` |
| Liquidity ratio ≥ 15% of deposits + short-term liabilities | `Sacco.Modules.Reporting` (`GET /api/reporting/liquidity`) | Liquid assets = `Reporting:LiquidAssetGls`; denominator = member deposits + `ShortTermLiabilityGls`. **Open:** weighting of FOSA demand liabilities still to confirm; surfaced in every package's `OpenItems` |
| FOSA/BOSA distinguishable in returns | `Sacco.Modules.Ledger` (GL dimension) | See ADR 0002. Every GL account, sub-ledger account and journal line carries an explicit segment; journals must balance *within* each segment (cross-segment flows use clearing accounts 1800/2800), so FOSA and BOSA trial balances each balance independently and net to zero on consolidation |
| Loan aging buckets (normal/watch/substandard/doubtful/loss) | `Sacco.Modules.Lending` (`ProvisioningConfig`, `GET /api/loans/provisioning/aging`) | Bucket thresholds are per-tenant configuration (seeded: 0/31/181/361/721 days); days in arrears = oldest unpaid instalment less product grace period |
| Provisioning percentages per aging bucket | `Sacco.Modules.Lending` (`ProvisioningRun`) | Rates are configuration (seeded 1/5/25/50/100%); a run is computed by one user and posted (Dr provision expense / Cr provision contra-asset, per FOSA/BOSA product GL pair) by a different user |
| Large exposure reporting (loans/deposits above threshold vs. core capital) | `Sacco.Modules.Reporting` (`GET /api/reporting/large-exposures`) | Members whose outstanding loans ≥ `Reporting:LargeExposureThresholdBps` (default 25%) of core capital. **Open:** confirm the current limit |
| Credit committee / board approval trail | `Sacco.Modules.Lending` (`Loan.Approvals`) | Every decision is a `LoanApproval` row with approver, decision, notes and time; N-of-M above the product committee threshold; originator/appraiser excluded from approving; audit events `loans.approved`, `loans.rejected`, `loans.disbursed` |
| Segregation of duties (initiator ≠ approver) | `Sacco.Shared.Domain.MakerChecker` + each workflow entity | Enforced in code (`MakerChecker.EnsureDistinct`), see `.claude/CLAUDE.md` non-negotiable #6. Ledger: manual journals and reversals (`JournalEntry.Approve/Reject`) — verified by integration test `Manual_journal_requires_a_different_approver_and_then_moves_balances` |
| KYC / member onboarding segregation | `Sacco.Modules.Members` (`Member.VerifyKyc`) | The user who registered a member (or converted their public application) cannot verify that member's KYC; verification requires ID and photo documents on file. Public applications only ever create a *pending application* (non-negotiable #9) |
| Staff access control | `Sacco.Modules.Identity` + `Sacco.Shared.Auth` | Roles are per-tenant permission bundles; permissions resolved server-side per request with a 60 s cache invalidated on change, so revocation is immediate (ADR 0005). Lockout after 5 failed logins. Staff accounts are created by invitation (maker-checker when proposed by a manager), activated from a single-use emailed link, and require an authenticator-app second factor at every portal sign-in; password resets are self-service links and administrators never set passwords (ADR 0016) |
| Member withdrawals and notice periods | `Sacco.Modules.Savings` (`WithdrawalRequest`) | BOSA deposits carry a product-level notice period enforced at payout; withdrawals above the teller limit require a second approver; funds are held on the ledger for the life of the request |
| Dividend / interest-rebate declaration | `Sacco.Modules.Savings` (`DividendDeclaration`) | Declared (maker) → approved by a different user (posts Dr retained earnings / Cr dividends payable) → paid net of withholding tax (2700). Rates are per declaration, not hardcoded |
| Guarantor exposure | `Sacco.Modules.Lending` (`GET /api/loans/guarantors/{id}/exposure`) | Guarantees are ledger holds on the guarantor's BOSA deposits; caps: ratio to deposits and count of active guarantees (configuration) |
| Audit trail of GL adjustments | `Sacco.Modules.Platform` (append-only `platform.audit_log`) | Initiation, approval, rejection and reversal requests of manual journals are recorded with actor, entity id and details; queryable via `GET /api/admin/audit-log` |
| Governance audit trail (who did what, from where, and proof it wasn't edited) | `Sacco.Modules.Platform` (`platform.audit_log`, ADR 0019) | Every entry carries the actor's id **and name as it was at the time**, outcome (success / failure / denied), IP address, user agent, request correlation id and branch. Coverage includes sign-ins, failed sign-ins and lockouts, two-step verification failures and resets, KYC decisions, approvals and denials, product, listing, role and branding changes (as field diffs), deposits, maintenance runs, return exports and blocked payment callbacks. Rows are hash-chained per tenant and a database trigger refuses `UPDATE`/`DELETE` — the application's database role cannot lift it. `GET /api/admin/audit-log/verify` recomputes the chain and names the first altered entry; `export.csv` hands an examiner the filtered rows (and records that it did) |
| Branch-level accountability | `platform.branches` + `branch_id` on staff, members and journals (ADR 0018) | Each office is a dimension on one set of books, not separate books: postings are stamped with the branch the money moved at, members with the office that serves them, audit rows with the acting user's branch. Returns and provisioning are unchanged and remain society-wide |
| SDGF (Sacco Societies Deposit Guarantee Fund) contribution tracking | `Sacco.Modules.Ledger` (2600/5400) / `Sacco.Modules.Reporting` | Package computes the contribution only when `Reporting:SdgfContributionBps` is set. **Open:** basis unconfirmed, so it is null and listed under `OpenItems` |
| Statutory return submission format | `Sacco.Modules.Reporting` (`StatutoryReturn`, `POST /api/reporting/statutory-returns`) | Package = FOSA/BOSA/consolidated financial position + income statements + capital, liquidity, portfolio quality, large exposures + 8 reconciliation checks (must all pass before submission). Stored as immutable JSON; submission is maker-checker (generator ≠ submitter) and records the SASRA acknowledgement reference. **Open:** map the package onto SASRA's current electronic format |

## Open items (still to confirm with SASRA before the first live submission)

These are encoded as configuration with build-time defaults and are printed in every generated package's `OpenItems`:

- Current exact statutory return submission mechanism/format from SASRA (the package is a structured JSON document; an export in SASRA's format is a thin mapping once known)
- Current provisioning percentages and aging bucket definitions — seeded as 0/31/181/361/721 days at 1/5/25/50/100% (`ProvisioningConfig`, editable per tenant)
- Current SDGF contribution basis (`Reporting:SdgfContributionBps`, unset)
- Liquidity weighting of FOSA demand liabilities
- Single-member large-exposure limit (`Reporting:LargeExposureThresholdBps`, 25%)

## Credit reference bureau checks (added 2026-09-14)

| Requirement | Where it lives | Status |
|---|---|---|
| Consult a licensed CRB before granting credit (Credit Reference Bureau Regulations; SASRA prudential guidance) | `CreditScoringService` runs an `ICreditBureau` lookup on every application and appraisal; outcome and reference are stored on `lending.loan_credit_scores` and audited as `loans.scored` | Sandbox provider only. A live provider (TransUnion / Metropol / Creditinfo) is a configuration-selected implementation still to be written; confirm the SACCO's CRB subscription and consent wording at cutover |
| Consistent, explainable credit decisions | Tenant scorecard (`/api/loans/scoring/scorecard`) with stored per-factor breakdown per computation; recommendation is advisory, approval remains maker-checker (ADR 0010) | Implemented |

