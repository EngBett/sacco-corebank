import '../../domain/repositories/branding_repository.dart';
import '../datasources/branding_remote_datasource.dart';

class BrandingRepositoryImpl implements BrandingRepository {
  BrandingRepositoryImpl(this._remote);
  final BrandingRemoteDataSource _remote;

  @override
  Future<TenantBranding> getBranding() async => TenantBranding.fromJson(await _remote.getBranding());
}
