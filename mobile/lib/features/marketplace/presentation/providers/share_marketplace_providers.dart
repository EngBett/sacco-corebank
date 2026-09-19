import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/di/service_locator.dart';
import '../../domain/repositories/share_marketplace_repository.dart';

final shareMarketplaceRepositoryProvider = Provider<ShareMarketplaceRepository>(
  (ref) => sl<ShareMarketplaceRepository>(),
);

final openShareListingsProvider = FutureProvider.autoDispose(
  (ref) => ref.watch(shareMarketplaceRepositoryProvider).getOpenListings(),
);

final myShareListingsProvider = FutureProvider.autoDispose(
  (ref) => ref.watch(shareMarketplaceRepositoryProvider).getMyListings(),
);
