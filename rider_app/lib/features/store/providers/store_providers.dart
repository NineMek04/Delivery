import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter/foundation.dart';

import '../../../core/api/api_helpers.dart';
import '../../../core/api/services/shop_api_service.dart';
import '../../../core/api/services/menu_item_api_service.dart';
import '../../../core/api/services/menu_category_api_service.dart';
import '../../../core/api/services/auth_api_service.dart';
import '../../../core/auth/auth_service.dart';
import '../../../models/shop.dart';

// ═══════════════════════════════════════════════════════════════════
// Shop Provider — loads the shop linked to the current StorePartner user
// ═══════════════════════════════════════════════════════════════════

final currentShopProvider = FutureProvider<ShopDto?>((ref) async {
  final authStatus = ref.watch(authServiceProvider);
  if (authStatus != AuthStatus.authenticated) {
    debugPrint('[StoreProviders] User is not authenticated ($authStatus).');
    return null;
  }

  final authService = ref.read(authServiceProvider.notifier);
  final userData = await authService.getUserData();
  if (userData == null) {
    debugPrint('[StoreProviders] No cached user data found.');
    return null;
  }

  String? shopId =
      readField<String>(userData, 'ShopId') ?? readField<String>(userData, 'shopId');
  
  if (shopId == null || shopId.isEmpty) {
    debugPrint('[StoreProviders] shopId is missing in local storage. Fetching fresh session...');
    final authApi = ref.read(authApiServiceProvider);
    final sessionUser = await authApi.getSession();
    shopId = sessionUser.shopId;
    debugPrint('[StoreProviders] Fresh session fetched. ShopId: $shopId');
    if (shopId != null && shopId.isNotEmpty) {
      await authService.setUserData(sessionUser.toJson());
    }
  }

  if (shopId == null || shopId.isEmpty) {
    throw const ApiException(
      'The signed-in store account is not linked to a shop.',
      code: 'SHOP_CONTEXT_MISSING',
    );
  }

  final shopApi = ref.read(shopApiServiceProvider);
  return shopApi.getById(shopId);
});

// ═══════════════════════════════════════════════════════════════════
// Menu Categories Provider — loads menu categories for the current shop
// ═══════════════════════════════════════════════════════════════════

final menuCategoriesProvider =
    AsyncNotifierProvider<MenuCategoriesNotifier, List<MenuCategoryDto>>(
  MenuCategoriesNotifier.new,
);

class MenuCategoriesNotifier extends AsyncNotifier<List<MenuCategoryDto>> {
  @override
  Future<List<MenuCategoryDto>> build() async {
    final shop = await ref.watch(currentShopProvider.future);
    if (shop == null) {
      throw const ApiException(
        'Shop data is unavailable.',
        code: 'SHOP_CONTEXT_MISSING',
      );
    }

    final categoryApi = ref.read(menuCategoryApiServiceProvider);
    return categoryApi.getByShop(shop.id);
  }

  Future<MenuCategoryDto> addCategory(String name, {String? description}) async {
    final shop = await ref.read(currentShopProvider.future);
    if (shop == null) throw Exception('ไม่พบข้อมูลร้านค้า');

    final categoryApi = ref.read(menuCategoryApiServiceProvider);
    final newCat = await categoryApi.create({
      'Name': name,
      if (description != null) 'Description': description,
      'ShopId': shop.id,
      'DisplayOrder': 0,
    });
    
    final currentList = state.value ?? const <MenuCategoryDto>[];
    state = AsyncValue.data([...currentList, newCat]);
    return newCat;
  }

  void refresh() {
    ref.invalidateSelf();
  }
}

// ═══════════════════════════════════════════════════════════════════
// Menu Items Provider — loads menu items for the current shop
// ═══════════════════════════════════════════════════════════════════

final menuItemsProvider =
    AsyncNotifierProvider<MenuItemsNotifier, List<MenuItemDto>>(
  MenuItemsNotifier.new,
);

class MenuItemsNotifier extends AsyncNotifier<List<MenuItemDto>> {
  @override
  Future<List<MenuItemDto>> build() async {
    final shop = await ref.watch(currentShopProvider.future);
    if (shop == null) {
      throw const ApiException(
        'Shop data is unavailable.',
        code: 'SHOP_CONTEXT_MISSING',
      );
    }

    final menuApi = ref.read(menuItemApiServiceProvider);
    final result = await menuApi.getByShop(shop.id, pageSize: 200);
    return result.items;
  }

  Future<MenuItemDto> addItem(Map<String, dynamic> data) async {
    debugPrint('[MenuItemsNotifier] addItem: $data');
    final menuApi = ref.read(menuItemApiServiceProvider);
    final created = await menuApi.create(data);
    final currentList = state.value ?? const <MenuItemDto>[];
    state = AsyncValue.data([...currentList, created]);
    return created;
  }

  Future<MenuItemDto> updateItem(String id, Map<String, dynamic> data) async {
    debugPrint('[MenuItemsNotifier] updateItem: $id with $data');
    final menuApi = ref.read(menuItemApiServiceProvider);
    final updated = await menuApi.update(id, data);
    final currentList = state.value ?? const <MenuItemDto>[];
    state = AsyncValue.data([
      for (final item in currentList)
        if (item.id == id) updated else item,
    ]);
    return updated;
  }

  Future<void> deleteItem(String id) async {
    debugPrint('[MenuItemsNotifier] deleteItem: $id');
    final menuApi = ref.read(menuItemApiServiceProvider);
    try {
      await menuApi.delete(id);
      debugPrint('[MenuItemsNotifier] deleteItem success on server for $id');
    } catch (e) {
      debugPrint('[MenuItemsNotifier] deleteItem failed on server for $id: $e');
      rethrow;
    }
    final currentList = state.value ?? [];
    state = AsyncValue.data(
      currentList.where((item) => item.id != id).toList()
    );
  }

  Future<void> deleteItems(List<String> ids) async {
    debugPrint('[MenuItemsNotifier] deleteItems started for ids: $ids');
    final menuApi = ref.read(menuItemApiServiceProvider);
    for (final id in ids) {
      try {
        debugPrint('[MenuItemsNotifier] Deleting single item: $id');
        await menuApi.delete(id);
        debugPrint('[MenuItemsNotifier] Deleting single item success: $id');
      } catch (e) {
        debugPrint('[MenuItemsNotifier] Deleting single item failed: $id: $e');
        rethrow;
      }
    }
    final currentList = state.value ?? [];
    state = AsyncValue.data(
      currentList.where((item) => !ids.contains(item.id)).toList()
    );
    debugPrint('[MenuItemsNotifier] deleteItems successfully updated local state list');
  }

  void refresh() {
    ref.invalidateSelf();
  }
}
