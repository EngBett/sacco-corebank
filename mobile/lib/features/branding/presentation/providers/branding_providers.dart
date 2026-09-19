import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/di/service_locator.dart';
import '../../domain/repositories/branding_repository.dart';

final brandingRepositoryProvider = Provider<BrandingRepository>((ref) => sl<BrandingRepository>());

final tenantBrandingProvider = FutureProvider<TenantBranding>(
  (ref) => ref.watch(brandingRepositoryProvider).getBranding(),
);
