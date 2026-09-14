# ADR 0010: Credit scoring advises; maker-checker decides

## Status
Accepted (2026-09-14)

## Context
Lending had hard eligibility gates (KYC, arrears, deposit multiplier, guarantee coverage) but no view of a
borrower's risk: two members within the same limits looked identical to the committee. SASRA expects DT-SACCOs
to consult a credit reference bureau (CRB) before lending, and boards expect a consistent, explainable basis
for credit decisions. A score must inform people without displacing them — non-negotiable #6 forbids any
path where software approves money movement.

## Decision
1. **A scorecard is tenant configuration, not code.** `lending.scorecards` holds points per factor and the
   approve/refer/decline cut-offs, editable under a dedicated permission (`loans.scoring.manage`) and audited.
   The set of factors is fixed in code (`ScoringFactors`): repayment history, current arrears, savings
   consistency, membership tenure, deposit coverage, existing exposure, guarantor coverage, bureau result.
   Adding a factor is a code change with tests; re-weighting is a policy change by the SACCO.
2. **The engine is pure.** `CreditScoringEngine.Score(scorecard, inputs)` is deterministic and has no I/O, so a
   stored score can always be explained and reproduced. Scores are normalised to 0–100 with fixed grade bands.
3. **Every computation is kept.** `lending.loan_credit_scores` stores each run (application, appraisal, manual)
   with the full factor breakdown as JSON and the bureau outcome. The committee's record shows exactly what the
   engine saw at the time it decided; the loan itself is never modified by scoring.
4. **Bureau lookups follow the provider pattern.** `ICreditBureau` has a deterministic sandbox (national ID
   ending `0` = listed, `9` = unavailable) so demos and tests need no credentials; `Lending:CreditBureau:Mode`
   selects the live implementation when one is written. A failed lookup degrades to "unavailable", which turns an
   approve-band score into a referral rather than blocking origination.
5. **Recommendations are advisory.** Approve / Refer / Decline appears on the loan and in lists, and appraisers
   can refresh it; approval remains the N-of-M maker-checker workflow. An adverse listing recommends decline by
   default policy, but the committee can still record its decision and the audit trail shows the override.

## Consequences
- Thin-file members (no prior loans) are scored neutrally on repayment history rather than penalised.
- Late payment on a since-closed loan is not yet visible to the engine because instalments do not record when they
  were paid; recording per-instalment payment dates is the natural next step and will sharpen the repayment factor.
- A live bureau integration (TransUnion, Metropol, Creditinfo) is a configuration-selected provider plus an ADR
  note on data-protection obligations for storing bureau reports; until then Live mode reports "unavailable".
