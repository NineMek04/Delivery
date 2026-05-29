import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import '../../../app/app_theme.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../core/api/services/menu_item_api_service.dart';
import '../../../models/shop.dart';
import '../cart/widgets/cart_action_button.dart';
import '../cart/providers/cart_provider.dart';
import 'widgets/dish_options_bottom_sheet.dart';

class ShopDetailsScreen extends ConsumerStatefulWidget {
  final String shopId;

  const ShopDetailsScreen({
    super.key,
    required this.shopId,
  });

  @override
  ConsumerState<ShopDetailsScreen> createState() => _ShopDetailsScreenState();
}

class _ShopDetailsScreenState extends ConsumerState<ShopDetailsScreen> {
  ShopDto? _shop;
  List<MenuItemDto> _dishes = [];
  bool _isLoading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadData();
  }

  Future<void> _loadData() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final shop = await ref.read(shopApiServiceProvider).getById(widget.shopId);
      final result = await ref.read(menuItemApiServiceProvider).getByShop(widget.shopId);
      
      if (mounted) {
        setState(() {
          _shop = shop;
          _dishes = result.items;
          _isLoading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = e.toString();
          _isLoading = false;
        });
      }
    }
  }

  void _onAddDish(BuildContext context, MenuItemDto dish) {
    if (dish.options != null && dish.options!.isNotEmpty) {
      showModalBottomSheet(
        context: context,
        isScrollControlled: true,
        backgroundColor: Colors.transparent,
        builder: (context) => DishOptionsBottomSheet(dish: dish),
      );
    } else {
      ref.read(cartProvider.notifier).addItem(dish);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('เพิ่ม ${dish.name} ลงในตะกร้าแล้ว'),
          duration: const Duration(seconds: 1),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final formatCurrency = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0);

    return Scaffold(
      backgroundColor: Colors.grey[50],
      body: _isLoading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text('เกิดข้อผิดพลาด: $_error', style: const TextStyle(color: Colors.red)),
                      const SizedBox(height: 16),
                      ElevatedButton(
                        onPressed: _loadData,
                        child: const Text('ลองใหม่'),
                      ),
                    ],
                  ),
                )
              : CustomScrollView(
                  slivers: [
                    // Shop Header/Banner Sliver
                    SliverAppBar(
                      expandedHeight: 200,
                      pinned: true,
                      backgroundColor: AppTheme.primaryColor,
                      actions: const [
                        CartActionButton(),
                      ],
                      flexibleSpace: FlexibleSpaceBar(
                        title: Text(
                          _shop?.name ?? 'ร้านค้าในระบบ',
                          style: const TextStyle(
                            color: Colors.white,
                            fontWeight: FontWeight.bold,
                            shadows: [
                              Shadow(offset: Offset(0, 1), blurRadius: 4, color: Colors.black54),
                            ],
                          ),
                        ),
                        background: Stack(
                          fit: StackFit.expand,
                          children: [
                            _buildBannerImage(_shop?.name),
                            const DecoratedBox(
                              decoration: BoxDecoration(
                                gradient: LinearGradient(
                                  begin: Alignment.topCenter,
                                  end: Alignment.bottomCenter,
                                  colors: [Colors.black38, Colors.black87],
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),

                    // Shop details card details
                    SliverToBoxAdapter(
                      child: Padding(
                        padding: const EdgeInsets.all(16.0),
                        child: Card(
                          elevation: 0,
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(16),
                            side: BorderSide(color: Colors.grey.shade200),
                          ),
                          child: Padding(
                            padding: const EdgeInsets.all(16.0),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Row(
                                  children: [
                                    const Icon(Icons.store, color: AppTheme.primaryColor),
                                    const SizedBox(width: 8),
                                    Text(
                                      _shop?.name ?? '',
                                      style: const TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 12),
                                Row(
                                  children: [
                                    const Icon(Icons.access_time, size: 16, color: Colors.grey),
                                    const SizedBox(width: 6),
                                    Text('เวลาจัดเตรียมประมาณ: ${_shop?.prepTimeMinutes ?? 15} นาที',
                                        style: const TextStyle(color: Colors.grey)),
                                  ],
                                ),
                                if (_shop?.lat != null && _shop?.lng != null) ...[
                                  const SizedBox(height: 8),
                                  Row(
                                    children: [
                                      const Icon(Icons.location_on, size: 16, color: Colors.grey),
                                      const SizedBox(width: 6),
                                      Text(
                                        'พิกัดร้านค้า: ${_shop!.lat!.toStringAsFixed(4)}, ${_shop!.lng!.toStringAsFixed(4)}',
                                        style: const TextStyle(color: Colors.grey),
                                      ),
                                    ],
                                  ),
                                ],
                              ],
                            ),
                          ),
                        ),
                      ),
                    ),

                    // Menu title section
                    const SliverToBoxAdapter(
                      child: Padding(
                        padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                        child: Text(
                          'รายการเมนูอาหารทั้งหมด',
                          style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                        ),
                      ),
                    ),

                    // Dishes Grid
                    if (_dishes.isEmpty)
                      const SliverFillRemaining(
                        hasScrollBody: false,
                        child: Center(
                          child: Text('ร้านค้านี้ยังไม่มีรายการเมนูอาหารในขณะนี้'),
                        ),
                      )
                    else
                      SliverPadding(
                        padding: const EdgeInsets.symmetric(horizontal: 16),
                        sliver: SliverGrid(
                          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                            crossAxisCount: 2,
                            childAspectRatio: 0.72,
                            crossAxisSpacing: 12,
                            mainAxisSpacing: 12,
                          ),
                          delegate: SliverChildBuilderDelegate(
                            (context, index) {
                              final dish = _dishes[index];
                              return Card(
                                elevation: 0,
                                shape: RoundedRectangleBorder(
                                  borderRadius: BorderRadius.circular(16),
                                  side: BorderSide(color: Colors.grey.shade200),
                                ),
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Expanded(
                                      child: Container(
                                        width: double.infinity,
                                        decoration: BoxDecoration(
                                          color: Colors.grey[200],
                                          borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
                                        ),
                                        clipBehavior: Clip.antiAlias,
                                        child: _buildDishImage(dish.imageUrl),
                                      ),
                                    ),
                                    Padding(
                                      padding: const EdgeInsets.all(12),
                                      child: Column(
                                        crossAxisAlignment: CrossAxisAlignment.start,
                                        children: [
                                          Text(
                                            dish.name,
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15),
                                          ),
                                          const SizedBox(height: 4),
                                          Text(
                                            dish.description ?? 'เมนูแนะนำ',
                                            maxLines: 1,
                                            overflow: TextOverflow.ellipsis,
                                            style: TextStyle(fontSize: 12, color: Colors.grey[600]),
                                          ),
                                          const SizedBox(height: 8),
                                          Row(
                                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                            children: [
                                              Text(
                                                formatCurrency.format(dish.price),
                                                style: const TextStyle(
                                                    fontWeight: FontWeight.bold,
                                                    color: AppTheme.primaryColor,
                                                    fontSize: 15),
                                              ),
                                              Material(
                                                color: Colors.transparent,
                                                child: InkWell(
                                                  onTap: () => _onAddDish(context, dish),
                                                  borderRadius: BorderRadius.circular(20),
                                                  child: Container(
                                                    padding: const EdgeInsets.all(6),
                                                    decoration: const BoxDecoration(
                                                      color: AppTheme.primaryColor,
                                                      shape: BoxShape.circle,
                                                    ),
                                                    child: const Icon(Icons.add, color: Colors.white, size: 16),
                                                  ),
                                                ),
                                              ),
                                            ],
                                          ),
                                        ],
                                      ),
                                    ),
                                  ],
                                ),
                              );
                            },
                            childCount: _dishes.length,
                          ),
                        ),
                      ),
                    const SliverToBoxAdapter(child: SizedBox(height: 40)),
                  ],
                ),
    );
  }

  Widget _buildBannerImage(String? shopName) {
    String url = 'https://images.unsplash.com/photo-1504674900247-0877df9cc836?w=600&auto=format&fit=crop&q=80';
    if (shopName != null) {
      if (shopName.contains('Burger')) {
        url = 'https://images.unsplash.com/photo-1568901346375-23c9450c58cd?w=600&auto=format&fit=crop&q=80';
      } else if (shopName.contains('Sushi')) {
        url = 'https://images.unsplash.com/photo-1579871494447-9811cf80d66c?w=600&auto=format&fit=crop&q=80';
      }
    }
    return Image.network(url, fit: BoxFit.cover);
  }

  Widget _buildDishImage(String? url) {
    if (url == null || url.isEmpty) {
      return const Center(
        child: Icon(Icons.fastfood, size: 40, color: Colors.grey),
      );
    }
    if (url.startsWith('data:image')) {
      try {
        final base64Part = url.split(',').last;
        return Image.memory(
          base64Decode(base64Part),
          fit: BoxFit.cover,
          errorBuilder: (_, __, ___) => const Center(
            child: Icon(Icons.broken_image, size: 40, color: Colors.grey),
          ),
        );
      } catch (_) {
        return const Center(
          child: Icon(Icons.broken_image, size: 40, color: Colors.grey),
        );
      }
    }
    return Image.network(
      url,
      fit: BoxFit.cover,
      errorBuilder: (_, __, ___) => const Center(
        child: Icon(Icons.fastfood, size: 40, color: Colors.grey),
      ),
    );
  }
}
