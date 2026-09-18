import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../app/app_theme.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';

class EditShopFormSheet extends ConsumerStatefulWidget {
  final ShopDto shop;
  const EditShopFormSheet({super.key, required this.shop});

  @override
  ConsumerState<EditShopFormSheet> createState() => _EditShopFormSheetState();
}

class _EditShopFormSheetState extends ConsumerState<EditShopFormSheet> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _menuNameController;
  late final TextEditingController _menuPriceController;
  late final TextEditingController _prepTimeController;
  late final TextEditingController _openingHoursController;

  bool _isSaving = false;
  bool _showMap = false;
  LatLng? _selectedLocation;
  late final MapController _mapController;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.shop.name);
    _menuNameController = TextEditingController(text: widget.shop.menuName);
    _menuPriceController = TextEditingController(text: widget.shop.menuPrice.toStringAsFixed(0));
    _prepTimeController = TextEditingController(text: widget.shop.prepTimeMinutes.toString());
    _openingHoursController = TextEditingController(text: widget.shop.openingHours ?? '');

    if (widget.shop.lat != null && widget.shop.lng != null) {
      _selectedLocation = LatLng(widget.shop.lat!, widget.shop.lng!);
    } else {
      _selectedLocation = const LatLng(17.4138, 102.7872);
    }
    _mapController = MapController();
  }

  @override
  void dispose() {
    _nameController.dispose();
    _menuNameController.dispose();
    _menuPriceController.dispose();
    _prepTimeController.dispose();
    _openingHoursController.dispose();
    _mapController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final bottomInset = MediaQuery.of(context).viewInsets.bottom;

    return Padding(
      padding: EdgeInsets.only(bottom: bottomInset),
      child: DraggableScrollableSheet(
        initialChildSize: 0.85,
        maxChildSize: 0.95,
        minChildSize: 0.5,
        expand: false,
        builder: (context, scrollController) {
          return SingleChildScrollView(
            controller: scrollController,
            padding: const EdgeInsets.all(24),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Center(
                    child: Container(
                      width: 40,
                      height: 4,
                      margin: const EdgeInsets.only(bottom: 16),
                      decoration: BoxDecoration(
                        color: AppTheme.textMuted,
                        borderRadius: BorderRadius.circular(2),
                      ),
                    ),
                  ),
                  Text(
                    'แก้ไขข้อมูลร้านค้า',
                    style: Theme.of(context).textTheme.headlineMedium,
                  ),
                  const SizedBox(height: 24),

                  // 1. Shop Name
                  TextFormField(
                    controller: _nameController,
                    decoration: const InputDecoration(
                      labelText: 'ชื่อร้านค้า *',
                      prefixIcon: Icon(Icons.store),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกชื่อร้านค้า';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 2. Menu Name
                  TextFormField(
                    controller: _menuNameController,
                    decoration: const InputDecoration(
                      labelText: 'ชื่อเมนูหลัก *',
                      prefixIcon: Icon(Icons.restaurant),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกชื่อเมนูหลัก';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 3. Menu Price
                  TextFormField(
                    controller: _menuPriceController,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'ราคาเริ่มต้น (บาท) *',
                      prefixIcon: Icon(Icons.attach_money),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกราคาเริ่มต้น';
                      final price = double.tryParse(v);
                      if (price == null || price <= 0) return 'ราคาต้องมากกว่า 0';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 4. Prep Time
                  TextFormField(
                    controller: _prepTimeController,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'เวลาเตรียมอาหารเฉลี่ย (นาที) *',
                      prefixIcon: Icon(Icons.timer),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกเวลาเตรียมอาหาร';
                      final minutes = int.tryParse(v);
                      if (minutes == null || minutes <= 0) return 'เวลาต้องมากกว่า 0';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 5. Opening Hours
                  TextFormField(
                    controller: _openingHoursController,
                    decoration: const InputDecoration(
                      labelText: 'เวลาเปิด-ปิด (เช่น 08:00 - 20:00) *',
                      prefixIcon: Icon(Icons.schedule),
                    ),
                    validator: (v) {
                      if (v == null || v.trim().isEmpty) return 'กรุณากรอกเวลาเปิด-ปิด';
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // 6. Map pin switch
                  SwitchListTile.adaptive(
                    title: const Text('ตั้งค่าพิกัดร้านค้าบนแผนที่'),
                    subtitle: _selectedLocation != null
                        ? Text(
                            'พิกัด: ${_selectedLocation!.latitude.toStringAsFixed(5)}, ${_selectedLocation!.longitude.toStringAsFixed(5)}')
                        : const Text('ยังไม่ได้ปักหมุดพิกัดร้านค้า'),
                    value: _showMap,
                    activeColor: AppTheme.accentColor,
                    onChanged: (val) {
                      setState(() {
                        _showMap = val;
                      });
                    },
                  ),

                  // Map Container
                  if (_showMap) ...[
                    const SizedBox(height: 8),
                    Container(
                      height: 250,
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(16),
                        border: Border.all(color: AppTheme.textMuted.withValues(alpha: 0.3)),
                      ),
                      clipBehavior: Clip.antiAlias,
                      child: FlutterMap(
                        mapController: _mapController,
                        options: MapOptions(
                          initialCenter: _selectedLocation ?? const LatLng(17.4138, 102.7872),
                          initialZoom: 15,
                          onTap: (tapPosition, point) {
                            setState(() {
                              _selectedLocation = point;
                            });
                          },
                        ),
                        children: [
                          TileLayer(
                            urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                            userAgentPackageName: 'com.delivery.rider_app',
                          ),
                          if (_selectedLocation != null)
                            MarkerLayer(
                              markers: [
                                Marker(
                                  point: _selectedLocation!,
                                  width: 40,
                                  height: 40,
                                  child: const Icon(
                                    Icons.location_on,
                                    color: AppTheme.errorColor,
                                    size: 40,
                                  ),
                                ),
                              ],
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 8),
                    const Center(
                      child: Text(
                        'แตะบนแผนที่เพื่อปักหมุดพิกัดเริ่มต้นของร้านค้า',
                        style: TextStyle(fontSize: 12, color: AppTheme.textMuted),
                      ),
                    ),
                  ],

                  const SizedBox(height: 24),

                  // Save button
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _isSaving ? null : _submit,
                      child: _isSaving
                          ? const SizedBox(
                              height: 20,
                              width: 20,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Text('บันทึกข้อมูลร้าน'),
                    ),
                  ),
                  const SizedBox(height: 16),
                ],
              ),
            ),
          );
        },
      ),
    );
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() => _isSaving = true);

    try {
      final data = widget.shop.toJson();
      data['Name'] = _nameController.text.trim();
      data['MenuName'] = _menuNameController.text.trim();
      data['MenuPrice'] = double.parse(_menuPriceController.text.trim());
      data['PrepTimeMinutes'] = int.parse(_prepTimeController.text.trim());
      data['OpeningHours'] = _openingHoursController.text.trim();

      if (_selectedLocation != null) {
        data['Lat'] = _selectedLocation!.latitude;
        data['Lng'] = _selectedLocation!.longitude;
      }

      final shopApi = ref.read(shopApiServiceProvider);
      await shopApi.update(widget.shop.id, data);
      ref.invalidate(currentShopProvider);

      if (mounted) {
        Navigator.pop(context);
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('บันทึกข้อมูลร้านค้าเรียบร้อยแล้ว')),
        );
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
}
