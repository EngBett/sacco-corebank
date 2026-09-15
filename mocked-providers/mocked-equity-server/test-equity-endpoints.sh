#!/usr/bin/env bash
# Smoke-tests every mocked-equity-server endpoint against a running instance.
#
#   ./test-equity-endpoints.sh [base-url] [callback-url]
#
# Start a callback sink first if you want to see the async results, e.g.
#   python3 -c "from http.server import *
# class H(BaseHTTPRequestHandler):
#  def do_POST(s):
#   print(s.rfile.read(int(s.headers['Content-Length'])).decode(), flush=True)
#   s.send_response(200); s.end_headers()
# HTTPServer(('127.0.0.1',5999),H).serve_forever()"

set -uo pipefail

BASE="${1:-http://localhost:5104}"
CALLBACK="${2:-http://127.0.0.1:5999/api/callbacks/equity/result}"

API_KEY="local-api-key"
MERCHANT_CODE="0582910862"
CONSUMER_SECRET="local-consumer-secret"
SHORT_CODE="800800"
ORG_USER="TESTAPIUSER"

pass=0; fail=0
RUN_ID="$(date +%s)"

green() { printf '\033[0;32m%s\033[0m\n' "$1"; }
red()   { printf '\033[0;31m%s\033[0m\n' "$1"; }

# check <name> <expected-substring> <actual>
check() {
  if [[ "$3" == *"$2"* ]]; then
    green "  PASS  $1"; pass=$((pass+1))
  else
    red   "  FAIL  $1"; red "        expected to contain: $2"; red "        got: $3"; fail=$((fail+1))
  fi
}

json() { python3 -c "import sys,json;print(json.load(sys.stdin).get('$1',''))" 2>/dev/null; }

echo "== Equity mock smoke test =="
echo "   base:     $BASE"
echo "   callback: $CALLBACK"
echo

echo "-- health --"
check "health responds" '"status":"healthy"' "$(curl -s "$BASE/_test/health")"

echo "-- authentication --"
AUTH_BODY=$(printf '{"merchantCode":"%s","consumerSecret":"%s"}' "$MERCHANT_CODE" "$CONSUMER_SECRET")

check "wrong Api-Key is rejected" '"code":"101"' \
  "$(curl -s -X POST "$BASE/authentication/api/v3/authenticate/merchant" \
      -H 'Content-Type: application/json' -H 'Api-Key: nope' -d "$AUTH_BODY")"

AUTH=$(curl -s -X POST "$BASE/authentication/api/v3/authenticate/merchant" \
  -H 'Content-Type: application/json' -H "Api-Key: $API_KEY" -d "$AUTH_BODY")
check "valid credentials return a token" '"tokenType":"Bearer"' "$AUTH"
check "expiresIn is an absolute timestamp" 'Z"' "$AUTH"

TOKEN=$(echo "$AUTH" | json accessToken)
AUTH_HDR="Authorization: Bearer $TOKEN"

# call <path> <body> [simulate-header]
call() {
  local path="$1" body="$2" simulate="${3:-}"
  local args=(-s -X POST "$BASE/momo-apis/api/v1/transaction/$path"
              -H 'Content-Type: application/json' -H "$AUTH_HDR")
  [[ -n "$simulate" ]] && args+=(-H "X-Simulate: $simulate")
  args+=(-d "$body")
  curl "${args[@]}"
}

echo "-- credential envelope --"
encrypt() { curl -s -X POST "$BASE/_test/encrypt" -H 'Content-Type: application/json' \
              -d "$(printf '{"value":"%s"}' "$1")" | json encrypted; }

