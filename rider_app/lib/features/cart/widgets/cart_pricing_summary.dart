import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

class CartPricingSummary extends StatelessWidget {
  final double subtotal;
  final double deliveryFee;
  final double distance;
  final bool calculatingRoute;
  final VoidCallback? onCheckout;

  const CartPricingSummary({
    super.key,
    required this.subtotal,
    required this.deliveryFee,
    required this.distance,
    required this.calculatingRoute,
    required this.onCheckout,
  });

  @override
  Widget build(BuildContext context) {
    final grandTotal = subtotal + deliveryFee;
    final formatCurrency = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0);

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            const Text('ราคารวม', style: TextStyle(color: Colors.grey)),
            Text(formatCurrency.format(subtotal)),
          ],
        ),
        const SizedBox(height: 8),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            const Text('ค่าส่ง (Estimated)', style: TextStyle(color: Colors.grey)),
            calculatingRoute
                ? const SizedBox(
                    width: 12,
                    height: 12,
                    child: CircularProgressIndicator(strokeWidth: 1.5),
                  )
                : Text(
                    '${formatCurrency.format(deliveryFee)} (${distance.toStringAsFixed(1)} กม.)',
                  ),
          ],
        ),
        const Divider(height: 24),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            const Text(
              'ราคารวมทั้งหมด',
              style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold),
            ),
            Text(
              formatCurrency.format(grandTotal),
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.bold,
                color: Color(0xFF6366F1),
              ),
            ),
          ],
        ),
        const SizedBox(height: 20),
        ElevatedButton(
          onPressed: calculatingRoute ? null : onCheckout,
          style: ElevatedButton.styleFrom(
            padding: const EdgeInsets.symmetric(vertical: 14),
            backgroundColor: const Color(0xFF6366F1),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(14),
            ),
          ),
          child: const Text(
            'สั่งซื้อออเดอร์',
            style: TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.bold,
              color: Colors.white,
            ),
          ),
        ),
      ],
    );
  }
}
