import 'package:uuid/uuid.dart';

import '../storage/secure_storage.dart';

/// A stable id for this install, generated once and kept in secure storage —
/// the unit `MemberTrustedDevice` trust is scoped to server-side (ADR 0008).
/// Reinstalling the app (secure storage wiped) means a fresh id and the
/// device going through SMS OTP again, same as a brand-new device.
class DeviceIdService {
  DeviceIdService(this._storage);

  final SecureStorage _storage;
  static const _uuid = Uuid();

  Future<String> getOrCreate() async {
    final existing = await _storage.readDeviceId();
    if (existing != null) return existing;
    final generated = _uuid.v4();
    await _storage.saveDeviceId(generated);
    return generated;
  }
}
