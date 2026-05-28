import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../app/app_theme.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../models/shop.dart';
import '../providers/store_providers.dart';

/// Store Profile Screen — Page 3: Shop info, online/offline toggle, logout.
class StoreProfileScreen extends ConsumerStatefulWidget {
  const StoreProfileScreen({super.key});

  @override
  ConsumerState<StoreProfileScreen> createState() => _StoreProfileScreenState();
}

class _StoreProfileScreenState extends ConsumerState<StoreProfileScreen> {
  bool _isSaving = false;

  @override
  Widget build(BuildContext context) {
    final shopAsync = ref.watch(currentShopProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('โปรไฟล์ร้านค้า')),
      body: shopAsync.when(
        data: (shop) {
          if (shop == null) {
            return const Center(child: Text('ไม่พบข้อมูลร้านค้า'));
          }
          return _buildProfileBody(context, shop);
        },
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(Icons.error_outline, size: 48, color: AppTheme.errorColor),
              const SizedBox(height: 16),
              Text('เกิดข้อผิดพลาด: $e'),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildProfileBody(BuildContext context, ShopDto shop) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        // ── Shop Avatar & Name ──────────────────────────────────
        Center(
          child: Column(
            children: [
              Container(
                width: 100,
                height: 100,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: LinearGradient(
                    colors: [
                      AppTheme.primaryColor,
                      AppTheme.primaryColor.withValues(alpha: 0.6),
                    ],
                  ),
                ),
                child: const Center(
                  child: Icon(Icons.storefront, size: 48, color: Colors.white),
                ),
              ),
              const SizedBox(height: 16),
              Text(
                shop.name,
                style: Theme.of(context).textTheme.headlineMedium,
              ),
              const SizedBox(height: 4),
              Text(
                'รหัสร้าน: ${shop.trackingCode.isNotEmpty ? shop.trackingCode : shop.id.substring(0, 8)}',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ],
          ),
        ),
        const SizedBox(height: 32),

        // ── Online/Offline Toggle ────────────────────────────────
        _ProfileCard(
          child: SwitchListTile.adaptive(
            title: const Text('สถานะร้าน'),
            subtitle: Text(
              shop.isOpen ? 'เปิดร้าน — รับออเดอร์อยู่' : 'ปิดร้าน — หยุดรับออเดอร์',
              style: TextStyle(
                color: shop.isOpen ? AppTheme.accentColor : AppTheme.textMuted,
                fontWeight: FontWeight.w600,
              ),
            ),
            value: shop.isOpen,
            activeColor: AppTheme.accentColor,
            secondary: Icon(
              shop.isOpen ? Icons.circle : Icons.circle_outlined,
              color: shop.isOpen ? AppTheme.accentColor : AppTheme.textMuted,
            ),
            onChanged: _isSaving ? null : (v) => _toggleShopStatus(shop, v),
          ),
        ),
        const SizedBox(height: 12),

        // ── Shop Info ────────────────────────────────────────────
        _ProfileCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Padding(
                padding: EdgeInsets.fromLTRB(16, 16, 16, 8),
                child: Text('ข้อมูลร้านค้า', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
              ),
              _InfoTile(
                icon: Icons.restaurant,
                label: 'ชื่อเมนูหลัก',
                value: shop.menuName.isNotEmpty ? shop.menuName : '—',
              ),
              _InfoTile(
                icon: Icons.attach_money,
                label: 'ราคาเมนูเริ่มต้น',
                value: '฿${shop.menuPrice.toStringAsFixed(0)}',
              ),
              _InfoTile(
                icon: Icons.timer,
                label: 'เวลาเตรียมอาหาร',
                value: '${shop.prepTimeMinutes} นาที',
              ),
              _InfoTile(
                icon: Icons.schedule,
                label: 'เวลาเปิด-ปิด',
                value: shop.openingHours ?? 'ไม่ได้ตั้งค่า',
              ),
              _InfoTile(
                icon: Icons.location_on,
                label: 'พิกัด',
                value: shop.lat != null && shop.lng != null
                    ? '${shop.lat!.toStringAsFixed(4)}, ${shop.lng!.toStringAsFixed(4)}'
                    : 'ยังไม่ได้ตั้งค่า',
              ),
              _InfoTile(
                icon: Icons.calendar_today,
                label: 'วันที่สร้าง',
                value: shop.createdAt != null
                    ? '${shop.createdAt!.day}/${shop.createdAt!.month}/${shop.createdAt!.year}'
                    : '—',
              ),
              const SizedBox(height: 8),
            ],
          ),
        ),
        const SizedBox(height: 12),