PIN=$(encrypt "2580")
ORG_PASSWORD=$(encrypt "local-org-password")
# Base64 padding is only present for some lengths, so assert on the length instead of a "=".
[[ ${#PIN} -gt 40 ]] && ENCRYPT_OK="ok" || ENCRYPT_OK="empty-or-short: $PIN"
check "encrypt helper returns a payload" "ok" "$ENCRYPT_OK"
check "decrypt validates our own payload" '"valid":true' \
  "$(curl -s -X POST "$BASE/_test/decrypt" -H 'Content-Type: application/json' \
      -d "$(printf '{"encrypted":"%s"}' "$PIN")")"

# mobile_body <requestId> <amount> <currency> <pin>
mobile_body() {
  printf '{"requestId":"%s","amount":"%s","currency":"%s","shortCode":"%s","pin":"%s","callbackUrl":"%s","msisdn":"254765555186","remarks":"smoke test"}' \
    "$1" "$2" "$3" "$SHORT_CODE" "$4" "$CALLBACK"
}

echo "-- authorization --"
check "payment without a token is rejected" '"code":"101"' \
  "$(curl -s -X POST "$BASE/momo-apis/api/v1/transaction/c2b/customer-initiated-payment" \
      -H 'Content-Type: application/json' -d '{"requestId":"800800_NOAUTH"}')"

echo "-- collections (C2B) --"
check "buy-goods is acknowledged as PENDING" '"serviceStatus":"PENDING"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_C2B-$RUN_ID" 250 KES "$PIN")")"
check "acknowledgement carries no transactionId" '"responseCode":"0","responseDesc"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_C2B2-$RUN_ID" 250 KES "$PIN")")"

PAYBILL_INCOMPLETE=$(printf '{"requestId":"%s","amount":"250","currency":"KES","pin":"%s","callbackUrl":"%s","msisdn":"254765555186"}' \
  "${SHORT_CODE}_PB-$RUN_ID" "$PIN" "$CALLBACK")
check "paybill needs billerCode" '"code":"100"' \
  "$(call c2b/paybill "$PAYBILL_INCOMPLETE")"

PAYBILL_COMPLETE=$(printf '{"requestId":"%s","amount":"250","currency":"KES","billerCode":"800800","billerName":"DTOne","billPaymentReference":"REF-1","pin":"%s","callbackUrl":"%s","msisdn":"254765555186"}' \
  "${SHORT_CODE}_PB2-$RUN_ID" "$PIN" "$CALLBACK")
check "paybill succeeds when complete" '"serviceStatus":"PENDING"' \
  "$(call c2b/paybill "$PAYBILL_COMPLETE")"

echo "-- disbursements (B2C) --"
check "B2C is acknowledged" '"serviceStatus":"PENDING"' \
  "$(call b2c/business-to-customer-payment "$(mobile_body "${SHORT_CODE}_B2C-$RUN_ID" 250 KES "$PIN")")"

echo "-- business transfers (B2B) --"
B2B_BODY=$(printf '{"transactionReference":"%s","amount":"1090","currency":"KES","initiatorShortCode":"%s","tillNumber":"987654","referenceData":{"invoiceNumber":"INV001","customerName":"John Doe","accountNumber":"ACC123456","remarks":"settlement"},"receiverOrgShortCode":"600999","password":"%s","callbackUrl":"%s"}' \
  "${SHORT_CODE}_B2B-$RUN_ID" "$SHORT_CODE" "$ORG_PASSWORD" "$CALLBACK")
check "B2B buy-goods is acknowledged" '"serviceStatus":"PENDING"' \
  "$(call b2b/buy-goods "$B2B_BODY")"

B2B_NO_TILL=$(printf '{"transactionReference":"%s","amount":"1090","currency":"KES","initiatorShortCode":"%s","password":"%s","callbackUrl":"%s"}' \
  "${SHORT_CODE}_B2BX-$RUN_ID" "$SHORT_CODE" "$ORG_PASSWORD" "$CALLBACK")
check "B2B without tillNumber is rejected" '"code":"100"' \
  "$(call b2b/buy-goods "$B2B_NO_TILL")"

echo "-- validation --"
check "requestId without shortCode prefix is rejected" 'shortCode_uniqueRequestId' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "NOPREFIX-$RUN_ID" 250 KES "$PIN")")"
check "duplicate requestId is rejected" 'already been used' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_C2B-$RUN_ID" 250 KES "$PIN")")"
check "non-ISO4217 currency is rejected" '"code":"3013"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_CUR-$RUN_ID" 250 USD "$PIN")")"
check "malformed PIN envelope is rejected" '"code":"101"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_PIN-$RUN_ID" 250 KES 'bm90LWFuLWVudmVsb3Bl')")"
check "zero amount is rejected" '"code":"100"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_AMT-$RUN_ID" 0 KES "$PIN")")"

echo "-- failure simulation --"
check "X-Simulate: reject fails at acknowledgement" '"serviceStatus":"FAILED"' \
  "$(call b2c/business-to-customer-payment "$(mobile_body "${SHORT_CODE}_REJ-$RUN_ID" 250 KES "$PIN")" reject)"
check "X-Simulate: fail is accepted then fails async" '"serviceStatus":"PENDING"' \
  "$(call b2c/business-to-customer-payment "$(mobile_body "${SHORT_CODE}_FAIL-$RUN_ID" 250 KES "$PIN")" fail)"
check "X-Simulate: no-callback is accepted" '"serviceStatus":"PENDING"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_STUCK-$RUN_ID" 250 KES "$PIN")" no-callback)"
check "amount 14 fails at acknowledgement" '"serviceStatus":"FAILED"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_A14-$RUN_ID" 14 KES "$PIN")")"

echo "-- waiting for async callbacks --"
sleep 6

echo "-- status query --"
status_body() { printf '{"transactionId":"%s"}' "$1"; }
check "known transaction resolves" '"status":"SUCCESS"' \
  "$(call query-transaction-status "$(status_body "${SHORT_CODE}_C2B-$RUN_ID")")"
check "failed transaction reports FAILED" '"status":"FAILED"' \
  "$(call query-transaction-status "$(status_body "${SHORT_CODE}_FAIL-$RUN_ID")")"
check "suppressed-callback transaction is still PENDING" '"status":"PENDING"' \
  "$(call query-transaction-status "$(status_body "${SHORT_CODE}_STUCK-$RUN_ID")")"
check "unknown transaction returns 3011, not 404" '"code":"3011"' \
  "$(call query-transaction-status "$(status_body 'DOES-NOT-EXIST')")"

echo "-- reversal --"
# reversal_body <receiptNumber> <organizationUsername>
reversal_body() {
  printf '{"receiptNumber":"%s","amount":"250","organizationUsername":"%s","password":"%s","shortCode":"%s"}' \
    "$1" "$2" "$ORG_PASSWORD" "$SHORT_CODE"
}
check "completed transaction can be reversed" '"status":"SUCCESS"' \
  "$(call reverse-transaction "$(reversal_body "${SHORT_CODE}_C2B-$RUN_ID" "$ORG_USER")")"
check "pending transaction cannot be reversed" '"code":"102"' \
  "$(call reverse-transaction "$(reversal_body "${SHORT_CODE}_STUCK-$RUN_ID" "$ORG_USER")")"
check "unknown organizationUsername is rejected" '"code":"101"' \
  "$(call reverse-transaction "$(reversal_body "${SHORT_CODE}_C2B2-$RUN_ID" 'WRONG')")"

echo "-- balance --"
BALANCE_BODY=$(printf '{"requestId":"%s","organizationUsername":"%s","password":"%s","shortCode":"%s","accountType":"Organization E-Money Account"}' \
  "${SHORT_CODE}_BAL-$RUN_ID" "$ORG_USER" "$ORG_PASSWORD" "$SHORT_CODE")
check "balance returns a float" '"availableBalance"' \
  "$(call organization/balance "$BALANCE_BODY")"

echo "-- token expiry --"
curl -s -X POST "$BASE/_test/expire-tokens" > /dev/null
check "expired token is rejected with 101" '"code":"101"' \
  "$(call c2b/customer-initiated-payment "$(mobile_body "${SHORT_CODE}_EXP-$RUN_ID" 250 KES "$PIN")")"

echo
echo "== $pass passed, $fail failed =="
[[ $fail -eq 0 ]]
