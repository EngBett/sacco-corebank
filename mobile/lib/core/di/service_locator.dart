import 'package:get_it/get_it.dart';

import '../../features/accounts/data/datasources/accounts_remote_datasource.dart';
import '../../features/accounts/data/repositories/accounts_repository_impl.dart';
import '../../features/accounts/domain/repositories/accounts_repository.dart';
import '../../features/auth/data/repositories/auth_repository_impl.dart';
import '../../features/auth/domain/repositories/auth_repository.dart';
import '../../features/branding/data/datasources/branding_remote_datasource.dart';
import '../../features/branding/data/repositories/branding_repository_impl.dart';
import '../../features/branding/domain/repositories/branding_repository.dart';
import '../../features/dividends/data/datasources/dividends_remote_datasource.dart';
import '../../features/dividends/data/repositories/dividends_repository_impl.dart';
import '../../features/dividends/domain/repositories/dividends_repository.dart';
import '../../features/marketplace/data/datasources/share_marketplace_remote_datasource.dart';
import '../../features/marketplace/data/repositories/share_marketplace_repository_impl.dart';
import '../../features/marketplace/domain/repositories/share_marketplace_repository.dart';
import '../../features/payments/data/datasources/payments_remote_datasource.dart';
import '../../features/payments/data/repositories/payments_repository_impl.dart';
import '../../features/payments/domain/repositories/payments_repository.dart';
import '../device/biometric_service.dart';
import '../device/device_id_service.dart';
import '../network/api_client.dart';
import '../storage/secure_storage.dart';

final sl = GetIt.instance;

/// Two-phase registration: [SecureStorage]/[DeviceIdService]/[AuthRepository] have no
/// [ApiClient] dependency (sign-in hits `/connect/token` and `/api/self/auth/otp/request`
/// directly, via the repository's own Dio instance), so they register first; [ApiClient]
/// then wraps the same [AuthRepository] for its 401-refresh interceptor before every other
/// feature registers.
void setupServiceLocator() {
  sl.registerLazySingleton<SecureStorage>(SecureStorage.new);
  sl.registerLazySingleton<DeviceIdService>(() => DeviceIdService(sl<SecureStorage>()));
  sl.registerLazySingleton<BiometricService>(BiometricService.new);

  sl.registerLazySingleton<AuthRepository>(() => AuthRepositoryImpl(sl<SecureStorage>(), sl<DeviceIdService>()));

  sl.registerLazySingleton<ApiClient>(() => ApiClient(sl<SecureStorage>(), sl<AuthRepository>()));

  sl.registerLazySingleton<AccountsRemoteDataSource>(() => AccountsRemoteDataSource(sl<ApiClient>().dio));
  sl.registerLazySingleton<AccountsRepository>(() => AccountsRepositoryImpl(sl<AccountsRemoteDataSource>()));

  sl.registerLazySingleton<PaymentsRemoteDataSource>(() => PaymentsRemoteDataSource(sl<ApiClient>().dio));
  sl.registerLazySingleton<PaymentsRepository>(() => PaymentsRepositoryImpl(sl<PaymentsRemoteDataSource>()));

  sl.registerLazySingleton<DividendsRemoteDataSource>(() => DividendsRemoteDataSource(sl<ApiClient>().dio));
  sl.registerLazySingleton<DividendsRepository>(() => DividendsRepositoryImpl(sl<DividendsRemoteDataSource>()));

  sl.registerLazySingleton<ShareMarketplaceRemoteDataSource>(
    () => ShareMarketplaceRemoteDataSource(sl<ApiClient>().dio),
  );
  sl.registerLazySingleton<ShareMarketplaceRepository>(
    () => ShareMarketplaceRepositoryImpl(sl<ShareMarketplaceRemoteDataSource>()),
  );

  sl.registerLazySingleton<BrandingRemoteDataSource>(() => BrandingRemoteDataSource(sl<ApiClient>().dio));
  sl.registerLazySingleton<BrandingRepository>(() => BrandingRepositoryImpl(sl<BrandingRemoteDataSource>()));
}
