import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:go_router/go_router.dart';
import '../providers/delivery_provider.dart';

class DeliveryConfirmationScreen extends ConsumerStatefulWidget {
  final String orderId;

  const DeliveryConfirmationScreen({super.key, required this.orderId});

  @override
  ConsumerState<DeliveryConfirmationScreen> createState() =>
      _DeliveryConfirmationScreenState();
}

class _DeliveryConfirmationScreenState
    extends ConsumerState<DeliveryConfirmationScreen> {
  final Map<String, bool> _checklist = {
    'Food is sealed and untampered': false,
    'All items are present': false,
    'Temperature condition met': false,
  };

  final TextEditingController _notesController = TextEditingController();
  bool _isLoading = false;

  @override
  void dispose() {
    _notesController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_checklist.values.any((v) => !v)) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Please complete the checklist.')),
      );
      return;
    }

    setState(() => _isLoading = true);

    try {
      await ref
          .read(deliveryNotifierProvider.notifier)
          .updateOrderStatus(widget.orderId, 'COMPLETED');

      final state = ref.read(deliveryNotifierProvider);
      if (state.error != null && !state.error!.startsWith('Offline:')) {
        throw Exception(state.error);
      }

      HapticFeedback.heavyImpact();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: const Text('ยืนยันจัดส่งสำเร็จ!'),
            backgroundColor: Colors.green[700],
          ),
        );
        context.goNamed('home');
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('เกิดข้อผิดพลาด: $e'),
            backgroundColor: Colors.red[700],
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(
          'Confirm Delivery',
          style: GoogleFonts.poppins(
              fontWeight: FontWeight.bold, fontSize: 18, color: Colors.black87),
        ),
        backgroundColor: Colors.white,
        elevation: 0,
        iconTheme: const IconThemeData(color: Colors.black87),
      ),
      body: _isLoading
          ? const Center(child: CircularProgressIndicator())
          : SingleChildScrollView(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Condition checklist
                  Text(
                    'Condition Checklist',
                    style: GoogleFonts.poppins(
                        fontSize: 16, fontWeight: FontWeight.bold),
                  ),
                  const SizedBox(height: 8),
                  Container(
                    decoration: BoxDecoration(
                      border: Border.all(color: Colors.grey[300]!),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Column(
                      children: _checklist.keys.map((key) {
                        return CheckboxListTile(
                          title: Text(key,
                              style: GoogleFonts.poppins(fontSize: 14)),
                          value: _checklist[key],
                          activeColor: Colors.blueAccent,
                          onChanged: (v) =>
                              setState(() => _checklist[key] = v ?? false),
                        );
                      }).toList(),
                    ),
                  ),
                  const SizedBox(height: 24),

                  // Notes
                  Text(
                    'Driver Notes (Optional)',
                    style: GoogleFonts.poppins(
                        fontSize: 16, fontWeight: FontWeight.bold),
                  ),
                  const SizedBox(height: 8),
                  TextField(
                    controller: _notesController,
                    maxLines: 3,
                    decoration: InputDecoration(
                      hintText: 'e.g. Left package at the front door.',
                      hintStyle:
                          GoogleFonts.poppins(color: Colors.grey[400]),
                      border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(12)),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(12),
                        borderSide: const BorderSide(
                            color: Colors.blueAccent, width: 2),
                      ),
                    ),
                  ),
                  const SizedBox(height: 32),

                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _submit,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.blueAccent,
                        padding: const EdgeInsets.symmetric(vertical: 16),
                        shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12)),
                      ),
                      child: Text(
                        'CONFIRM DELIVERY',
                        style: GoogleFonts.poppins(
                            fontSize: 16,
                            fontWeight: FontWeight.bold,
                            color: Colors.white),
                      ),
                    ),
                  ),
                  const SizedBox(height: 24),
                ],
              ),
            ),
    );
  }
}
