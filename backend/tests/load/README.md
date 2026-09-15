# Load tests (k6)

`concurrent-debits.js` hammers one FOSA account with parallel teller withdrawals and fails if the
account is ever overdrawn or any request errors for a reason other than insufficient funds. It is the
sustained-load counterpart of the 40-request integration test in `Sacco.IntegrationTests/Ledger/ConcurrencyTests.cs`.

```bash
brew install k6                        # or docker run --rm -i grafana/k6 run - < tests/load/concurrent-debits.js
k6 run -e API=http://localhost:5000 -e ACCOUNT=M00001-FO -e VUS=25 -e DURATION=60s tests/load/concurrent-debits.js
```

Thresholds: zero unexpected errors, p95 latency under 1.5 s. Run it against staging before every cutover
with a staging teller login (`USER`/`PASS`) and a funded test account; never against production data.
Results belong in the cutover checklist (`docs/runbooks/production-cutover.md`).
