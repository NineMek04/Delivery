import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/auth/auth_service.dart';
import '../../../shared/widgets/error_dialog.dart';
import '../providers/auth_provider.dart';

/// Registration Screen — allows creating new accounts with role selection.
class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _fullNameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  
  String _selectedRole = 'Rider';
  bool _obscurePassword = true;

  final List<Map<String, String>> _roles = const [
    {'value': 'Rider', 'label': 'Rider (ผู้จัดส่ง)'},
    {'value': 'Customer', 'label': 'Customer (ผู้สั่งซื้อ)'},
    {'value': 'StorePartner', 'label': 'Store Partner (ร้านค้าพันธมิตร)'},
  ];

  @override
  void dispose() {
    _fullNameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    debugPrint('[RegisterScreen] _submit called');
    if (_formKey.currentState == null) return;
    
    final isValid = _formKey.currentState!.validate();
    debugPrint('[RegisterScreen] Form validation: $isValid');
    if (!isValid) return;

    try {
      debugPrint('[RegisterScreen] Invoking AuthNotifier.register...');
      await ref.read(authNotifierProvider.notifier).register(
            email: _emailController.text.trim(),
            password: _passwordController.text,
            fullName: _fullNameController.text.trim(),
            role: _selectedRole,
          );
      debugPrint('[RegisterScreen] AuthNotifier.register completed');
    } catch (e, stack) {
      debugPrint('[RegisterScreen] Exception during register call: $e\n$stack');
    }

    if (!mounted) return;

    final authStatus = ref.read(authServiceProvider);
    final formState = ref.read(authNotifierProvider);

    if (authStatus == AuthStatus.authenticated) {
      debugPrint('[RegisterScreen] Authenticated! Navigating to home');
      context.go('/');
      return;
    }

    if (formState.error != null) {
      debugPrint('[RegisterScreen] Registration failed: ${formState.error}');
      ErrorDialog.show(
        context,
        title: 'ลงทะเบียนไม่สำเร็จ',
        message: formState.error!,
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final authForm = ref.watch(authNotifierProvider);

    return Scaffold(
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
                    Icons.app_registration_rounded,
                    size: 80,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                  const SizedBox(height: 16),
                  Text(
                    'สร้างบัญชีใหม่',
                    style: Theme.of(context).textTheme.headlineLarge,
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'กรอกข้อมูลเพื่อลงทะเบียนเข้าสู่ระบบ',
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  const SizedBox(height: 32),
                  
                  // Full Name Field
                  TextFormField(
                    controller: _fullNameController,
                    keyboardType: TextInputType.name,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(
                      labelText: 'ชื่อ-นามสกุล',
                      prefixIcon: Icon(Icons.person_outline),
                      border: OutlineInputBorder(),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) {
                        return 'กรุณากรอกชื่อ-นามสกุล';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // Email Field
                  TextFormField(
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    autofillHints: const [AutofillHints.email],
                    decoration: const InputDecoration(
                      labelText: 'อีเมล',
                      prefixIcon: Icon(Icons.email_outlined),
                      border: OutlineInputBorder(),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) {
                        return 'กรุณากรอกอีเมล';
                      }
                      final emailRegExp = RegExp(r'^[\w-\.]+@([\w-]+\.)+[\w-]{2,4}$');
                      if (!emailRegExp.hasMatch(v.trim())) {
                        return 'รูปแบบอีเมลไม่ถูกต้อง';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // Password Field
                  TextFormField(
                    controller: _passwordController,
                    obscureText: _obscurePassword,
                    autofillHints: const [AutofillHints.password],
                    decoration: InputDecoration(
                      labelText: 'รหัสผ่าน',
                      prefixIcon: const Icon(Icons.lock_outline),
                      suffixIcon: IconButton(
                        icon: Icon(
                          _obscurePassword ? Icons.visibility_off_outlined : Icons.visibility_outlined,
                        ),
                        onPressed: () {
                          setState(() {
                            _obscurePassword = !_obscurePassword;
                          });
                        },
                      ),
                      border: const OutlineInputBorder(),
                    ),
                    validator: (v) {
                      if (v == null || v.isEmpty) {
                        return 'กรุณากรอกรหัสผ่าน';
                      }
                      if (v.length < 12) {
                        return 'รหัสผ่านต้องมีอย่างน้อย 12 ตัวอักษร';
                      }
                      if (v.length > 128) {
                        return 'รหัสผ่านต้องยาวไม่เกิน 128 ตัวอักษร';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // Role Dropdown Field
                  DropdownButtonFormField<String>(
                    value: _selectedRole,
                    decoration: const InputDecoration(
                      labelText: 'บทบาท (Role)',
                      prefixIcon: Icon(Icons.shield_outlined),
                      border: OutlineInputBorder(),
                    ),
                    items: _roles.map((role) {
                      return DropdownMenuItem<String>(
                        value: role['value'],
                        child: Text(role['label']!),
                      );
                    }).toList(),
                    onChanged: (val) {
                      if (val != null) {
                        setState(() {
                          _selectedRole = val;
                        });
                      }
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
                  
                  // Submit Button
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
                          : const Text('ลงทะเบียน'),
                    ),
                  ),
                  const SizedBox(height: 16),
                  
                  // Link to Login
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(
                        'มีบัญชีผู้ใช้งานอยู่แล้ว?',
                        style: Theme.of(context).textTheme.bodyMedium,
                      ),
                      TextButton(
                        onPressed: () => context.go('/login'),
                        child: const Text('เข้าสู่ระบบ'),
                      ),
                    ],
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
