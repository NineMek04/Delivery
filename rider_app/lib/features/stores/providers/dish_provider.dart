import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/api/api_helpers.dart';
import '../../../core/api/services/menu_item_api_service.dart';
import '../../../models/shop.dart';

final dishProvider = NotifierProvider<DishNotifier, DishState>(
  DishNotifier.new,
);

class DishNotifier extends Notifier<DishState> {
  @override
  DishState build() {
    return const DishState();
  }

  Future<void> loadDishes({String? search, bool refresh = false}) async {
    if (state.isLoading && !refresh) return;

    state = state.copyWith(isLoading: true, error: null);

    try {
      final result = await ref.read(menuItemApiServiceProvider).getAll(
        search: search,
        page: 1,
        pageSize: 50,
      );

      state = state.copyWith(
        isLoading: false,
        dishes: result.items,
      );
    } on ApiException catch (e) {
      state = state.copyWith(isLoading: false, error: e.message);
    } catch (e) {
      state = state.copyWith(isLoading: false, error: e.toString());
    }
  }
}

class DishState {
  final bool isLoading;
  final String? error;
  final List<MenuItemDto> dishes;

  const DishState({
    this.isLoading = false,
    this.error,
    this.dishes = const [],
  });

  DishState copyWith({
    bool? isLoading,
    String? error,
    List<MenuItemDto>? dishes,
  }) {
    return DishState(
      isLoading: isLoading ?? this.isLoading,
      error: error,
      dishes: dishes ?? this.dishes,
    );
  }
}
