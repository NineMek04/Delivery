import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../app/app_theme.dart';
import '../../core/api/services/order_api_service.dart';
import '../../models/order.dart';

class OrderReviewDialog extends ConsumerStatefulWidget {
  final OrderDto order;
  final VoidCallback? onReviewed;

  const OrderReviewDialog({
    super.key,
    required this.order,
    this.onReviewed,
  });

  static Future<bool?> show(
    BuildContext context, {
    required OrderDto order,
    VoidCallback? onReviewed,
  }) {
    return showDialog<bool>(
      context: context,
      barrierDismissible: true,
      builder: (context) => OrderReviewDialog(
        order: order,
        onReviewed: onReviewed,
      ),
    );
  }

  @override
  ConsumerState<OrderReviewDialog> createState() => _OrderReviewDialogState();
}

class _OrderReviewDialogState extends ConsumerState<OrderReviewDialog> {
  int _rating = 5;
  final TextEditingController _commentController = TextEditingController();
  bool _isSubmitting = false;

  @override
  void dispose() {
    _commentController.dispose();
    super.dispose();
  }

  Future<void> _submitReview() async {
    if (_rating < 1 || _rating > 5) return;

    setState(() => _isSubmitting = true);

    try {
      await ref.read(orderApiServiceProvider).submitReview(
            orderId: widget.order.id,
            rating: _rating,
            comment: _commentController.text.trim(),
          );

      if (mounted) {
        widget.onReviewed?.call();
        Navigator.of(context).pop(true);
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('⭐ ขอบคุณสำหรับคะแนนและความคิดเห็นของคุณ!'),
            backgroundColor: AppTheme.primaryColor,
            duration: Duration(seconds: 2),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        setState(() => _isSubmitting = false);
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('ส่งรีวิวไม่สำเร็จ: $e'),
            backgroundColor: AppTheme.errorColor,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Dialog(
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      backgroundColor: Colors.white,
      elevation: 8,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              // Top row: Title and Close button
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Row(
                    children: [
                      Icon(Icons.stars_rounded, color: Colors.amber, size: 28),
                      SizedBox(width: 8),
                      Text(
                        'ให้คะแนนบริการ',
                        style: TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.bold,
                          color: AppTheme.textLightPrimary,
                        ),
                      ),
                    ],
                  ),
                  IconButton(
                    icon: const Icon(Icons.close, color: Colors.grey),
                    splashRadius: 20,
                    tooltip: 'ปิด / ข้าม',
                    onPressed: _isSubmitting ? null : () => Navigator.of(context).pop(false),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              Text(
                'ออเดอร์ #${widget.order.trackingCode ?? widget.order.id.substring(0, 8)} จัดส่งสำเร็จแล้ว คุณพอใจกับบริการนี้มากน้อยเพียงใด?',
                style: const TextStyle(fontSize: 13, color: Colors.black54),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),

              // Star Rating Selector
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: List.generate(5, (index) {
                  final starIndex = index + 1;
                  return GestureDetector(
                    onTap: _isSubmitting
                        ? null
                        : () {
                            setState(() => _rating = starIndex);
                          },
                    child: Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 4),
                      child: Icon(
                        starIndex <= _rating ? Icons.star_rounded : Icons.star_outline_rounded,
                        size: 40,
                        color: starIndex <= _rating ? Colors.amber : Colors.grey.shade300,
                      ),
                    ),
                  );
                }),
              ),
              const SizedBox(height: 8),
              Text(
                _getRatingLabel(_rating),
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w600,
                  color: _rating >= 4 ? Colors.green.shade700 : (_rating == 3 ? Colors.amber.shade800 : Colors.deepOrange),
                ),
              ),
              const SizedBox(height: 16),

              // Comment TextField
              TextField(
                controller: _commentController,
                maxLines: 3,
                maxLength: 300,
                decoration: InputDecoration(
                  hintText: 'เขียนความคิดเห็นติชมเพิ่มเติม (ถ้ามี)...',
                  hintStyle: const TextStyle(fontSize: 13, color: Colors.black38),
                  filled: true,
                  fillColor: Colors.grey.shade50,
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                    borderSide: BorderSide(color: Colors.grey.shade200),
                  ),
                  enabledBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                    borderSide: BorderSide(color: Colors.grey.shade200),
                  ),
                  focusedBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(12),
                    borderSide: const BorderSide(color: AppTheme.primaryColor),
                  ),
                  contentPadding: const EdgeInsets.all(12),
                ),
              ),
              const SizedBox(height: 16),

              // Action Buttons: Skip & Submit
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        side: BorderSide(color: Colors.grey.shade300),
                      ),
                      onPressed: _isSubmitting ? null : () => Navigator.of(context).pop(false),
                      child: const Text(
                        'ข้ามไว้ก่อน',
                        style: TextStyle(color: Colors.black54, fontWeight: FontWeight.w600),
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: ElevatedButton(
                      style: ElevatedButton.styleFrom(
                        backgroundColor: AppTheme.primaryColor,
                        padding: const EdgeInsets.symmetric(vertical: 12),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        elevation: 2,
                      ),
                      onPressed: _isSubmitting ? null : _submitReview,
                      child: _isSubmitting
                          ? const SizedBox(
                              width: 20,
                              height: 20,
                              child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                            )
                          : const Text(
                              'ส่งรีวิว',
                              style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold),
                            ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _getRatingLabel(int rating) {
    switch (rating) {
      case 5:
        return 'ยอดเยี่ยมมาก! ⭐⭐⭐⭐⭐';
      case 4:
        return 'ดีมาก ⭐⭐⭐⭐';
      case 3:
        return 'ปานกลาง ⭐⭐⭐';
      case 2:
        return 'พอใช้ ⭐⭐';
      case 1:
        return 'ต้องปรับปรุง ⭐';
      default:
        return '';
    }
  }
}
