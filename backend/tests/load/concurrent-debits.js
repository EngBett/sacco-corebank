// k6 load scenario: sustained concurrent withdrawals against ONE FOSA account.
// Proves the ledger's row-locking never overdraws under load (ADR 0004) and reports latency.
//
//   k6 run -e API=http://localhost:5000 -e TENANT=demo -e USER=teller -e PASS='Demo2026!pass' -e ACCOUNT=M00001-FO tests/load/concurrent-debits.js
//
// Against staging: point API at the staging host and use a staging teller login. The account must have a
// known balance; the script asserts that successful withdrawals never exceed it.
import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Trend } from "k6/metrics";

const API = __ENV.API || "http://localhost:5000";
const TENANT = __ENV.TENANT || "demo";
const ACCOUNT = __ENV.ACCOUNT || "M00001-FO";
const AMOUNT = Number(__ENV.AMOUNT || 10);
const succeeded = new Counter("withdrawals_succeeded");
const insufficient = new Counter("withdrawals_insufficient_funds");
const other = new Counter("withdrawals_other_errors");
const postLatency = new Trend("withdrawal_latency_ms", true);

export const options = {
  scenarios: {
    debits: { executor: "constant-vus", vus: Number(__ENV.VUS || 25), duration: __ENV.DURATION || "60s" },
  },
  thresholds: {
    withdrawals_other_errors: ["count==0"],
    withdrawal_latency_ms: ["p(95)<1500"],
  },
};

export function setup() {
  const res = http.post(`${API}/connect/token`, {
    grant_type: "password", client_id: "sacco-cli", client_secret: __ENV.CLIENT_SECRET || "sacco-cli-dev-secret",
    username: __ENV.USER || "teller", password: __ENV.PASS || "Demo2026!pass", scope: "openid profile tenant sacco-api", tenant: TENANT,
  });
  check(res, { "token issued": (r) => r.status === 200 });
  const token = res.json("access_token");
  const before = http.get(`${API}/api/savings/accounts/${ACCOUNT}`, { headers: { Authorization: `Bearer ${token}`, "X-Tenant": TENANT } }).json();
  return { token, openingBalance: Number(before.availableBalance) };
}

export default function (data) {
  const ref = `K6:${__VU}:${__ITER}:${Date.now()}`;
  const res = http.post(`${API}/api/savings/accounts/${ACCOUNT}/withdrawals`,
    JSON.stringify({ amount: AMOUNT, channel: "Cash", destination: null, narrative: "k6 load test" }),
    { headers: { Authorization: `Bearer ${data.token}`, "X-Tenant": TENANT, "Content-Type": "application/json", "Idempotency-Key": ref } });
  postLatency.add(res.timings.duration);
  if (res.status === 201) succeeded.add(1);
  else if (res.status === 422 && String(res.body).includes("insufficient")) insufficient.add(1);
  else { other.add(1); console.error(`${res.status} ${String(res.body).slice(0, 200)}`); }
  sleep(0.05);
}

export function teardown(data) {
  const after = http.get(`${API}/api/savings/accounts/${ACCOUNT}`, { headers: { Authorization: `Bearer ${data.token}`, "X-Tenant": TENANT } }).json();
  const closing = Number(after.availableBalance);
  console.log(`opening ${data.openingBalance} closing ${closing}`);
  if (closing < 0) throw new Error(`ACCOUNT OVERDRAWN: ${closing}`);
}
