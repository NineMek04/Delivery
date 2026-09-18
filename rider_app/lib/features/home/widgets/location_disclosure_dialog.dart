import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

class LocationDisclosureDialog {
  LocationDisclosureDialog._();

  static Future<bool> show(BuildContext context) async {
    final permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.always || permission == LocationPermission.whileInUse) {
      return true;
    }

    if (!context.mounted) return false;

    final accepted = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return AlertDialog(
          title: const Row(
            children: [
              Icon(Icons.location_on, color: Colors.blueAccent),
              SizedBox(width: 8),
              Text('คำชี้แจงการเข้าถึงตำแหน่ง'),
            ],
          ),
          content: const SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'แอปพลิเคชัน Rider นี้จำเป็นต้องเข้าถึงข้อมูลตำแหน่งพิกัดของคุณ (GPS Location) แม้ในขณะที่ปิดแอปพลิเคชันหรือไม่ได้ใช้งาน (Background Location)',
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
                SizedBox(height: 12),
                Text('ข้อมูลตำแหน่งจะถูกนำไปใช้เพื่อ:'),
                Text('• ตรวจสอบพิกัดปัจจุบันสำหรับการแจกจ่ายงานส่งอาหารของระบบ AI Dispatcher'),
                Text('• คำนวณเส้นทางและเวลาจัดส่ง (ETA) ให้กับร้านค้าและลูกค้า'),
                Text('• ติดตามการเดินทางเพื่อความปลอดภัยในระหว่างการส่งสินค้า'),
                SizedBox(height: 12),
                Text('หากคุณไม่อนุญาต คุณจะไม่สามารถออนไลน์เพื่อรับงานผ่านระบบได้'),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(false),
              child: const Text('ปฏิเสธ (Deny)', style: TextStyle(color: Colors.red)),
            ),
            ElevatedButton(
              onPressed: () => Navigator.of(context).pop(true),
              child: const Text('ตกลง (Accept)'),
            ),
          ],
        );
      },
    );

    return accepted ?? false;
  }
}
