import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import '../providers/cart_provider.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../models/shop.dart';
import '../../../shared/widgets/loading_overlay.dart';
import 'package:latlong2/latlong.dart';
import 'location_pinpoint_sheet.dart';
import 'cart_item_tile.dart';
import 'cart_delivery_address_section.dart';
import 'cart_pricing_summary.dart';

class CartBottomSheet extends ConsumerStatefulWidget {
  const CartBottomSheet({super.key});

  @override
  ConsumerState<CartBottomSheet> createState() => _CartBottomSheetState();
}

class _CartBottomSheetState extends ConsumerState<CartBottomSheet> {
  List<ShopDto> _shops = [];
  double _distance = 0.0;
  double _deliveryFee = 30.0;
  bool _calculatingRoute = true;
  double _dropoffLat = 17.4138;
  double _dropoffLng = 102.7872;

  final _noteToShopController = TextEditingController();
  final _noteToRiderController = TextEditingController();
  final _deliveryAddressController = TextEditingController();

  @override
  void dispose() {
    _noteToShopController.dispose();
    _noteToRiderController.dispose();
    _deliveryAddressController.dispose();
    super.dispose();
  }

  @override
  void initState() {
    super.initState();
    _loadRouteDetails();
  }

  Future<void> _loadRouteDetails() async {
    final cart = ref.read(cartProvider);
    if (cart.items.isEmpty) return;
    try {
      try {
        final position = await Geolocator.getCurrentPosition(
          locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
        );
        _dropoffLat = position.latitude;
        _dropoffLng = position.longitude;
      } catch (_) {}

      final uniqueShopIds = cart.items.values.map((item) => item.dish.shopId).toSet().toList();
      final shopService = ref.read(shopApiServiceProvider);

      double totalDistance = 0.0;
      double totalDeliveryFee = 0.0;
      final loadedShops = <ShopDto>[];

      for (final shopId in uniqueShopIds) {
        final shop = await shopService.getById(shopId);
        loadedShops.add(shop);
        final shopLat = shop.lat ?? 17.4138;
        final shopLng = shop.lng ?? 102.7872;
        final dist = Geolocator.distanceBetween(shopLat, shopLng, _dropoffLat, _dropoffLng) / 1000.0;
        totalDistance += dist;
        totalDeliveryFee += 30.0 + (dist * 10.0);
      }

      if (mounted) {
        setState(() {
          _shops = loadedShops;
          _distance = totalDistance;
          _deliveryFee = totalDeliveryFee;
          _calculatingRoute = false;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _calculatingRoute = false;
        });
      }
    }
  }

  void _recalculateDeliveryFee() {
    double totalDistance = 0.0;
    double totalDeliveryFee = 0.0;
    for (final shop in _shops) {
      final shopLat = shop.lat ?? 17.4138;
      final shopLng = shop.lng ?? 102.7872;
      final dist = Geolocator.distanceBetween(shopLat, shopLng, _dropoffLat, _dropoffLng) / 1000.0;
      totalDistance += dist;
      totalDeliveryFee += 30.0 + (dist * 10.0);
    }
    setState(() {
      _distance = totalDistance;
      _deliveryFee = totalDeliveryFee;
    });
  }

  Future<void> _pickLocationOnMap() async {
    final selected = await LocationPinpointSheet.show(
      context,
      initialLocation: LatLng(_dropoffLat, _dropoffLng),
    );
    if (selected != null && mounted) {
      setState(() {
        _dropoffLat = selected.latitude;
        _dropoffLng = selected.longitude;
      });
      _recalculateDeliveryFee();
    }
  }

  bool _validateOrderBeforeSubmit() {
    final cart = ref.read(cartProvider);
    if (cart.items.isEmpty) {
      _showValidationError('ไม่มีสินค้าในตะกร้า');
      return false;
    }

    if (_dropoffLat == 0.0 && _dropoffLng == 0.0) {
      _showValidationError('ไม่พบพิกัดจัดส่งที่ถูกต้อง กรุณาเปิดบริการระบุตำแหน่ง');
      return false;
    }

    for (final shop in _shops) {
      if (!shop.isOpen) {
        _showValidationError('ร้าน "${shop.name}" ปิดให้บริการชั่วคราวในขณะนี้ ไม่สามารถสั่งซื้อได้');
        return false;
      }
    }

    for (final shop in _shops) {
      final shopLat = shop.lat ?? 17.4138;
      final shopLng = shop.lng ?? 102.7872;
      final dist = Geolocator.distanceBetween(shopLat, shopLng, _dropoffLat, _dropoffLng) / 1000.0;
      if (dist > 25.0) {
        _showValidationError('ร้าน "${shop.name}" อยู่ไกลเกินไป (ระยะทาง ${dist.toStringAsFixed(1)} กม. เกินระยะสูงสุด 25 กม.)');
        return false;
      }
    }

    return true;
  }

