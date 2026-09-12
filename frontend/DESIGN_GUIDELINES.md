# Frontend Design Guidelines (Next.js)

## Stack

- Next.js (App Router)
- Tailwind CSS
- shadcn/ui

This platform is a regulated financial product used by SACCO staff every day and by members
occasionally. The bar is: modern, clean, and trustworthy — never a bare CRUD admin screen,
but also never so decorative that it undermines the sense of financial precision the product
needs. Transactional screens (ledger entries, loan approvals, balances) favor clarity and
data density over illustration; marketing, onboarding, and empty states are where visual
warmth belongs.

---

## Component system

- Always use shadcn/ui components. Never hand-roll a raw HTML button/form/dialog when a
  shadcn equivalent exists.
- Extend shared components via a `packages/ui`-style local package if the same customization
  is needed in both `portal` and `public-site`.
- Buttons → shadcn `Button`. Forms → shadcn `Form` + inputs, with server-side validation
  mirrored from the backend's rules. Dialogs → shadcn `Dialog`. Data → shadcn `Card` / `Table`.

---

## Design guidelines

- Consistent spacing on the Tailwind spacing scale — no arbitrary pixel values.
- Rounded corners (`rounded-xl` / `rounded-2xl`) on cards and containers.
- Soft shadows for elevation, not heavy drop shadows.
- Grid layouts for listings (loan products, branches, member lists on the public site).
- Avoid cluttered UI — a screen showing member balances or loan status needs generous
  whitespace precisely because the numbers on it matter.

---

## Illustrations (mandatory on the right screens)

Use illustrations from **unDraw** (free, customizable SVG, no attribution required for
commercial use) on:
- Empty states (no loans yet, no transactions yet, no pending applications)
- Onboarding screens
- Public-site landing/marketing sections

Match illustration color to the active tenant's brand color. Use SVG. Do **not** use
illustrations on transactional back-office screens (ledger, approvals, reconciliation) —
those should read as precise and data-forward, not playful.

---

## Images (mandatory on the right screens)

Use **Pexels** (free for commercial use, modifiable, no attribution required) for:
- Public-site hero sections
- Branch/location imagery on the public site
- Fallback imagery where a SACCO hasn't yet uploaded their own branch photos

Optimize (Next.js `<Image>`, correct sizes) before use. Do not use decorative photography on
authenticated financial screens (dashboards, statements, approvals).

---

## Visual experience requirements

**Public site and onboarding**: every page includes at least one of — hero section with
image/illustration, card-based layout, empty-state illustration, image-driven listing.

**Portal (authenticated)**: every page prioritizes information hierarchy and data accuracy;
visual polish comes from spacing, typography, and consistent component use rather than
imagery.

---

## Layout strategy

### Public site pages
- Large hero section (tenant-branded)
- Product/services overview (savings, loans, membership benefits)
- Membership application call-to-action
- Branch locator / contact info
- Category navigation if the SACCO offers multiple product lines

### Portal — dashboard pages
- Sidebar layout
- Cards for summary data (portfolio at risk, pending approvals, today's transactions)
- Clean, validated forms for data entry

### Portal — admin/back-office pages
- Table + filters
- Minimal, dense, clean — this is where staff spend their whole day; optimize for speed of
  scanning, not visual flourish

---

## SEO & performance (public site especially)

- Server components wherever possible
- Optimized, lazy-loaded images
- Proper per-page metadata (title, description, OG tags) per tenant

---

## What not to do

- Do not ship an unstyled or visually bare screen anywhere in either app
- Do not ignore spacing/layout consistency
- Do not use raw unstyled HTML form elements where a shadcn component exists
- Do not decorate transactional/back-office screens with illustrations or stock photography
  — save visual richness for public-facing and empty-state moments

---

## Key principle

> This is a regulated financial platform serving real SACCOs and their members.

The UI must emphasize **clarity, trust, and data accuracy** everywhere money is shown or
moved, and **warmth, imagery, and approachability** everywhere a prospective or new member is
being welcomed in. Both things are true at once, on different screens — know which screen
you're building.
