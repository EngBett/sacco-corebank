# SASRA confirmation register

Every prudential figure the platform applies is configuration with a recorded source (`docs/compliance/sasra-mapping.md`).
This register tracks the confirmation of each figure against SASRA's current published guidance before the first live
submission. Confirming a figure is a configuration change, never a code change; every statutory package prints the items
still open under `OpenItems` until this register is complete.

| # | Item | Where it is configured | Current value / default | Source of the default | Status | Owner | Confirmed on / evidence |
|---|---|---|---|---|---|---|---|
| 1 | Loan-loss provisioning percentages per aging bucket | `lending.provisioning_config` (`PUT /api/loans/provisioning/config`) | Performing 1%, Watch 5%, Substandard 25%, Doubtful 50%, Loss 100% | Sacco Societies (DT-Sacco Business) Regulations schedule | Open | Compliance officer | |
| 2 | Aging bucket boundaries (days in arrears) | same | 0 / 31 / 181 / 361 / 721 | same | Open | Compliance officer | |
| 3 | Liquidity ratio minimum and weighting of FOSA demand liabilities | `Reporting:LiquidityMinimumBps`, `Reporting:ShortTermLiabilityGls` | 15% of deposits + short-term liabilities | Regulations, r. 66 | Open | Accountant | |
| 4 | Core capital / total assets, institutional capital / total assets, core capital / deposits minima | `Reporting:*MinimumBps` | 10% / 8% / 8% | Regulations, capital adequacy schedule | Open | Accountant | |
| 5 | Large-exposure threshold against core capital | `Reporting:LargeExposureThresholdBps` | 25% | Regulations (single-borrower limit) | Open | Credit committee | |
| 6 | Deposit Guarantee Fund contribution basis | `Reporting:SdgfContributionBps` | null (not computed) | Awaiting SASRA circular | Open | Accountant | |
| 7 | Statutory return submission format | `GET /api/reporting/returns/{id}/export.csv` (flat CSV) and the JSON package | CSV per section | Platform choice pending SASRA portal specification | Open | Compliance officer | |
| 8 | CRB consent wording and bureau report retention | `Lending:CreditBureau:ConsentText`, `Lending:CreditBureau:RetentionDays` | Platform wording; 365 days | Credit Reference Bureau Regulations; SACCO's bureau agreement | Open | Compliance officer | |
| 9 | Dividend / interest withholding tax rate | `Savings:DividendWithholdingTaxBps` | 5% | KRA (resident WHT on dividends) | Open | Accountant | |

## How to close an item
1. Obtain the current SASRA circular, regulation or written confirmation.
2. Set the configuration value (API, appsettings or environment variable) in every environment.
3. Record the date, the document reference and who confirmed it in the last column, and change Status to Confirmed.
4. Regenerate a statutory package: the item disappears from `OpenItems` once its configuration carries a confirmed source.