  void _showValidationError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: Colors.orange[800],
        duration: const Duration(seconds: 4),
      ),
    );
  }

  Future<void> _placeOrder() async {
    if (!_validateOrderBeforeSubmit()) return;

    try {
      await ref.read(cartProvider.notifier).checkout(
        dropoffLat: _dropoffLat,
        dropoffLng: _dropoffLng,
        noteToShop: _noteToShopController.text.trim().isEmpty ? null : _noteToShopController.text.trim(),
        noteToRider: _noteToRiderController.text.trim().isEmpty ? null : _noteToRiderController.text.trim(),
        deliveryAddress: _deliveryAddressController.text.trim().isEmpty ? null : _deliveryAddressController.text.trim(),
      );

      if (mounted) {
        Navigator.pop(context);
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('ส่งคำสั่งซื้อของคุณสำเร็จ! รอร้านค้าและไรเดอร์ดำเนินการ'),
            backgroundColor: Colors.green,
          ),
        );
        context.go('/customer/orders');
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('สั่งซื้อล้มเหลว: $e'),
            backgroundColor: Colors.red,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final cart = ref.watch(cartProvider);
    final isDark = Theme.of(context).brightness == Brightness.dark;

    final subtotal = cart.items.values.fold<double>(
      0.0,
      (sum, item) => sum + ((item.dish.price + item.optionsPrice) * item.quantity),
    );

    return Stack(
      children: [
        Container(
          decoration: BoxDecoration(
            color: isDark ? const Color(0xFF1E1E2E) : Colors.white,
            borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
          ),
          padding: EdgeInsets.only(
            bottom: MediaQuery.of(context).viewInsets.bottom + 24,
            left: 20,
            right: 20,
            top: 16,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Center(
                child: Container(
                  width: 40,
                  height: 4,
                  decoration: BoxDecoration(
                    color: Colors.grey[300],
                    borderRadius: BorderRadius.circular(2),
                  ),
                ),
              ),
              const SizedBox(height: 16),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    'ตะกร้าสินค้า',
                    style: TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.bold,
                      color: isDark ? Colors.white : Colors.black87,
                    ),
                  ),
                  TextButton.icon(
                    onPressed: () {
                      ref.read(cartProvider.notifier).clearCart();
                      Navigator.pop(context);
                    },
                    icon: const Icon(Icons.delete_outline, size: 18),
                    label: const Text('ล้างตะกร้า'),
                    style: TextButton.styleFrom(foregroundColor: Colors.red),
                  )
                ],
              ),
              if (_shops.isNotEmpty) ...[
                const SizedBox(height: 4),
                Wrap(
                  spacing: 8,
                  runSpacing: 4,
                  children: _shops.map((s) => Chip(
                    avatar: Icon(Icons.store, size: 14, color: Theme.of(context).primaryColor),
                    label: Text(s.name, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.bold)),
                    backgroundColor: isDark ? Colors.grey[800] : Theme.of(context).primaryColor.withOpacity(0.08),
                    side: BorderSide.none,
                    padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 0),
                  )).toList(),
                ),
              ],
              const Divider(height: 24),

              ConstrainedBox(
                constraints: BoxConstraints(
                  maxHeight: MediaQuery.of(context).size.height * 0.35,
                ),
                child: ListView.builder(
                  shrinkWrap: true,
                  itemCount: cart.items.length,
                  itemBuilder: (context, index) {
                    final key = cart.items.keys.toList()[index];
                    final item = cart.items.values.toList()[index];
                    return CartItemTile(
                      item: item,
                      onIncrement: () => ref.read(cartProvider.notifier).updateQuantity(key, item.quantity + 1),
                      onDecrement: () => ref.read(cartProvider.notifier).updateQuantity(key, item.quantity - 1),
                    );
                  },
                ),
              ),

              const Divider(height: 24),

              CartDeliveryAddressSection(
                dropoffLat: _dropoffLat,
                dropoffLng: _dropoffLng,
                onPickLocation: _pickLocationOnMap,
                deliveryAddressController: _deliveryAddressController,
                noteToShopController: _noteToShopController,
                noteToRiderController: _noteToRiderController,
              ),

              const SizedBox(height: 16),

              CartPricingSummary(
                subtotal: subtotal,
                deliveryFee: _deliveryFee,
                distance: _distance,
                calculatingRoute: _calculatingRoute,
                onCheckout: _placeOrder,
              ),
            ],
          ),
        ),
        if (cart.isLoading) const LoadingOverlay(message: 'กำลังส่งคำสั่งซื้อ...'),
      ],
    );
  }
}
