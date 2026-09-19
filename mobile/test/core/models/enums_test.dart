import 'package:flutter_test/flutter_test.dart';
import 'package:sacco_member/core/models/enums.dart';

void main() {
  group('PayoutChannel', () {
    test('round-trips every wire name the backend can send', () {
      for (final channel in PayoutChannel.values) {
        expect(PayoutChannel.fromWire(channel.wireName), channel);
      }
    });

    test('falls back to MPesa for an unknown value instead of throwing', () {
      expect(PayoutChannel.fromWire('SomethingNew'), PayoutChannel.mpesa);
    });
  });

  group('WithdrawalStatus', () {
    test('round-trips every wire name', () {
      for (final status in WithdrawalStatus.values) {
        expect(WithdrawalStatus.fromWire(status.wireName), status);
      }
    });
  });

  group('DividendStatus', () {
    test('round-trips every wire name', () {
      for (final status in DividendStatus.values) {
        expect(DividendStatus.fromWire(status.wireName), status);
      }
    });
  });

  group('ProductKind', () {
    test('round-trips every wire name', () {
      for (final kind in ProductKind.values) {
        expect(ProductKind.fromWire(kind.wireName), kind);
      }
    });
  });

  group('ShareListingStatus', () {
    test('round-trips every wire name', () {
      for (final status in ShareListingStatus.values) {
        expect(ShareListingStatus.fromWire(status.wireName), status);
      }
    });
  });
}
