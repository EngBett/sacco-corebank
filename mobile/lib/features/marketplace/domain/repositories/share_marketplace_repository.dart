import '../entities/share_listing.dart';

abstract class ShareMarketplaceRepository {
  /// Lists part of the member's own share capital for sale. Still maker-checker — this only
  /// creates the `Open` listing; a staff member approves once another member claims it.
  Future<ShareListing> listSharesForSale(double amount);

  /// Open listings from other members — never the caller's own.
  Future<List<ShareListing>> getOpenListings();

  /// The member's own activity: shares they're selling and purchases they've claimed.
  Future<List<ShareListing>> getMyListings();

  Future<ShareListing> claim(String listingId);

  Future<ShareListing> cancel(String listingId);
}
