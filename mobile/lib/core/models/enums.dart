/// Mirrors of the backend's shared enums (backend/src/Sacco.Shared). The API
/// serializes every enum as its PascalCase name (`JsonStringEnumConverter`,
/// `Program.cs`), never a number — decode and encode by that name, not an int
/// code. An unknown value (a future channel/status the app doesn't know about
/// yet) falls back to a safe default rather than crashing the whole screen.
library;

enum ProductKind {
  fosaCurrent('FosaCurrent'),
  bosaDeposit('BosaDeposit'),
  shares('Shares'),
  fixedDeposit('FixedDeposit');

  const ProductKind(this.wireName);
  final String wireName;

  static ProductKind fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => ProductKind.fosaCurrent);

  String get label => switch (this) {
    ProductKind.fosaCurrent => 'FOSA (current)',
    ProductKind.bosaDeposit => 'BOSA deposits',
    ProductKind.shares => 'Shares',
    ProductKind.fixedDeposit => 'Fixed deposit',
  };
}

enum SavingsAccountStatus {
  active('Active'),
  matured('Matured'),
  closed('Closed');

  const SavingsAccountStatus(this.wireName);
  final String wireName;

  static SavingsAccountStatus fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => SavingsAccountStatus.active);
}

enum PayoutChannel {
  cash('Cash'),
  mpesa('MPesa'),
  airtelMoney('AirtelMoney'),
  bankTransfer('BankTransfer');

  const PayoutChannel(this.wireName);
  final String wireName;

  static PayoutChannel fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => PayoutChannel.mpesa);

  String get label => switch (this) {
    PayoutChannel.cash => 'Cash (at a branch)',
    PayoutChannel.mpesa => 'M-Pesa',
    PayoutChannel.airtelMoney => 'Airtel Money',
    PayoutChannel.bankTransfer => 'Bank account (Pesalink)',
  };
}

enum WithdrawalStatus {
  pendingApproval('PendingApproval'),
  approved('Approved'),
  paid('Paid'),
  rejected('Rejected'),
  cancelled('Cancelled');

  const WithdrawalStatus(this.wireName);
  final String wireName;

  static WithdrawalStatus fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => WithdrawalStatus.pendingApproval);

  String get label => switch (this) {
    WithdrawalStatus.pendingApproval => 'Pending approval',
    WithdrawalStatus.approved => 'Approved',
    WithdrawalStatus.paid => 'Paid',
    WithdrawalStatus.rejected => 'Rejected',
    WithdrawalStatus.cancelled => 'Cancelled',
  };
}

enum DividendStatus {
  declared('Declared'),
  approved('Approved'),
  paid('Paid'),
  rejected('Rejected');

  const DividendStatus(this.wireName);
  final String wireName;

  static DividendStatus fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => DividendStatus.declared);

  String get label => switch (this) {
    DividendStatus.declared => 'Declared',
    DividendStatus.approved => 'Approved',
    DividendStatus.paid => 'Paid',
    DividendStatus.rejected => 'Rejected',
  };
}

enum PaymentStatus {
  initiated('Initiated'),
  pendingCallback('PendingCallback'),
  succeeded('Succeeded'),
  failed('Failed'),
  timedOut('TimedOut');

  const PaymentStatus(this.wireName);
  final String wireName;

  static PaymentStatus fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => PaymentStatus.initiated);

  String get label => switch (this) {
    PaymentStatus.initiated => 'Initiated',
    PaymentStatus.pendingCallback => 'Processing',
    PaymentStatus.succeeded => 'Succeeded',
    PaymentStatus.failed => 'Failed',
    PaymentStatus.timedOut => 'Timed out',
  };
}

enum EntryDirection {
  debit('Debit'),
  credit('Credit');

  const EntryDirection(this.wireName);
  final String wireName;

  static EntryDirection fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => EntryDirection.debit);
}

enum KycStatus {
  pendingVerification('PendingVerification'),
  verified('Verified'),
  rejected('Rejected'),
  suspended('Suspended'),
  exited('Exited'),
  exitRequested('ExitRequested');

  const KycStatus(this.wireName);
  final String wireName;

  static KycStatus fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => KycStatus.pendingVerification);
}

enum ShareListingStatus {
  open('Open'),
  pendingApproval('PendingApproval'),
  approved('Approved'),
  rejected('Rejected'),
  cancelled('Cancelled');

  const ShareListingStatus(this.wireName);
  final String wireName;

  static ShareListingStatus fromWire(String value) =>
      values.firstWhere((v) => v.wireName == value, orElse: () => ShareListingStatus.open);

  String get label => switch (this) {
    ShareListingStatus.open => 'Open',
    ShareListingStatus.pendingApproval => 'Awaiting staff approval',
    ShareListingStatus.approved => 'Completed',
    ShareListingStatus.rejected => 'Rejected',
    ShareListingStatus.cancelled => 'Cancelled',
  };
}

/// Deposit/withdrawal providers this app offers a member (ADR 0008 channel-scope
/// decision: NCBA is payout-only so it never appears as a deposit option, and
/// withdrawal bank routing is generic Pesalink, not a named-bank choice).
class ProviderNames {
  const ProviderNames._();
  static const mpesa = 'MPesa';
  static const airtelMoney = 'AirtelMoney';
  static const equity = 'Bank:EQUITY';
}
