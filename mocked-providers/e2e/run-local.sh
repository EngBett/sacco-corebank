#!/usr/bin/env bash
# End-to-end check of the LIVE provider implementations against the mock servers.
# Prerequisites: mock servers running (see ../README.md) and the platform API started in Live mode against them
# (env vars in ../README.md), by default on http://localhost:5010. Exercises:
#   0. M-Pesa STK push + B2C    → mock callbacks → collection and withdrawal payout Succeeded
#   1. Airtel Money USSD push  → mock callback → transaction Succeeded, ledger posted
#   2. Equity (Jenga) C2B      → mock callback → transaction Succeeded
#   3. NCBA Pesalink payout    → synchronous receipt → withdrawal paid
# Set SKIP_MPESA=1 when the M-Pesa mock (Docker, needs Mongo + RabbitMQ) is not running.
set -euo pipefail
API=${API:-http://localhost:5010}; TENANT=${TENANT:-demo}; PASS=${PASS:-'Demo2026!pass'}
tok() { curl -s -X POST "$API/connect/token" -d "grant_type=password&client_id=sacco-cli&client_secret=sacco-cli-dev-secret&username=$1&password=$PASS&scope=openid profile tenant sacco-api&tenant=$TENANT" | python3 -c 'import sys,json;print(json.load(sys.stdin)["access_token"])'; }
j() { python3 -c "import sys,json;d=json.load(sys.stdin);print($1)"; }
call() { local t=$1; shift; curl -s -H "Authorization: Bearer $t" -H "X-Tenant: $TENANT" -H 'Content-Type: application/json' "$@"; }
wait_final() { # $1 token, $2 tx id
  for i in $(seq 1 30); do s=$(call "$1" "$API/api/payments/transactions/$2" | j 'd["status"]'); [[ "$s" == "Succeeded" || "$s" == "Failed" || "$s" == "TimedOut" ]] && { echo "$s"; return; }; sleep 1; done; echo "$s"; }
TELLER=$(tok teller); MANAGER=$(tok manager)
echo "providers: $(call "$TELLER" "$API/api/payments/providers")"

if [[ "${SKIP_MPESA:-0}" != "1" ]]; then
echo "--- 0a. M-Pesa collection (STK push)"
TX=$(call "$TELLER" -X POST "$API/api/payments/collections" -d '{"provider":"MPesa","phoneNumber":"254722100004","amount":300,"purpose":"SavingsDeposit","accountNumber":"M00004-FO","narrative":"e2e mpesa"}' | j 'd["id"]')
echo "  tx=$TX status=$(wait_final "$TELLER" "$TX")"; call "$TELLER" "$API/api/payments/transactions/$TX" | j '"  receipt=%s journal=%s" % (d["providerTransactionReference"], d["ledgerJournalEntryId"])'
echo "--- 0b. M-Pesa B2C payout of an approved M-Pesa withdrawal"
W=$(call "$TELLER" -X POST "$API/api/savings/accounts/M00004-FO/withdrawals" -d '{"amount":200,"channel":"MPesa","destination":"254722100004","narrative":"e2e mpesa b2c"}' | j 'd["id"]')
call "$MANAGER" -X POST "$API/api/savings/withdrawals/$W/approve" >/dev/null
TX=$(call "$TELLER" -X POST "$API/api/payments/disbursements" -d "{\"withdrawalId\":\"$W\"}" | j 'd["id"]')
echo "  tx=$TX status=$(wait_final "$TELLER" "$TX")"; call "$TELLER" "$API/api/payments/transactions/$TX" | j '"  provider=%s receipt=%s" % (d["provider"], d["providerTransactionReference"])'
echo "  withdrawal status=$(call "$TELLER" "$API/api/savings/withdrawals/$W" | j 'd["status"]')"
fi

echo "--- 1. Airtel Money collection (USSD push)"
TX=$(call "$TELLER" -X POST "$API/api/payments/collections" -d '{"provider":"AirtelMoney","phoneNumber":"254733100001","amount":150,"purpose":"SavingsDeposit","accountNumber":"M00001-FO","narrative":"e2e airtel"}' | j 'd["id"]')
echo "  tx=$TX status=$(wait_final "$TELLER" "$TX")"; call "$TELLER" "$API/api/payments/transactions/$TX" | j '"  receipt=%s journal=%s" % (d["providerTransactionReference"], d["ledgerJournalEntryId"])'

echo "--- 2. Equity (Jenga) collection (C2B buy goods)"
TX=$(call "$TELLER" -X POST "$API/api/payments/collections" -d '{"provider":"Bank:EQUITY","phoneNumber":"254765555186","amount":250,"purpose":"SavingsDeposit","accountNumber":"M00002-FO","narrative":"e2e equity"}' | j 'd["id"]')
echo "  tx=$TX status=$(wait_final "$TELLER" "$TX")"; call "$TELLER" "$API/api/payments/transactions/$TX" | j '"  receipt=%s journal=%s" % (d["providerTransactionReference"], d["ledgerJournalEntryId"])'

echo "--- 3. NCBA Pesalink payout of an approved bank-transfer withdrawal"
W=$(call "$TELLER" -X POST "$API/api/savings/accounts/M00003-FO/withdrawals" -d '{"amount":1200,"channel":"BankTransfer","destination":"11|0123456789|Akinyi Owino|001","narrative":"e2e ncba"}' | j 'd["id"]')
call "$MANAGER" -X POST "$API/api/savings/withdrawals/$W/approve" >/dev/null
TX=$(call "$TELLER" -X POST "$API/api/payments/disbursements" -d "{\"withdrawalId\":\"$W\"}" | j 'd["id"]')
echo "  tx=$TX status=$(wait_final "$TELLER" "$TX")"; call "$TELLER" "$API/api/payments/transactions/$TX" | j '"  provider=%s receipt=%s" % (d["provider"], d["providerTransactionReference"])'
echo "  withdrawal status=$(call "$TELLER" "$API/api/savings/withdrawals/$W" | j 'd["status"]')"