        // ── Edit shop info ───────────────────────────────────────
        _ProfileCard(
          child: ListTile(
            leading: const Icon(Icons.edit, color: AppTheme.primaryColor),
            title: const Text('แก้ไขข้อมูลร้าน'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => _showEditShopDialog(context, shop),
          ),
        ),
        const SizedBox(height: 24),

        // ── Logout ───────────────────────────────────────────────
        SizedBox(
          width: double.infinity,
          child: OutlinedButton.icon(
            onPressed: () => _confirmLogout(context),
            icon: const Icon(Icons.logout, color: AppTheme.errorColor),
            label: const Text('ออกจากระบบ', style: TextStyle(color: AppTheme.errorColor)),
            style: OutlinedButton.styleFrom(
              side: const BorderSide(color: AppTheme.errorColor),
              padding: const EdgeInsets.symmetric(vertical: 14),
              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
            ),
          ),
        ),
        const SizedBox(height: 32),
      ],
    );
  }

  Future<void> _toggleShopStatus(ShopDto shop, bool isOpen) async {
    setState(() => _isSaving = true);
    try {
      final shopApi = ref.read(shopApiServiceProvider);
      await shopApi.update(shop.id, {'IsOpen': isOpen});
      ref.invalidate(currentShopProvider);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('ไม่สามารถเปลี่ยนสถานะได้: $e')),
        );
      }
    } finally {
      if (mounted) setState(() => _isSaving = false);
    }
  }

  void _showEditShopDialog(BuildContext context, ShopDto shop) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surfaceCard,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (ctx) => _EditShopFormSheet(shop: shop),
    );
  }

  Future<void> _confirmLogout(BuildContext context) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ออกจากระบบ'),
        content: const Text('ต้องการออกจากระบบหรือไม่?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ยกเลิก')),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('ออกจากระบบ', style: TextStyle(color: AppTheme.errorColor)),
          ),
        ],
      ),
    );

    if (confirmed == true && mounted) {
      await ref.read(authServiceProvider.notifier).logout();
    }
  }
}

// ═══════════════════════════════════════════════════════════════════
// Profile Card Container
// ═══════════════════════════════════════════════════════════════════
class _ProfileCard extends StatelessWidget {
  final Widget child;
  const _ProfileCard({required this.child});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.surfaceCard,
        borderRadius: BorderRadius.circular(16),
      ),
      clipBehavior: Clip.antiAlias,
      child: child,
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Info Tile
// ═══════════════════════════════════════════════════════════════════
class _InfoTile extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;

  const _InfoTile({required this.icon, required this.label, required this.value});

  @override
  Widget build(BuildContext context) {
    return ListTile(
      dense: true,
      leading: Icon(icon, size: 20, color: AppTheme.textMuted),
      title: Text(label, style: const TextStyle(fontSize: 13, color: AppTheme.textMuted)),
      trailing: Text(
        value,
        style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
      ),
    );
  }
}

// ═══════════════════════════════════════════════════════════════════
// Edit Shop Form Sheet
// ═══════════════════════════════════════════════════════════════════
class _EditShopFormSheet extends ConsumerStatefulWidget {
  final ShopDto shop;
  const _EditShopFormSheet({required this.shop});

  @override
  ConsumerState<_EditShopFormSheet> createState() => _EditShopFormSheetState();
}

class _EditShopFormSheetState extends ConsumerState<_EditShopFormSheet> {
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
                  // Handle bar
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
      final data = <String, dynamic>{
        'Name': _nameController.text.trim(),
        'MenuName': _menuNameController.text.trim(),
        'MenuPrice': double.parse(_menuPriceController.text.trim()),
        'PrepTimeMinutes': int.parse(_prepTimeController.text.trim()),
        'OpeningHours': _openingHoursController.text.trim(),
      };

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
