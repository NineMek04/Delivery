import 'package:flutter/material.dart';

class HomeSummaryStats extends StatelessWidget {
  final int assignedOrderCount;
  final int completedOrderCount;
  final double totalEarnings;

  const HomeSummaryStats({
    super.key,
    required this.assignedOrderCount,
    required this.completedOrderCount,
    required this.totalEarnings,
  });

  Widget _stat(BuildContext context, String label, String value, IconData icon, Color color) {
    return Expanded(
      child: Card(
        elevation: 1,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 8),
          child: Column(
            children: [
              Icon(icon, color: color, size: 28),
              const SizedBox(height: 8),
              Text(value, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
              const SizedBox(height: 4),
              Text(label, style: Theme.of(context).textTheme.bodySmall, textAlign: TextAlign.center),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        _stat(context, 'งานที่ได้รับ', '$assignedOrderCount', Icons.assignment, Colors.blue),
        const SizedBox(width: 8),
        _stat(context, 'ส่งสำเร็จ', '$completedOrderCount', Icons.check_circle, Colors.green),
        const SizedBox(width: 8),
        _stat(context, 'รายได้', '฿${totalEarnings.toStringAsFixed(0)}', Icons.account_balance_wallet, Colors.orange),
      ],
    );
  }
}
