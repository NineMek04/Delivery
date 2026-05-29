import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/api/services/order_api_service.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../models/order.dart';
import '../../../models/shop.dart';

class CartItem {
  final MenuItemDto dish;
  final int quantity;
  final String? notes;
  final String? optionsDescription;
  final double optionsPrice;

  CartItem({
    required this.dish,
    this.quantity = 1,
    this.notes,
    this.optionsDescription,
    this.optionsPrice = 0.0,
  });

  CartItem copyWith({
    MenuItemDto? dish,
    int? quantity,
    String? notes,
    String? optionsDescription,
    double? optionsPrice,
  }) {
    return CartItem(
      dish: dish ?? this.dish,
      quantity: quantity ?? this.quantity,
      notes: notes ?? this.notes,
      optionsDescription: optionsDescription ?? this.optionsDescription,
      optionsPrice: optionsPrice ?? this.optionsPrice,
    );
  }
}

class CartState {
  final Map<String, CartItem> items;
  final String? shopId;
  final bool isLoading;
  final String? error;

  const CartState({
    this.items = const {},
    this.shopId,
    this.isLoading = false,
    this.error,
  });

  CartState copyWith({
    Map<String, CartItem>? items,
    String? shopId,
    bool? isLoading,
    String? error,
    bool clearShop = false,
  }) {
    return CartState(
      items: items ?? this.items,
      shopId: clearShop ? null : (shopId ?? this.shopId),
      isLoading: isLoading ?? this.isLoading,
      error: error,
    );
  }
}

class CartNotifier extends StateNotifier<CartState> {
  final Ref _ref;

  CartNotifier(this._ref) : super(const CartState());

  void addItem(
    MenuItemDto dish, {
    String? optionsDescription,
    double optionsPrice = 0.0,
    String? notes,
  }) {
    // Single-shop constraint is removed to support ordering from multiple stores.
    
    // Create a unique key based on the menu item ID and options selected
    final itemId = optionsDescription != null && optionsDescription.isNotEmpty
        ? '${dish.id}_${optionsDescription.hashCode}'
        : dish.id;

    final newItems = Map<String, CartItem>.from(state.items);
    if (newItems.containsKey(itemId)) {
      newItems[itemId] = newItems[itemId]!.copyWith(
        quantity: newItems[itemId]!.quantity + 1,
      );
    } else {
      newItems[itemId] = CartItem(
        dish: dish,
        optionsDescription: optionsDescription,
        optionsPrice: optionsPrice,
        notes: notes,
      );
    }

    state = state.copyWith(
      items: newItems,
      shopId: dish.shopId,
    );
  }

  void updateQuantity(String itemId, int quantity) {
    if (quantity <= 0) {
      removeItem(itemId);
      return;
    }

    final newItems = Map<String, CartItem>.from(state.items);
    if (newItems.containsKey(itemId)) {
      newItems[itemId] = newItems[itemId]!.copyWith(quantity: quantity);
      state = state.copyWith(items: newItems);
    }
  }

  void removeItem(String itemId) {
    final newItems = Map<String, CartItem>.from(state.items);
    newItems.remove(itemId);

    if (newItems.isEmpty) {
      state = const CartState();
    } else {
      state = state.copyWith(items: newItems);
    }
  }

  void clearCart() {
    state = const CartState();
  }

  Future<void> checkout({
    required double dropoffLat,
    required double dropoffLng,
  }) async {
    if (state.items.isEmpty) return;

    state = state.copyWith(isLoading: true, error: null);

    try {
      final auth = _ref.read(authServiceProvider.notifier);
      final customerId = auth.userId ?? '';

      // 1. Group cart items by their shopId
      final itemsByShop = <String, List<CartItem>>{};
      for (final item in state.items.values) {
        final shopId = item.dish.shopId;
        itemsByShop.putIfAbsent(shopId, () => []).add(item);
      }

      final shopService = _ref.read(shopApiServiceProvider);
      final orderService = _ref.read(orderApiServiceProvider);

      // 2. Submit a separate order for each shop sequentially
      for (final entry in itemsByShop.entries) {
        final shopId = entry.key;
        final shopItems = entry.value;

        // Fetch coordinates for this specific shop
        final shop = await shopService.getById(shopId);
        final shopLat = shop.lat ?? 17.4138;
        final shopLng = shop.lng ?? 102.7872;

        final orderItems = shopItems.map((item) {
          return CreateOrderItemDto(
            menuItemId: item.dish.id,
            quantity: item.quantity,
            notes: item.notes,
            optionsDescription: item.optionsDescription,
          );
        }).toList();

        final createDto = CreateOrderDto(
          pickupLat: shopLat,
          pickupLng: shopLng,
          dropoffLat: dropoffLat,
          dropoffLng: dropoffLng,
          expectedDeliveryTime: DateTime.now().add(const Duration(minutes: 40)),
          customerId: customerId,
          shopId: shopId,
          items: orderItems,
        );

        await orderService.createOrder(createDto);
      }

      clearCart();
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
      rethrow;
    }
  }
}

final cartProvider = StateNotifierProvider<CartNotifier, CartState>((ref) {
  return CartNotifier(ref);
});
