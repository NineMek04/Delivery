import '../core/api/api_helpers.dart';

/// Shop data model matching backend ShopDto.
class ShopDto {
  final String id;
  final String trackingCode;
  final String name;
  final String menuName;
  final double menuPrice;
  final double? lat;
  final double? lng;
  final bool isOpen;
  final int prepTimeMinutes;
  final String? openingHours;
  final DateTime? createdAt;
  final List<MenuItemDto>? menuItems;

  const ShopDto({
    required this.id,
    this.trackingCode = '',
    required this.name,
    this.menuName = '',
    this.menuPrice = 0,
    this.lat,
    this.lng,
    this.isOpen = true,
    this.prepTimeMinutes = 15,
    this.openingHours,
    this.createdAt,
    this.menuItems,
  });

  factory ShopDto.fromJson(Map<String, dynamic> json) {
    return ShopDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      trackingCode: readField<String>(json, 'TrackingCode') ??
          readField<String>(json, 'trackingCode') ??
          '',
      name: readField<String>(json, 'Name') ??
          readField<String>(json, 'name') ??
          '',
      menuName: readField<String>(json, 'MenuName') ??
          readField<String>(json, 'menuName') ??
          '',
      menuPrice: (readField<num>(json, 'MenuPrice') ??
              readField<num>(json, 'menuPrice') ??
              0)
          .toDouble(),
      lat: (readField<num>(json, 'Lat') ?? readField<num>(json, 'lat'))
          ?.toDouble(),
      lng: (readField<num>(json, 'Lng') ?? readField<num>(json, 'lng'))
          ?.toDouble(),
      isOpen: readField<bool>(json, 'IsOpen') ??
          readField<bool>(json, 'isOpen') ??
          true,
      prepTimeMinutes:
          readField<int>(json, 'PrepTimeMinutes') ??
          readField<int>(json, 'prepTimeMinutes') ??
          15,
      openingHours: readField<String>(json, 'OpeningHours') ??
          readField<String>(json, 'openingHours'),
      createdAt: _parseDate(
        readField<String>(json, 'CreatedAt') ??
            readField<String>(json, 'createdAt'),
      ),
      menuItems: _parseMenuItems(json),
    );
  }

  Map<String, dynamic> toJson() => {
        'Id': id,
        'Name': name,
        'MenuName': menuName,
        'MenuPrice': menuPrice,
        if (lat != null) 'Lat': lat,
        if (lng != null) 'Lng': lng,
        'IsOpen': isOpen,
        'PrepTimeMinutes': prepTimeMinutes,
        if (openingHours != null) 'OpeningHours': openingHours,
      };

  static DateTime? _parseDate(String? raw) {
    if (raw == null) return null;
    return DateTime.tryParse(raw);
  }

  static List<MenuItemDto>? _parseMenuItems(Map<String, dynamic> json) {
    final raw = readField<List>(json, 'MenuItems') ??
        readField<List>(json, 'menuItems');
    if (raw == null) return null;
    return raw
        .map((e) => MenuItemDto.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
  }
}

/// Simple menu item model used inside ShopDto.
/// Full model is in menu_item.dart.
class MenuItemDto {
  final String id;
  final String trackingCode;
  final String name;
  final String? description;
  final double price;
  final String? imageUrl;
  final String shopId;
  final String? menuCategoryId;
  final List<MenuItemOptionDto>? options;
  final DateTime? createdAt;

  const MenuItemDto({
    required this.id,
    this.trackingCode = '',
    required this.name,
    this.description,
    this.price = 0,
    this.imageUrl,
    this.shopId = '',
    this.menuCategoryId,
    this.options,
    this.createdAt,
  });

  factory MenuItemDto.fromJson(Map<String, dynamic> json) {
    return MenuItemDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      trackingCode: readField<String>(json, 'TrackingCode') ??
          readField<String>(json, 'trackingCode') ??
          '',
      name: readField<String>(json, 'Name') ??
          readField<String>(json, 'name') ??
          '',
      description: readField<String>(json, 'Description') ??
          readField<String>(json, 'description'),
      price: (readField<num>(json, 'Price') ??
              readField<num>(json, 'price') ??
              0)
          .toDouble(),
      imageUrl: readField<String>(json, 'ImageUrl') ??
          readField<String>(json, 'imageUrl'),
      shopId: readField<String>(json, 'ShopId') ??
          readField<String>(json, 'shopId') ??
          '',
      menuCategoryId: readField<String>(json, 'MenuCategoryId') ??
          readField<String>(json, 'menuCategoryId'),
      options: _parseOptions(json),
      createdAt: _parseDate(
        readField<String>(json, 'CreatedAt') ??
            readField<String>(json, 'createdAt'),
      ),
    );
  }

  Map<String, dynamic> toJson() => {
        'Name': name,
        if (description != null) 'Description': description,
        'Price': price,
        if (imageUrl != null) 'ImageUrl': imageUrl,
        'ShopId': shopId,
        if (menuCategoryId != null) 'MenuCategoryId': menuCategoryId,
      };

  static DateTime? _parseDate(String? raw) {
    if (raw == null) return null;
    return DateTime.tryParse(raw);
  }

  static List<MenuItemOptionDto>? _parseOptions(Map<String, dynamic> json) {
    final raw = readField<List>(json, 'Options') ??
        readField<List>(json, 'options');
    if (raw == null) return null;
    return raw
        .map((e) =>
            MenuItemOptionDto.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
  }
}

class MenuItemOptionDto {
  final String id;
  final String name;
  final bool required;
  final int maxSelections;
  final List<MenuItemOptionItemDto>? items;

  const MenuItemOptionDto({
    this.id = '',
    required this.name,
    this.required = false,
    this.maxSelections = 0,
    this.items,
  });

  factory MenuItemOptionDto.fromJson(Map<String, dynamic> json) {
    return MenuItemOptionDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      name: readField<String>(json, 'Name') ??
          readField<String>(json, 'name') ??
          '',
      required: readField<bool>(json, 'Required') ??
          readField<bool>(json, 'required') ??
          false,
      maxSelections:
          readField<int>(json, 'MaxSelections') ??
          readField<int>(json, 'maxSelections') ??
          0,
      items: _parseItems(json),
    );
  }

  static List<MenuItemOptionItemDto>? _parseItems(Map<String, dynamic> json) {
    final raw =
        readField<List>(json, 'Items') ?? readField<List>(json, 'items');
    if (raw == null) return null;
    return raw
        .map((e) =>
            MenuItemOptionItemDto.fromJson(Map<String, dynamic>.from(e as Map)))
        .toList();
  }
}

class MenuItemOptionItemDto {
  final String id;
  final String name;
  final double price;

  const MenuItemOptionItemDto({
    this.id = '',
    required this.name,
    this.price = 0,
  });

  factory MenuItemOptionItemDto.fromJson(Map<String, dynamic> json) {
    return MenuItemOptionItemDto(
      id: readField<String>(json, 'Id') ?? readField<String>(json, 'id') ?? '',
      name: readField<String>(json, 'Name') ??
          readField<String>(json, 'name') ??
          '',
      price: (readField<num>(json, 'Price') ??
              readField<num>(json, 'price') ??
              0)
          .toDouble(),
    );
  }
}
