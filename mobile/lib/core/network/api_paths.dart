/// Every `/api/self/*` route this app calls. Kept in one place so a backend
/// route rename is a one-file fix (docs/architecture/0008-mobile-scope.md).
class ApiPaths {
  const ApiPaths._();

  static const profile = '/api/self/profile';
  static const accounts = '/api/self/accounts';
  static const summary = '/api/self/summary';
  static String statement(String accountNumber) => '/api/self/statements/$accountNumber';

  /// Pay to reveal a fee-gated balance (ADR 0015). Idempotent per key.
  static String balanceEnquiry(String accountNumber) => '/api/self/accounts/$accountNumber/balance-enquiries';
  static const feeQuote = '/api/self/fees/quote';
  static const verifyPin = '/api/self/auth/pin/verify';
  static const withdrawals = '/api/self/withdrawals';
  static const dividends = '/api/self/dividends';
  static const paymentsTopUp = '/api/self/payments/topup';
  static const payments = '/api/self/payments';

  /// Tenant is resolved from the `X-Tenant` header (same as every other call), not a path segment.
  static const tenantBranding = '/api/public/tenant/branding';

  static const requestLoginOtp = '/api/self/auth/otp/request';

  /// Shares marketplace (ADR 0008 follow-up): share capital isn't withdrawable, only sold to
  /// another member, and still maker-checker — a staff member approves before anything moves.
  static const shareListings = '/api/self/shares-marketplace/listings';
  static const openShareListings = '/api/self/shares-marketplace/listings/open';
  static const myShareListings = '/api/self/shares-marketplace/listings/mine';
  static String claimShareListing(String id) => '/api/self/shares-marketplace/listings/$id/claim';
  static String cancelShareListing(String id) => '/api/self/shares-marketplace/listings/$id/cancel';
}
