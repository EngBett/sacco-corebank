# sacco_member — Icodeio SACCO member app

App ID `co.ke.icodeio.sacco_member` (Android) / `co.ke.icodeio.saccoMember` (iOS). The launcher icon is generated from
`assets/branding/` with `dart run flutter_launcher_icons`; the logo on the sign-in screen comes from the tenant branding API.

The SACCO platform's member self-service mobile app (Flutter). See `CLAUDE.md` in this
directory and `docs/architecture/0008-mobile-scope.md` for the scope decision and the
backend endpoints it depends on.

## What it does

- Sign in with phone number + PIN via the hosted `/account/login` page (OIDC Authorization
  Code + PKCE, public client `mobile`, redirect `sacco://auth/callback`).
- View accounts and balances (FOSA, BOSA deposits, shares, fixed deposits) and per-account
  statements.
- Deposit into any own account via M-Pesa, Airtel Money, or Equity Bank.
- Request a withdrawal via M-Pesa, Airtel Money, or a bank account (Pesalink) — every request
  still needs a staff checker to approve it before payout (maker-checker).
- View dividends declared for any financial year, with status (Declared/Approved/Paid).

## Architecture

Feature-based clean architecture (`.claude/skills/flutter-enterprise`):

```
lib/
  core/           config, network (dio + auth interceptor), secure storage, DI (get_it),
                  router (go_router), theme, shared widgets/models/enums
  features/
    auth/         OIDC login via flutter_appauth
    accounts/     profile, accounts, summary, statements
    payments/     deposits (top-up) and withdrawals
    dividends/    dividends by financial year
    branding/     tenant branding (public endpoint) — themes the login screen
```

State management is Riverpod. Every `/api/self/*` DTO's enum fields are encoded/decoded by
their JSON string name (`JsonStringEnumConverter` on the API), never by number — see
`lib/core/models/enums.dart`.

## Running locally

The backend must be running first (`backend/CLAUDE.md` — `docker compose up -d postgres
mailpit mocked-sms`, seed, then `dotnet run --project backend/src/Sacco.Api`).

```bash
flutter pub get
# Android emulator (can't resolve `localhost` as the host machine):
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5000
# iOS simulator / physical device on the same network as the API host:
flutter run --dart-define=API_BASE_URL=http://<host-lan-ip>:5000
```

`API_BASE_URL` defaults to `http://10.0.2.2:5000` (Android emulator). `TENANT_SLUG` defaults
to `demo`. Sign in with any seeded member's phone number and PIN `2468`
(`backend/seed/README.md`).

## Tests

```bash
flutter analyze
flutter test
```

## Known issues

- **iOS build unverified on this dev machine**: `xcodebuild` itself crashes on this machine
  (`dlopen(@rpath/libxcodebuildLoader.dylib): Symbol not found: _XPCTypeBool` — Xcode 16.2's
  `CoreDevice.framework` vs. macOS 26.6.2's `Mercury.framework`, an ABI mismatch between the
  installed Xcode and OS version, not a Flutter or app issue). `sudo xcodebuild -runFirstLaunch`
  does not fix it. Update Xcode to a version that supports this OS build, then confirm with
  `flutter build ios --no-codesign`. Android is fully verified (`flutter analyze`, `flutter
  test`, `flutter build apk --debug` all green).
