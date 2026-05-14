/// Authentication constants.
///
/// เทียบกับ:
/// - .NET: `BackendApi/Security/AuthConstants.cs`
///
/// Roles ตรงกับ policy ที่ตั้งไว้ใน BackendApi SecurityConfiguration:
/// - AdminOnly, Operations, Rider
class AuthConstants {
  AuthConstants._();

  // ── JWT Claim Types ────────────────────────────────────────────────
  /// ตรงกับ ClaimTypes.NameIdentifier ใน .NET
  static const String claimUserId = 'nameid';

  /// ตรงกับ ClaimTypes.Email ใน .NET
  static const String claimEmail = 'email';

  /// ตรงกับ ClaimTypes.Name ใน .NET
  static const String claimName = 'unique_name';

  /// ตรงกับ ClaimTypes.Role ใน .NET
  static const String claimRole = 'role';

  // ── Role Names ─────────────────────────────────────────────────────
  /// ตรงกับ policy "AdminOnly" ใน BackendApi
  static const String roleAdmin = 'Admin';

  /// ตรงกับ policy "Operations" ใน BackendApi
  static const String roleOperations = 'Operations';

  /// ตรงกับ policy "Rider" ใน BackendApi
  static const String roleRider = 'Rider';
}
