import '../../domain/entities/share_listing.dart';
import '../../domain/repositories/share_marketplace_repository.dart';
import '../datasources/share_marketplace_remote_datasource.dart';

class ShareMarketplaceRepositoryImpl implements ShareMarketplaceRepository {
  ShareMarketplaceRepositoryImpl(this._remote);
  final ShareMarketplaceRemoteDataSource _remote;

  @override
  Future<ShareListing> listSharesForSale(double amount) async =>
      ShareListing.fromJson(await _remote.listSharesForSale(amount));

  @override
  Future<List<ShareListing>> getOpenListings() async =>
      (await _remote.getOpenListings()).map(ShareListing.fromJson).toList();

  @override
  Future<List<ShareListing>> getMyListings() async =>
      (await _remote.getMyListings()).map(ShareListing.fromJson).toList();

  @override
  Future<ShareListing> claim(String listingId) async => ShareListing.fromJson(await _remote.claim(listingId));

  @override
  Future<ShareListing> cancel(String listingId) async => ShareListing.fromJson(await _remote.cancel(listingId));
}
