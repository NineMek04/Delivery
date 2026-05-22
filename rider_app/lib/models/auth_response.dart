import '../core/api/api_helpers.dart';

/// Auth response from POST /api/v1/auth/login|refresh.
class AuthResponse {
  final String accessToken;
  final String refreshToken;
  final DateTime? expiresAt;
  final UserInfo user;

  const AuthResponse({
    required this.accessToken,
    required this.refreshToken,
    this.expiresAt,
    required this.user,
  });

  factory AuthResponse.fromJson(Map<String, dynamic> json) {
    final accessToken =
        readField<String>(json, 'AccessToken') ??
        readField<String>(json, 'accessToken');
    final refreshToken =
        readField<String>(json, 'RefreshToken') ??
        readField<String>(json, 'refreshToken');

    if (accessToken == null || refreshToken == null) {
      throw const ApiException('Invalid auth response: missing tokens');
    }

    final expiresRaw =
        readField<String>(json, 'ExpiresAt') ?? readField<String>(json, 'expiresAt');
    final userRaw = readField<Map<String, dynamic>>(json, 'User') ??
        readField<Map<String, dynamic>>(json, 'user');

    return AuthResponse(
      accessToken: accessToken,
      refreshToken: refreshToken,
      expiresAt: expiresRaw != null ? DateTime.tryParse(expiresRaw) : null,
      user: userRaw != null
          ? UserInfo.fromJson(userRaw)
          : const UserInfo(id: '', email: '', fullName: '', role: ''),
    );
  }
}

/// User info embedded in auth responses.
class UserInfo {
  final String id;
  final String email;
  final String fullName;
  final String role;
  final String? riderId;
  final String? trackingCode;

  const UserInfo({
    required this.id,
    required this.email,
    required this.fullName,
    required this.role,
    this.riderId,
    this.trackingCode,
  });

  factory UserInfo.fromJson(Map<String, dynamic> json) {
    return UserInfo(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      email:
          readField<String>(json, 'Email') ?? readField<String>(json, 'email') ?? '',
      fullName:
          readField<String>(json, 'FullName') ??
          readField<String>(json, 'fullName') ??
          '',
      role: readField<String>(json, 'Role') ?? readField<String>(json, 'role') ?? '',
      riderId:
          readField<String>(json, 'RiderId') ?? readField<String>(json, 'riderId'),
      trackingCode:
          readField<String>(json, 'TrackingCode') ??
          readField<String>(json, 'trackingCode'),
    );
  }

  Map<String, dynamic> toJson() => {
    'Id': id,
    'Email': email,
    'FullName': fullName,
    'Role': role,
    if (riderId != null) 'RiderId': riderId,
    if (trackingCode != null) 'TrackingCode': trackingCode,
  };
}
