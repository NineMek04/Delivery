import 'package:jwt_decoder/jwt_decoder.dart';
import 'package:logger/logger.dart';
import '../../models/auth_response.dart';
import 'auth_constants.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Helper class for decoding JWT and reading claims.
class AuthClaimsHelper {
  static Map<String, dynamic>? decodeToken(String? token) {
    if (token == null) return null;
    try {
      return JwtDecoder.decode(token);
    } catch (e) {
      _logger.e('❌ Failed to decode token', error: e);
      return null;
    }
  }

  static String? getUserId(Map<String, dynamic>? claims) {
    return claims?[AuthConstants.claimUserId] ??
        claims?['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
  }

  static String? getUserName(Map<String, dynamic>? claims) {
    return claims?[AuthConstants.claimName] ??
        claims?['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'];
  }

  static String? getUserRole(Map<String, dynamic>? claims) {
    return claims?[AuthConstants.claimRole] ??
        claims?['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
  }

  static String? getUserEmail(Map<String, dynamic>? claims) {
    return claims?[AuthConstants.claimEmail] ??
        claims?['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'];
  }

  static UserInfo? getCurrentUser(Map<String, dynamic>? claims) {
    if (claims == null) return null;
    return UserInfo(
      id: getUserId(claims) ?? '',
      email: getUserEmail(claims) ?? '',
      fullName: getUserName(claims) ?? '',
      role: getUserRole(claims) ?? '',
    );
  }
}
