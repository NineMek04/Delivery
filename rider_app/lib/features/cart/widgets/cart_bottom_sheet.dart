import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:intl/intl.dart';
import 'package:go_router/go_router.dart';
import '../providers/cart_provider.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../models/shop.dart';
import '../../../shared/widgets/loading_overlay.dart';

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

  @override
  void initState() {
    super.initState();
    _loadRouteDetails();
  }

  Future<void> _loadRouteDetails() async {
    final cart = ref.read(cartProvider);
    if (cart.items.isEmpty) return;
    try {
      // 1. Get Customer GPS location
      try {
        final position = await Geolocator.getCurrentPosition(
          locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
        );
        _dropoffLat = position.latitude;
        _dropoffLng = position.longitude;
      } catch (_) {
        // Fallback to default Udon Thani center
      }

      // 2. Fetch all unique shops
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
        totalDeliveryFee += 30.0 + (dist * 10.0); // Base fee 30 THB + 10 THB/km
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

  Future<void> _placeOrder() async {
    final cart = ref.read(cartProvider);
    if (cart.items.isEmpty) return;

    try {
      await ref.read(cartProvider.notifier).checkout(
        dropoffLat: _dropoffLat,
        dropoffLng: _dropoffLng,
      );

      if (mounted) {
        Navigator.pop(context); // Close sheet
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('ส่งคำสั่งซื้อของคุณสำเร็จ! รอร้านค้าและไรเดอร์ดำเนินการ'),
            backgroundColor: Colors.green,
          ),
        );
        // Navigate to Customer Orders
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

    // Calculate subtotal
    final subtotal = cart.items.values.fold<double>(
      0.0,
      (sum, item) => sum + ((item.dish.price + item.optionsPrice) * item.quantity),
    );

    final grandTotal = subtotal + _deliveryFee;

    final formatCurrency = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0);

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
              // Pull Bar
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

              // Item List
              ConstrainedBox(
                constraints: BoxConstraints(
                  maxHeight: MediaQuery.of(context).size.height * 0.35,
                ),
                child: ListView.builder(
                  shrinkWrap: true,
                  itemCount: cart.items.length,
                  itemBuilder: (context, index) {
                    final item = cart.items.values.toList()[index];
                    return Padding(
                      padding: const EdgeInsets.only(bottom: 16),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          // Dish image or fallback icon
                          Container(
                            width: 60,
                            height: 60,
                            decoration: BoxDecoration(
                              color: Colors.grey[100],
                              borderRadius: BorderRadius.circular(12),
                            ),
                            clipBehavior: Clip.antiAlias,
                            child: _buildDishImage(item.dish.imageUrl),
                          ),
                          const SizedBox(width: 12),
                          // Name & Price & Options
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  item.dish.name,
                                  style: const TextStyle(
                                    fontSize: 15,
                                    fontWeight: FontWeight.bold,
                                  ),
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                ),
                                if (item.optionsDescription != null && item.optionsDescription!.isNotEmpty) ...[
                                  const SizedBox(height: 4),
                                  Text(
                                    item.optionsDescription!,
                                    style: TextStyle(
                                      fontSize: 12,
                                      color: isDark ? Colors.blue[300] : Colors.blue[800],
                                      fontStyle: FontStyle.italic,
                                    ),
                                  ),
                                ],
                                const SizedBox(height: 4),
                                Text(
                                  formatCurrency.format(item.dish.price + item.optionsPrice),
                                  style: const TextStyle(
                                    fontSize: 13,
                                    color: Colors.grey,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          const SizedBox(width: 12),
                          // Quantity Selector
                          Row(
                            children: [
                              _buildCircleButton(
                                icon: Icons.remove,
                                onTap: () => ref
                                    .read(cartProvider.notifier)
                                    .updateQuantity(cart.items.keys.toList()[index], item.quantity - 1),
                              ),
                              Padding(
                                padding: const EdgeInsets.symmetric(horizontal: 10),
                                child: Text(
                                  '${item.quantity}',
                                  style: const TextStyle(
                                    fontSize: 15,
                                    fontWeight: FontWeight.bold,
                                  ),
                                ),
                              ),
                              _buildCircleButton(
                                icon: Icons.add,
                                onTap: () => ref
                                    .read(cartProvider.notifier)
                                    .updateQuantity(cart.items.keys.toList()[index], item.quantity + 1),
                              ),
                            ],
                          ),
                        ],
                      ),
                    );
                  },
                ),
              ),

              const Divider(height: 24),

              // Address details
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: isDark ? Colors.grey[900] : Colors.grey[50],
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Row(
                  children: [
                    const Icon(Icons.location_on, color: Colors.red, size: 20),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Text(
                            'ส่งที่พิกัดจัดส่งของคุณ (Dropoff)',
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            'พิกัด: ${_dropoffLat.toStringAsFixed(4)}, ${_dropoffLng.toStringAsFixed(4)}',
                            style: const TextStyle(
                              fontSize: 11,
                              color: Colors.grey,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),

              const SizedBox(height: 16),

              // Pricing summary
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
                  _calculatingRoute
                      ? const SizedBox(
                          width: 12,
                          height: 12,
                          child: CircularProgressIndicator(strokeWidth: 1.5),
                        )
                      : Text(
                          '${formatCurrency.format(_deliveryFee)} (${_distance.toStringAsFixed(1)} กม.)',
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

              // Checkout Button
              ElevatedButton(
                onPressed: _calculatingRoute ? null : _placeOrder,
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
          ),
        ),
        if (cart.isLoading) const LoadingOverlay(message: 'กำลังส่งคำสั่งซื้อ...'),
      ],
    );
  }

  Widget _buildCircleButton({required IconData icon, required VoidCallback onTap}) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(14),
      child: Container(
        width: 28,
        height: 28,
        decoration: BoxDecoration(
          border: Border.all(color: Colors.grey.shade300),
          shape: BoxShape.circle,
          color: isDark ? Colors.grey[850] : Colors.white,
        ),
        child: Icon(icon, size: 16, color: isDark ? Colors.white : Colors.black87),
      ),
    );
  }

  Widget _buildDishImage(String? url) {
    if (url == null || url.isEmpty) {
      return const Center(
        child: Icon(Icons.fastfood, size: 28, color: Colors.grey),
      );
    }
    if (url.startsWith('data:image')) {
      try {
        final base64Part = url.split(',').last;
        return Image.memory(
          base64Decode(base64Part),
          fit: BoxFit.cover,
          errorBuilder: (_, __, ___) => const Center(
            child: Icon(Icons.broken_image, size: 28, color: Colors.grey),
          ),
        );
      } catch (_) {
        return const Center(
          child: Icon(Icons.broken_image, size: 28, color: Colors.grey),
        );
      }
    }
    return Image.network(
      url,
      fit: BoxFit.cover,
      errorBuilder: (_, __, ___) => const Center(
        child: Icon(Icons.fastfood, size: 28, color: Colors.grey),
      ),
    );
  }
}
