import 'package:flutter/material.dart';

class CartDeliveryAddressSection extends StatelessWidget {
  final double dropoffLat;
  final double dropoffLng;
  final VoidCallback onPickLocation;
  final TextEditingController deliveryAddressController;
  final TextEditingController noteToShopController;
  final TextEditingController noteToRiderController;

  const CartDeliveryAddressSection({
    super.key,
    required this.dropoffLat,
    required this.dropoffLng,
    required this.onPickLocation,
    required this.deliveryAddressController,
    required this.noteToShopController,
    required this.noteToRiderController,
  });

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: isDark ? Colors.grey[900] : Colors.grey[50],
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.grey.withOpacity(0.2)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.location_on, color: Colors.redAccent, size: 22),
              const SizedBox(width: 8),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text(
                      'จุดส่งสินค้า (Dropoff)',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    Text(
                      'พิกัด: ${dropoffLat.toStringAsFixed(5)}, ${dropoffLng.toStringAsFixed(5)}',
                      style: const TextStyle(
                        fontSize: 11,
                        color: Colors.grey,
                      ),
                    ),
                  ],
                ),
              ),
              OutlinedButton.icon(
                onPressed: onPickLocation,
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                  visualDensity: VisualDensity.compact,
                  side: const BorderSide(color: Color(0xFF6366F1)),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                ),
                icon: const Icon(Icons.pin_drop, size: 14, color: Color(0xFF6366F1)),
                label: const Text(
                  'ปักหมุด',
                  style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold, color: Color(0xFF6366F1)),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          TextField(
            controller: deliveryAddressController,
            style: const TextStyle(fontSize: 12),
            decoration: InputDecoration(
              isDense: true,
              hintText: 'รายละเอียดที่อยู่ (เช่น คอนโด A ชั้น 3 ห้อง 305)',
              hintStyle: const TextStyle(fontSize: 12, color: Colors.black38),
              prefixIcon: const Icon(Icons.home_outlined, size: 16, color: Colors.grey),
              contentPadding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: Colors.grey.shade300)),
              enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: Colors.grey.shade300)),
            ),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: noteToShopController,
            style: const TextStyle(fontSize: 12),
            decoration: InputDecoration(
              isDense: true,
              hintText: 'หมายเหตุถึงร้านค้า (เช่น ขอช้อนส้อม, เผ็ดน้อย)',
              hintStyle: const TextStyle(fontSize: 12, color: Colors.black38),
              prefixIcon: const Icon(Icons.restaurant, size: 16, color: Colors.orange),
              contentPadding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: Colors.grey.shade300)),
              enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: Colors.grey.shade300)),
            ),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: noteToRiderController,
            style: const TextStyle(fontSize: 12),
            decoration: InputDecoration(
              isDense: true,
              hintText: 'ข้อความถึงไรเดอร์ (เช่น วางไว้หน้าบ้าน, โทรหาก่อนถึง)',
              hintStyle: const TextStyle(fontSize: 12, color: Colors.black38),
              prefixIcon: const Icon(Icons.delivery_dining, size: 16, color: Colors.blue),
              contentPadding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: Colors.grey.shade300)),
              enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: Colors.grey.shade300)),
            ),
          ),
        ],
      ),
    );
  }
}
