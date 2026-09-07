import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/app_theme.dart';
import '../../../core/config/environment.dart';
import '../../../core/config/server_config_service.dart';

/// Screen allowing the user to configure, test, and save a dynamic Server URL (e.g. Cloudflare Tunnel).
class ServerSettingsScreen extends ConsumerStatefulWidget {
  const ServerSettingsScreen({super.key});

  @override
  ConsumerState<ServerSettingsScreen> createState() => _ServerSettingsScreenState();
}

class _ServerSettingsScreenState extends ConsumerState<ServerSettingsScreen> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _urlController;

  bool _isTesting = false;
  ServerConnectionResult? _lastTestResult;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    // Initialize controller with currently active base URL
    _urlController = TextEditingController(text: Environment.apiBaseUrl);

    // If there is already an active URL, test connection on open in background
    if (Environment.apiBaseUrl.isNotEmpty) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        _testConnectionSilently();
      });
    }
  }

  @override
  void dispose() {
    _urlController.dispose();
    super.dispose();
  }

  Future<void> _testConnectionSilently() async {
    if (!mounted) return;
    setState(() => _isTesting = true);
    final service = ref.read(serverConfigServiceProvider);
    final result = await service.testConnection(Environment.apiBaseUrl);
    if (!mounted) return;
    setState(() {
      _isTesting = false;
      _lastTestResult = result;
    });
  }

  Future<void> _pasteFromClipboard() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    if (data?.text != null && data!.text!.trim().isNotEmpty) {
      setState(() {
        _urlController.text = data.text!.trim();
        _errorMessage = null;
        _lastTestResult = null;
      });
    }
  }

  Future<void> _connectAndSave({bool skipHealthCheck = false}) async {
    if (!_formKey.currentState!.validate()) return;

    final inputUrl = _urlController.text.trim();
    setState(() {
      _isTesting = true;
      _errorMessage = null;
    });

    final service = ref.read(serverConfigServiceProvider);

    if (!skipHealthCheck) {
      final testResult = await service.testConnection(inputUrl);
      if (!mounted) return;

      setState(() {
        _isTesting = false;
        _lastTestResult = testResult;
      });

      if (!testResult.success) {
        setState(() {
          _errorMessage = testResult.message;
        });
        return;
      }
    } else {
      setState(() => _isTesting = false);
    }

    // Save and update providers
    await service.saveServerUrl(inputUrl);
    ref.read(serverUrlProvider.notifier).state = inputUrl;

    if (!mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Row(
          children: [
            const Icon(Icons.check_circle, color: Colors.white),
            const SizedBox(width: 8),
            Expanded(child: Text('เชื่อมต่อสำเร็จ: $inputUrl')),
          ],
        ),
        backgroundColor: Colors.green.shade700,
        behavior: SnackBarBehavior.floating,
      ),
    );

    // Return to previous screen or /login
    if (context.canPop()) {
      context.pop();
    } else {
      context.go('/login');
    }
  }

  Future<void> _resetToDefault() async {
    final service = ref.read(serverConfigServiceProvider);
    await service.clearServerUrl();
    ref.read(serverUrlProvider.notifier).state = Environment.apiBaseUrl;

    setState(() {
      _urlController.text = Environment.apiBaseUrl;
      _lastTestResult = null;
      _errorMessage = null;
    });

    if (!mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('รีเซ็ตเป็นค่าเริ่มต้นเรียบร้อยแล้ว'),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final activeUrl = ref.watch(serverUrlProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('ตั้งค่า Server URL'),
        leading: context.canPop()
            ? IconButton(
                icon: const Icon(Icons.arrow_back),
                onPressed: () => context.pop(),
              )
            : null,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'ทดสอบเชื่อมต่อใหม่',
            onPressed: _isTesting ? null : () => _connectAndSave(skipHealthCheck: false),
          ),
        ],
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Form(
            key: _formKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // ── Status Overview Card ──
                _buildStatusCard(activeUrl),

                const SizedBox(height: 24),

                // ── Section Title ──
                Text(
                  'กำหนด Server Public URL',
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.bold,
                        color: Colors.white,
                      ),
                ),
                const SizedBox(height: 8),
                Text(
                  'ป้อน Cloudflare Tunnel URL (เช่น https://xxxx.trycloudflare.com) หรือ IP เซิร์ฟเวอร์ ระบบจะบันทึกไว้ในเครื่องอัตโนมัติ ทำให้ใช้งาน APK เดิมได้ตลอดเวลา',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                        color: Colors.white70,
                        height: 1.4,
                      ),
                ),

                const SizedBox(height: 16),

                // ── URL Input Field ──
                TextFormField(
                  controller: _urlController,
                  keyboardType: TextInputType.url,
                  autocorrect: false,
                  enableSuggestions: false,
                  style: const TextStyle(fontFamily: 'monospace', fontSize: 14),
                  decoration: InputDecoration(
                    labelText: 'Server URL',
                    hintText: 'https://xxxx.trycloudflare.com',
                    prefixIcon: const Icon(Icons.dns_rounded),
                    suffixIcon: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (_urlController.text.isNotEmpty)
                          IconButton(
                            icon: const Icon(Icons.clear, size: 20),
                            onPressed: () => setState(() => _urlController.clear()),
                          ),
                        IconButton(
                          icon: const Icon(Icons.content_paste_rounded, size: 20),
                          tooltip: 'วางจากคลิปบอร์ด',
                          onPressed: _pasteFromClipboard,
                        ),
                      ],
                    ),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                    ),
                    filled: true,
                    fillColor: AppTheme.surfaceDark,
                  ),
                  validator: (value) {
                    if (value == null || value.trim().isEmpty) {
                      return 'กรุณาระบุ Server URL';
                    }
                    final trimmed = value.trim();
                    if (!trimmed.startsWith('http://') && !trimmed.startsWith('https://')) {
                      return 'URL ต้องขึ้นต้นด้วย http:// หรือ https://';
                    }
                    return null;
                  },
                ),

                const SizedBox(height: 12),

                // Quick Paste Chip
                Align(
                  alignment: Alignment.centerLeft,
                  child: ActionChip(
                    avatar: const Icon(Icons.paste_rounded, size: 16),
                    label: const Text('วาง URL จากคลิปบอร์ด', style: TextStyle(fontSize: 12)),
                    onPressed: _pasteFromClipboard,
                  ),
                ),

                if (_errorMessage != null) ...[
                  const SizedBox(height: 16),
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color: Colors.red.shade900.withValues(alpha: 0.3),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: Colors.red.shade600),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            const Icon(Icons.error_outline, color: Colors.redAccent, size: 20),
                            const SizedBox(width: 8),
                            const Expanded(
                              child: Text(
                                'ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ได้',
                                style: TextStyle(fontWeight: FontWeight.bold, color: Colors.redAccent),
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 6),
                        Text(
                          _errorMessage!,
                          style: const TextStyle(fontSize: 12, color: Colors.white70),
                        ),
                        const SizedBox(height: 10),
                        OutlinedButton.icon(
                          onPressed: () => _connectAndSave(skipHealthCheck: true),
                          icon: const Icon(Icons.save, size: 16),
                          label: const Text('บันทึกต่อไปโดยไม่ตรวจสอบ', style: TextStyle(fontSize: 12)),
                          style: OutlinedButton.styleFrom(
                            foregroundColor: Colors.orangeAccent,
                            side: const BorderSide(color: Colors.orangeAccent),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],

                const SizedBox(height: 24),

                // ── Primary Action Button [ Connect ] ──
                SizedBox(
                  height: 50,
                  child: ElevatedButton(
                    onPressed: _isTesting ? null : () => _connectAndSave(skipHealthCheck: false),
                    style: ElevatedButton.styleFrom(
                      backgroundColor: AppTheme.primaryColor,
                      foregroundColor: Colors.white,
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                      ),
                      elevation: 2,
                    ),
                    child: _isTesting
                        ? const Row(
                            mainAxisAlignment: MainAxisAlignment.center,
                            children: [
                              SizedBox(
                                width: 20,
                                height: 20,
                                child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                              ),
                              SizedBox(width: 12),
                              Text('กำลังทดสอบการเชื่อมต่อ...'),
                            ],
                          )
                        : const Row(
                            mainAxisAlignment: MainAxisAlignment.center,
                            children: [
                              Icon(Icons.cloud_done_rounded),
                              SizedBox(width: 8),
                              Text(
                                'เชื่อมต่อและบันทึก (Connect)',
                                style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
                              ),
                            ],
                          ),
                  ),
                ),

                const SizedBox(height: 16),

                // ── Secondary Action: Reset to default ──
                TextButton.icon(
                  onPressed: _isTesting ? null : _resetToDefault,
                  icon: const Icon(Icons.restart_alt_rounded, size: 18),
                  label: const Text('รีเซ็ตเป็นค่าเริ่มต้น (Reset Default)'),
                  style: TextButton.styleFrom(
                    foregroundColor: Colors.white60,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildStatusCard(String activeUrl) {
    final bool isConfigured = activeUrl.isNotEmpty;
    final bool isOnline = _lastTestResult?.success == true;

    Color badgeColor = Colors.orange;
    String statusText = 'ยังไม่ได้ตั้งค่า';
    IconData statusIcon = Icons.warning_amber_rounded;

    if (isConfigured) {
      if (_isTesting) {
        badgeColor = Colors.blue;
        statusText = 'กำลังตรวจสอบ...';
        statusIcon = Icons.sync;
      } else if (isOnline) {
        badgeColor = Colors.green;
        statusText = 'ออนไลน์ (${_lastTestResult?.latencyMs ?? 0} ms)';
        statusIcon = Icons.check_circle_rounded;
      } else if (_lastTestResult != null && !_lastTestResult!.success) {
        badgeColor = Colors.red;
        statusText = 'เชื่อมต่อไม่สำเร็จ';
        statusIcon = Icons.error_rounded;
      } else {
        badgeColor = Colors.green.shade400;
        statusText = 'กำหนดค่าแล้ว';
        statusIcon = Icons.link_rounded;
      }
    }

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.surfaceDark,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: badgeColor.withValues(alpha: 0.4), width: 1.5),
        boxShadow: [
          BoxShadow(
            color: badgeColor.withValues(alpha: 0.1),
            blurRadius: 10,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text(
                'สถานะเซิร์ฟเวอร์ปัจจุบัน',
                style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14, color: Colors.white),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                decoration: BoxDecoration(
                  color: badgeColor.withValues(alpha: 0.2),
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(color: badgeColor, width: 1),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(statusIcon, size: 14, color: badgeColor),
                    const SizedBox(width: 4),
                    Text(
                      statusText,
                      style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold, color: badgeColor),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            isConfigured ? activeUrl : '(ไม่มี URL เซิร์ฟเวอร์ที่กำหนด)',
            style: TextStyle(
              fontFamily: 'monospace',
              fontSize: 13,
              color: isConfigured ? Colors.white : Colors.white38,
            ),
          ),
        ],
      ),
    );
  }
}
