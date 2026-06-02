import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;
import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:sqflite/sqflite.dart';
import 'package:path/path.dart';
import 'package:dio/dio.dart';
import 'package:logger/logger.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

/// Custom TileProvider that serves cached map tiles offline on-demand.
class CachedTileProvider extends TileProvider {
  final String dbDir;

  CachedTileProvider({required this.dbDir});

  @override
  ImageProvider getImage(TileCoordinates coordinates, TileLayer options) {
    final url = getTileUrl(coordinates, options);
    final cacheKey = '${coordinates.z}_${coordinates.x}_${coordinates.y}';
    return CachedTileImageProvider(url, cacheKey: cacheKey, dbDir: dbDir);
  }
}

/// Custom ImageProvider to handle tile caching from/to local file system.
class CachedTileImageProvider extends ImageProvider<CachedTileImageProvider> {
  final String url;
  final String cacheKey;
  final String dbDir;
  final Dio _dio = Dio();

  CachedTileImageProvider(this.url, {
    required this.cacheKey,
    required this.dbDir,
  });

  @override
  Future<CachedTileImageProvider> obtainKey(ImageConfiguration configuration) {
    return SynchronousFuture<CachedTileImageProvider>(this);
  }

  @override
  ImageStreamCompleter loadImage(CachedTileImageProvider key, ImageDecoderCallback decode) {
    return MultiFrameImageStreamCompleter(
      codec: _loadAsync(key, decode),
      scale: 1.0,
      debugLabel: url,
      informationCollector: () => <DiagnosticsNode>[
        DiagnosticsProperty<ImageProvider>('Image provider', this),
        DiagnosticsProperty<CachedTileImageProvider>('Image key', key),
      ],
    );
  }

  Future<ui.Codec> _loadAsync(CachedTileImageProvider key, ImageDecoderCallback decode) async {
    final tileFile = File(join(dbDir, 'map_tiles', '$cacheKey.png'));

    try {
      // 1. Try reading from cache
      if (await tileFile.exists()) {
        final bytes = await tileFile.readAsBytes();
        if (bytes.isNotEmpty) {
          final buffer = await ui.ImmutableBuffer.fromUint8List(bytes);
          return await decode(buffer);
        }
      }
    } catch (e) {
      _logger.w('Failed to read cached tile: $cacheKey. Fallback to network.', error: e);
    }

    // 2. Fetch from network on cache miss
    try {
      final response = await _dio.get<List<int>>(
        url,
        options: Options(
          responseType: ResponseType.bytes,
          sendTimeout: const Duration(seconds: 5),
          receiveTimeout: const Duration(seconds: 5),
        ),
      );

      final data = response.data;
      if (response.statusCode == 200 && data != null && data.isNotEmpty) {
        // Save to cache directory asynchronously
        unawaited(() async {
          try {
            await tileFile.parent.create(recursive: true);
            await tileFile.writeAsBytes(data);
          } catch (e) {
            _logger.w('Failed to write tile cache: $cacheKey', error: e);
          }
        }());

        final buffer = await ui.ImmutableBuffer.fromUint8List(Uint8List.fromList(data));
        return await decode(buffer);
      }
    } catch (e) {
      _logger.e('Failed to fetch map tile from network: $url', error: e);
    }

    // 3. Fallback: return transparent 1x1 png image when totally offline/failed
    final transparentBytes = Uint8List.fromList([
      137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0,
      1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 31, 21, 108, 137, 0, 0, 0, 11, 73, 68,
      65, 84, 120, 156, 99, 96, 0, 0, 0, 2, 0, 1, 226, 33, 188, 51, 0, 0, 0,
      0, 73, 69, 78, 68, 174, 66, 96, 130
    ]);
    final buffer = await ui.ImmutableBuffer.fromUint8List(transparentBytes);
    return await decode(buffer);
  }

  @override
  bool operator ==(Object other) {
    if (other.runtimeType != runtimeType) return false;
    return other is CachedTileImageProvider &&
        other.url == url &&
        other.cacheKey == cacheKey;
  }

  @override
  int get hashCode => Object.hash(url, cacheKey);
}
