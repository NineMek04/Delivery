import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/auth/auth_service.dart';
import '../../../core/config/environment.dart';
import '../../../core/config/server_config_service.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../providers/auth_provider.dart';

/// Login Screen — email/password form wired to [AuthNotifier].
class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();

  @override
  void initState() {
    super.initState();
    // On native devices, if no server URL has been configured at all, guide the user to Server Settings
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!kIsWeb && Environment.apiBaseUrl.isEmpty) {
        context.push('/server-settings');
      }
    });
  }

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    debugPrint('[LoginScreen] _submit called');
    if (_formKey.currentState == null) {
      debugPrint('[LoginScreen] Error: _formKey.currentState is null!');
      return;
    }
    final isValid = _formKey.currentState!.validate();
    debugPrint('[LoginScreen] Form validation result: $isValid');
    if (!isValid) return;

    debugPrint('[LoginScreen] Inputs valid. Email: "${_emailController.text.trim()}", Password length: ${_passwordController.text.length}');
    try {
      debugPrint('[LoginScreen] Calling AuthNotifier.login...');
      await ref.read(authNotifierProvider.notifier).login(
        _emailController.text.trim(),
        _passwordController.text,
      );
      debugPrint('[LoginScreen] AuthNotifier.login completed');
    } catch (e, stack) {
      debugPrint('[LoginScreen] Exception during login call: $e\n$stack');
    }

    if (!mounted) {
      debugPrint('[LoginScreen] Widget not mounted after login call');
      return;
    }

    final authStatus = ref.read(authServiceProvider);
    final formState = ref.read(authNotifierProvider);
    debugPrint('[LoginScreen] Post-login - authStatus: $authStatus, formState.error: ${formState.error}, isLoading: ${formState.isLoading}');

    if (authStatus == AuthStatus.authenticated) {
      debugPrint('[LoginScreen] Authenticated! Navigating to home');
      context.go('/');
      return;
    }

    if (formState.error != null) {
      debugPrint('[LoginScreen] Login failed with error: ${formState.error}');
      ErrorDialog.show(
        context,
        title: 'เข้าสู่ระบบไม่สำเร็จ',
        message: formState.error!,
      );
    } else {
      debugPrint('[LoginScreen] Login failed without error message');
    }
  }

  @override
  Widget build(BuildContext context) {
    final authForm = ref.watch(authNotifierProvider);
    final activeServerUrl = ref.watch(serverUrlProvider);

    return Scaffold(
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        elevation: 0,
        actions: [
          IconButton(
            icon: const Icon(Icons.settings_outlined),
            tooltip: 'ตั้งค่า Server URL',
            onPressed: () => context.push('/server-settings'),
          ),
        ],
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Form(
              key: _formKey,
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(
                    Icons.delivery_dining,
                    size: 80,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                  const SizedBox(height: 16),
                  Text(
                    'Rider App',
                    style: Theme.of(context).textTheme.headlineLarge,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'เข้าสู่ระบบเพื่อเริ่มงานส่ง',
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  const SizedBox(height: 32),
                  TextFormField(
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    autofillHints: const [AutofillHints.email],
                    decoration: const InputDecoration(
                      labelText: 'อีเมล',
                      border: OutlineInputBorder(),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) {
                        return 'กรุณากรอกอีเมล';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: _passwordController,
                    obscureText: true,
                    autofillHints: const [AutofillHints.password],
                    onFieldSubmitted: (_) => _submit(),
                    decoration: const InputDecoration(
                      labelText: 'รหัสผ่าน',
                      border: OutlineInputBorder(),
                    ),
                    validator: (v) {
                      if (v == null || v.isEmpty) {
                        return 'กรุณากรอกรหัสผ่าน';
                      }
                      return null;
                    },
                  ),
                  if (authForm.error != null) ...[
                    const SizedBox(height: 16),
                    Text(
                      authForm.error!,
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.error,
                      ),
                    ),
                  ],
                  const SizedBox(height: 24),
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: authForm.isLoading ? null : _submit,
                      child: authForm.isLoading
                          ? const SizedBox(
                              height: 20,
                              width: 20,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Text('เข้าสู่ระบบ'),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(
                        'ยังไม่มีบัญชีผู้ใช้งาน?',
                        style: Theme.of(context).textTheme.bodyMedium,
                      ),
                      TextButton(
                        onPressed: () => context.go('/register'),
                        child: const Text('สมัครสมาชิก'),
                      ),
                    ],
                  ),
                  const SizedBox(height: 20),
                  // ── Server URL Status Chip ──
                  InkWell(
                    onTap: () => context.push('/server-settings'),
                    borderRadius: BorderRadius.circular(20),
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                      decoration: BoxDecoration(
                        color: Colors.white.withValues(alpha: 0.05),
                        borderRadius: BorderRadius.circular(20),
                        border: Border.all(
                          color: activeServerUrl.isNotEmpty
                              ? Colors.green.withValues(alpha: 0.5)
                              : Colors.orange.withValues(alpha: 0.5),
                        ),
                      ),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(
                            Icons.dns_rounded,
                            size: 14,
                            color: activeServerUrl.isNotEmpty ? Colors.greenAccent : Colors.orangeAccent,
                          ),
                          const SizedBox(width: 6),
                          ConstrainedBox(
                            constraints: const BoxConstraints(maxWidth: 240),
                            child: Text(
                              activeServerUrl.isNotEmpty
                                  ? (Uri.tryParse(activeServerUrl)?.host.isNotEmpty == true
                                      ? Uri.parse(activeServerUrl).host
                                      : activeServerUrl)
                                  : '⚙️ ตั้งค่า Server URL',
                              style: const TextStyle(fontSize: 12, color: Colors.white70),
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
