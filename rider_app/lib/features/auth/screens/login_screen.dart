import 'package:flutter/material.dart';

/// Login Screen — หน้า Login สำหรับ Rider.
///
/// เทียบกับ:
/// - Angular: จะมี Login component ใน admin-dashboard ภายหลัง
/// - .NET: จะมี AuthController รับ login request
///
/// TODO: ใส่ UI จริง — form fields (email, password), validation, login button
class LoginScreen extends StatelessWidget {
  const LoginScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(24.0),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                // Logo / App name
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
                const SizedBox(height: 48),

                // Placeholder — TODO: Replace with actual login form
                Text(
                  '[ Login Form Placeholder ]',
                  style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                    color: Theme.of(context).colorScheme.primary,
                  ),
                ),
                const SizedBox(height: 24),

                // Placeholder login button
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton(
                    onPressed: () {
                      // TODO: Implement actual login logic
                      // → Call BackendApi AuthController
                      // → Save JWT token via AuthService
                    },
                    child: const Text('เข้าสู่ระบบ'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
