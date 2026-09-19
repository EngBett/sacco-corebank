class TenantBranding {
  const TenantBranding({
    required this.slug,
    required this.name,
    required this.shortName,
    required this.primaryColor,
    required this.secondaryColor,
    required this.accentColor,
    required this.tagline,
    required this.supportEmail,
    required this.supportPhone,
    this.logoUrl,
  });

  factory TenantBranding.fromJson(Map<String, dynamic> json) => TenantBranding(
    slug: json['slug'] as String,
    name: json['name'] as String,
    shortName: json['shortName'] as String,
    primaryColor: json['primaryColor'] as String,
    secondaryColor: json['secondaryColor'] as String,
    accentColor: json['accentColor'] as String,
    logoUrl: json['logoUrl'] as String?,
    tagline: json['tagline'] as String,
    supportEmail: json['supportEmail'] as String,
    supportPhone: json['supportPhone'] as String,
  );

  final String slug;
  final String name;
  final String shortName;
  final String primaryColor;
  final String secondaryColor;
  final String accentColor;
  final String? logoUrl;
  final String tagline;
  final String supportEmail;
  final String supportPhone;
}

abstract class BrandingRepository {
  /// Public endpoint — tenant is resolved from `X-Tenant`, no member login needed.
  /// Themes the login screen before the member ever authenticates.
  Future<TenantBranding> getBranding();
}
