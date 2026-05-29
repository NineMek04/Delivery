import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:audioplayers/audioplayers.dart';
import 'package:vibration/vibration.dart';

import '../../core/config/environment.dart';
import '../../models/dispatch_offer.dart';

/// Bottom sheet รับ/ปฏิเสธงาน พร้อม countdown 30 วินาที.
class OfferBottomSheet extends StatefulWidget {
  final DispatchOffer offer;
  final VoidCallback onAccept;
  final VoidCallback onReject;

  const OfferBottomSheet({
    super.key,
    required this.offer,
    required this.onAccept,
    required this.onReject,
  });

  static Future<void> show(
    BuildContext context, {
    required DispatchOffer offer,
    required VoidCallback onAccept,
    required VoidCallback onReject,
  }) {
    return showModalBottomSheet(
      context: context,
      isDismissible: false,
      enableDrag: false,
      showDragHandle: true,
      builder: (_) => OfferBottomSheet(
        offer: offer,
        onAccept: onAccept,
        onReject: onReject,
      ),
    );
  }

  @override
  State<OfferBottomSheet> createState() => _OfferBottomSheetState();
}

class _OfferBottomSheetState extends State<OfferBottomSheet> {
  late int _secondsLeft;
  Timer? _timer;
  AudioPlayer? _audioPlayer;

  @override
  void initState() {
    super.initState();
    _secondsLeft = _initialSeconds();
    _startAlerts();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (_secondsLeft <= 1) {
        _timer?.cancel();
        _stopAlerts();
        if (mounted) {
          Navigator.of(context).pop();
          widget.onReject();
        }
        return;
      }
      setState(() => _secondsLeft--);
    });
  }

  void _startAlerts() async {
    try {
      _audioPlayer = AudioPlayer();
      await _audioPlayer?.play(UrlSource('https://assets.mixkit.co/active_storage/sfx/911/911-200.wav'));
      _audioPlayer?.setReleaseMode(ReleaseMode.loop);
    } catch (_) {}

    try {
      if (await Vibration.hasVibrator() ?? false) {
        Vibration.vibrate(pattern: [500, 1000, 500, 1000], repeat: 0);
      }
    } catch (_) {}
  }

  void _stopAlerts() {
    try {
      _audioPlayer?.stop();
      _audioPlayer?.dispose();
    } catch (_) {}
    try {
      Vibration.cancel();
    } catch (_) {}
  }

  int _initialSeconds() {
    if (widget.offer.expiresAt != null) {
      final diff = widget.offer.expiresAt!.difference(DateTime.now()).inSeconds;
      if (diff > 0) return diff;
    }
    return Environment.offerCountdownSeconds;
  }

  @override
  void dispose() {
    _timer?.cancel();
    _stopAlerts();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final fee = NumberFormat.currency(locale: 'th', symbol: '฿', decimalDigits: 0)
        .format(widget.offer.order.deliveryFee ?? 0);

    return Padding(
      padding: EdgeInsets.only(
        left: 20,
        right: 20,
        top: 8,
        bottom: MediaQuery.of(context).padding.bottom + 20,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'งานใหม่!',
            style: Theme.of(context).textTheme.headlineSmall,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 8),
          LinearProgressIndicator(
            value: _secondsLeft / Environment.offerCountdownSeconds,
            minHeight: 6,
            borderRadius: BorderRadius.circular(4),
          ),
          const SizedBox(height: 4),
          Text(
            'หมดเวลาใน $_secondsLeft วินาที',
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 20),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceAround,
            children: [
              _info(Icons.route, '${widget.offer.order.distanceKm?.toStringAsFixed(1) ?? "—"} km'),
              _info(Icons.payments, fee),
            ],
          ),
          const SizedBox(height: 24),
          ElevatedButton(
            onPressed: () {
              _timer?.cancel();
              _stopAlerts();
              Navigator.of(context).pop();
              widget.onAccept();
            },
            child: const Text('รับงาน'),
          ),
          const SizedBox(height: 8),
          OutlinedButton(
            onPressed: () {
              _timer?.cancel();
              _stopAlerts();
              Navigator.of(context).pop();
              widget.onReject();
            },
            child: const Text('ปฏิเสธ'),
          ),
        ],
      ),
    );
  }

  Widget _info(IconData icon, String text) {
    return Column(
      children: [
        Icon(icon, color: Theme.of(context).colorScheme.primary),
        const SizedBox(height: 4),
        Text(text, style: Theme.of(context).textTheme.titleMedium),
      ],
    );
  }
}
