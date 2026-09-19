# ADR 0017: SACCO-managed product listings and services on the public website

## Status
Accepted (2026-09-17)

## Context
The public site's Products page showed each product's operational figures: rate, amount range, term, notice period. It
had nothing else to work with. Kenyan SACCO websites present products the way members think about them. Imarisha
SACCO, which the product owner pointed to, uses:
- **Savings accounts with a services list:** mobile banking, ATM, SMS alerts, standing orders.
- **FOSA loans:** salary advances and salary-based loans.
- **BOSA loans:** deposit-based loans repaid by check-off.
- **MSME loans:** groups and chamas, businesses, farmers, asset finance.

Each product is described in plain language: what it's for, its benefits, who qualifies, what to bring, and often an
amount rule the platform can't compute ("up to 90% of your net salary"). The request was to copy that structure but
drive every word from the API, so each SACCO publishes its own products rather than Imarisha's.

## Decision
- **A `PublicListing` value** (Sacco.Shared) is owned by both `SavingsProduct` and `LoanProduct`. It holds `ShowOnPublicSite`,
  `DisplayOrder`, `Features[]`, `Requirements[]`, `AmountNote` and `ApplicationFormUrl`. It is stored as `listing_*` columns
  on each module's `products` table, with text arrays for the lists.
- **Validation keeps the content readable and safe:**
  - at most 12 lines of up to 200 characters each;
  - the form link must be http(s) or a same-site path.
- **Presentation only:** a listing is edited through its own endpoints (`PUT /api/savings/products/{code}/listing`,
  `PUT /api/loans/products/{code}/listing`) under the existing product-management permissions. It never changes rates,
  eligibility or GL mapping; hiding a product from the website does not deactivate it.
- **Loan categories:** `LoanProduct.Category` (`Fosa`, `Bosa`, `Msme`) groups loans on the website. It defaults to the segment,
  so existing products migrate as FOSA or BOSA, and MSME is chosen explicitly. It is deliberately separate from `Segment`:
  an MSME loan is still booked in a FOSA or BOSA segment.
- **Services** are `platform.public_services` rows (name, description, icon key, order, active), managed by
  `admin.tenant.manage` and published at `GET /api/public/services`. Icons come from a fixed key list that the websites
  know how to draw.
- **Public reads** (`/api/public/products/*`, `/api/public/services`) return only active, listed items in display order, under
  a per-IP `public-read` rate limit of 120/min. The old shared `public` limit of 10/min was too tight for page renders and
  stays on membership applications.
- **Seed data** (`Seed/Data/PublicCatalogue.cs`) gives the demo tenant generic, Imarisha-inspired content:
  - listings for the existing savings and loan products;
  - seven more loans across FOSA, BOSA and MSME;
  - nine services.
  Missing listings are filled in; text a SACCO has written is never overwritten.

## Consequences
- **Public site:** the products area splits into Savings & services, FOSA loans, BOSA loans and MSME loans, and every product
  and service comes from the tenant's own data.
- **Portal:** product managers edit listings, and tenant administrators manage services.
- **No new savings accounts yet:** the demo has no junior or retirement account. Several services pick "the" FOSA or
  shares product by kind (`FirstAsync(p => p.Kind == …)`), so a second product of the same kind needs those lookups made
  explicit first.
- **Uploaded forms:** application forms are links; hosting the files, like logos, is the SACCO's choice (tenant assets
  or any https URL).
