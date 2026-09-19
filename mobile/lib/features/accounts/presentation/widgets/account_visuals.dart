import 'package:flutter/material.dart';
import 'package:iconsax_flutter/iconsax_flutter.dart';

import '../../../../core/models/enums.dart';

/// One visual identity per account kind, used by the carousel cards, tiles and the statement header. Gradient stops
/// are dark enough that white text on them stays above 4.5:1.
class AccountVisuals {
  const AccountVisuals._();

  static IconData icon(ProductKind kind) => switch (kind) {
    ProductKind.fosaCurrent => Iconsax.wallet_2_copy,
    ProductKind.bosaDeposit => Iconsax.strongbox_copy,
    ProductKind.shares => Iconsax.chart_2_copy,
    ProductKind.fixedDeposit => Iconsax.lock_1_copy,
  };

  static List<Color> gradient(ProductKind kind) => switch (kind) {
    ProductKind.fosaCurrent => const [Color(0xFF1D4ED8), Color(0xFF0E7490)],
    ProductKind.bosaDeposit => const [Color(0xFF047857), Color(0xFF115E59)],
    ProductKind.shares => const [Color(0xFF6D28D9), Color(0xFF3730A3)],
    ProductKind.fixedDeposit => const [Color(0xFFB45309), Color(0xFF9A3412)],
  };

  /// Short line under the product name on a card.
  static String blurb(ProductKind kind) => switch (kind) {
    ProductKind.fosaCurrent => 'Everyday account',
    ProductKind.bosaDeposit => 'Monthly savings',
    ProductKind.shares => 'Share capital',
    ProductKind.fixedDeposit => 'Locked for a term',
  };
}
