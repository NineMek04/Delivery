import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:latlong2/latlong.dart';

import '../../../app/app_theme.dart';
import '../../../core/api/services/customer_address_api_service.dart';
import 'customer_addresses_screen.dart';

class CustomerAddressMapScreen extends ConsumerStatefulWidget {
  const CustomerAddressMapScreen({super.key});

  @override
  ConsumerState<CustomerAddressMapScreen> createState() => _CustomerAddressMapScreenState();
}

class _CustomerAddressMapScreenState extends ConsumerState<CustomerAddressMapScreen> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _addressLine1Controller = TextEditingController();
  final _addressLine2Controller = TextEditingController();
  final _cityController = TextEditingController(text: 'อุดรธานี');
  final _stateController = TextEditingController(text: 'เมืองอุดรธานี');
  final _postalCodeController = TextEditingController(text: '41000');

  LatLng _pinnedLocation = const LatLng(17.4138, 102.7872); // Default to Udon Thani center
  final MapController _mapController = MapController();
  bool _isSaving = false;
  bool _isDefault = false;

  @override
  void dispose() {
    _nameController.dispose();
    _addressLine1Controller.dispose();
    _addressLine2Controller.dispose();
    _cityController.dispose();
    _stateController.dispose();
    _postalCodeController.dispose();
    _mapController.dispose();
    super.dispose();
  }

  Future<void> _submitAddress() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() => _isSaving = true);
    try {
      final service = ref.read(customerAddressApiServiceProvider);
      await service.createAddress(
        name: _nameController.text.trim(),
        addressLine1: _addressLine1Controller.text.trim(),
        addressLine2: _addressLine2Controller.text.trim().isEmpty ? null : _addressLine2Controller.text.trim(),
        city: _cityController.text.trim(),
        state: _stateController.text.trim(),
        postalCode: _postalCodeController.text.trim(),
        latitude: _pinnedLocation.latitude,
        longitude: _pinnedLocation.longitude,
        isDefault: _isDefault,
      );

      ref.invalidate(customerAddressesProvider);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('บันทึกที่อยู่จัดส่งเรียบร้อยแล้ว')),
        );
        Navigator.pop(context);
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('เกิดข้อผิดพลาด: $e'), backgroundColor: AppTheme.errorColor),
        );
      }
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('ปักหมุดที่อยู่จัดส่ง'),
      ),
      body: Stack(
        children: [
          // ── Map Container ──────────────────────────────────────────
          Column(
            children: [
              Expanded(
                flex: 4,
                child: Stack(
                  children: [
                    FlutterMap(
                      mapController: _mapController,
                      options: MapOptions(
                        initialCenter: _pinnedLocation,
                        initialZoom: 15,
                        onTap: (tapPosition, point) {
                          setState(() {
                            _pinnedLocation = point;
                          });
                        },
                      ),
                      children: [
                        TileLayer(
                          urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                          userAgentPackageName: 'com.delivery.rider_app',
                        ),
                        MarkerLayer(
                          markers: [
                            Marker(
                              point: _pinnedLocation,
                              width: 50,
                              height: 50,
                              child: const Icon(
                                Icons.location_on,
                                color: AppTheme.errorColor,
                                size: 50,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                    Positioned(
                      top: 16,
                      left: 16,
                      right: 16,
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                        decoration: BoxDecoration(
                          color: AppTheme.surfaceCard,
                          borderRadius: BorderRadius.circular(30),
                          boxShadow: const [
                            BoxShadow(color: Colors.black26, blurRadius: 6, offset: Offset(0, 2)),
                          ],
                        ),
                        child: const Row(
                          children: [
                            Icon(Icons.info_outline, color: AppTheme.primaryColor, size: 20),
                            SizedBox(width: 8),
                            Expanded(
                              child: Text(
                                'แตะบนแผนที่เพื่อขยับหมุดไปยังที่อยู่ของคุณ',
                                style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              
              // ── Address Details Form ────────────────────────────────
              Expanded(
                flex: 5,
                child: Container(
                  padding: const EdgeInsets.all(20),
                  decoration: BoxDecoration(
                    color: AppTheme.surfaceCard,
                    borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
                    boxShadow: const [
                      BoxShadow(color: Colors.black12, blurRadius: 10, offset: Offset(0, -2)),
                    ],
                  ),
                  child: Form(
                    key: _formKey,
                    child: SingleChildScrollView(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Text(
                            'รายละเอียดที่อยู่',
                            style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                          ),
                          const SizedBox(height: 16),
                          
                          // Name of address (Home, Work, etc)
                          TextFormField(
                            controller: _nameController,
                            decoration: const InputDecoration(
                              labelText: 'ชื่อเรียกที่อยู่ (เช่น บ้าน, ที่ทำงาน) *',
                              prefixIcon: Icon(Icons.bookmark_outline),
                            ),
                            validator: (v) {
                              if (v == null || v.trim().isEmpty) return 'กรุณากรอกชื่อเรียกที่อยู่';
                              return null;
                            },
                          ),
                          const SizedBox(height: 12),
                          
                          // Address Line 1
                          TextFormField(
                            controller: _addressLine1Controller,
                            decoration: const InputDecoration(
                              labelText: 'บ้านเลขที่, ถนน, ซอย *',
                              prefixIcon: Icon(Icons.home_outlined),
                            ),
                            validator: (v) {
                              if (v == null || v.trim().isEmpty) return 'กรุณากรอกที่อยู่จัดส่ง';
                              return null;
                            },
                          ),
                          const SizedBox(height: 12),
                          
                          // Address Line 2 (Optional)
                          TextFormField(
                            controller: _addressLine2Controller,
                            decoration: const InputDecoration(
                              labelText: 'รายละเอียดเพิ่มเติม (อาคาร, ชั้น, ห้อง) (ถ้ามี)',
                              prefixIcon: Icon(Icons.apartment_outlined),
                            ),
                          ),
                          const SizedBox(height: 12),

                          // City, State, PostalCode Row
                          Row(
                            children: [
                              Expanded(
                                child: TextFormField(
                                  controller: _stateController,
                                  decoration: const InputDecoration(
                                    labelText: 'อำเภอ/เขต *',
                                  ),
                                  validator: (v) {
                                    if (v == null || v.trim().isEmpty) return 'กรุณากรอกอำเภอ/เขต';
                                    return null;
                                  },
                                ),
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: TextFormField(
                                  controller: _cityController,
                                  decoration: const InputDecoration(
                                    labelText: 'จังหวัด *',
                                  ),
                                  validator: (v) {
                                    if (v == null || v.trim().isEmpty) return 'กรุณากรอกจังหวัด';
                                    return null;
                                  },
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 12),
                          
                          TextFormField(
                            controller: _postalCodeController,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'รหัสไปรษณีย์ *',
                              prefixIcon: Icon(Icons.mail_outline),
                            ),
                            validator: (v) {
                              if (v == null || v.trim().isEmpty) return 'กรุณากรอกรหัสไปรษณีย์';
                              return null;
                            },
                          ),
                          const SizedBox(height: 16),
                          
                          // IsDefault Switch
                          SwitchListTile.adaptive(
                            title: const Text('ตั้งเป็นที่อยู่เริ่มต้นของบัญชีนี้'),
                            subtitle: const Text('ใช้ที่อยู่นี้เป็นที่จัดส่งหลักในการสั่งซื้อครั้งถัดไป'),
                            value: _isDefault,
                            activeColor: AppTheme.primaryColor,
                            onChanged: (val) {
                              setState(() {
                                _isDefault = val;
                              });
                            },
                          ),
                          const SizedBox(height: 24),
                          
                          // Submit Button
                          SizedBox(
                            width: double.infinity,
                            child: ElevatedButton(
                              onPressed: _isSaving ? null : _submitAddress,
                              style: ElevatedButton.styleFrom(
                                padding: const EdgeInsets.symmetric(vertical: 14),
                              ),
                              child: _isSaving
                                  ? const SizedBox(
                                      height: 20,
                                      width: 20,
                                      child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                                    )
                                  : const Text('บันทึกที่อยู่จัดส่ง', style: TextStyle(fontSize: 16)),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
          if (_isSaving)
            Container(
              color: Colors.black26,
              child: const Center(child: CircularProgressIndicator()),
            ),
        ],
      ),
    );
  }
}
