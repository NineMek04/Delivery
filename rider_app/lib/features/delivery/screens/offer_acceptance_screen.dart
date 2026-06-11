import 'dart:async';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:audioplayers/audioplayers.dart';
import 'package:vibration/vibration.dart';
import 'package:google_fonts/google_fonts.dart';

class OfferAcceptanceScreen extends ConsumerStatefulWidget {
  final String orderId;
  final String pickupLocation;
  final String dropoffLocation;
  final double distanceKm;
  final double deliveryFee;

  const OfferAcceptanceScreen({
    super.key,
    required this.orderId,
    required this.pickupLocation,
    required this.dropoffLocation,
    required this.distanceKm,
    required this.deliveryFee,
  });

  @override
  ConsumerState<OfferAcceptanceScreen> createState() => _OfferAcceptanceScreenState();
}

class _OfferAcceptanceScreenState extends ConsumerState<OfferAcceptanceScreen> with SingleTickerProviderStateMixin {
  late Timer _timer;
  int _timeLeft = 30;
  final AudioPlayer _audioPlayer = AudioPlayer();
  late AnimationController _animationController;
  late Animation<double> _scaleAnimation;

  @override
  void initState() {
    super.initState();
    _startTimer();
    _playAlert();

    _animationController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 600),
    )..repeat(reverse: true);

    _scaleAnimation = Tween<double>(begin: 1.0, end: 1.05).animate(
      CurvedAnimation(parent: _animationController, curve: Curves.easeInOut),
    );
  }

  void _startTimer() {
    _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (_timeLeft > 0) {
        setState(() {
          _timeLeft--;
        });
      } else {
        _timer.cancel();
        _rejectOffer(autoDismiss: true);
      }
    });
  }

  Future<void> _playAlert() async {
    if (kIsWeb) return;
    bool? hasVibrator = await Vibration.hasVibrator();
    if (hasVibrator == true) {
      Vibration.vibrate(pattern: [500, 1000, 500, 1000]);
    }
  }

  void _acceptOffer() {
    HapticFeedback.heavyImpact();
    _cleanup();
    if (mounted) context.pop();
  }

  void _rejectOffer({bool autoDismiss = false}) {
    HapticFeedback.lightImpact();
    _cleanup();
    if (mounted) {
      if (autoDismiss) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Offer expired.')),
        );
      }
      context.pop();
    }
  }

  void _cleanup() {
    _timer.cancel();
    _audioPlayer.dispose();
    _animationController.dispose();
    if (!kIsWeb) Vibration.cancel();
  }

  @override
  void dispose() {
    _cleanup();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final progress = _timeLeft / 30;
    
    return Scaffold(
      backgroundColor: Colors.black87,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24.0),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Icon(Icons.delivery_dining, size: 80, color: Colors.greenAccent),
              const SizedBox(height: 16),
              Text(
                'NEW DELIVERY OFFER',
                style: GoogleFonts.poppins(
                  fontSize: 24,
                  fontWeight: FontWeight.bold,
                  color: Colors.white,
                  letterSpacing: 1.5,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                'Order #${widget.orderId}',
                style: GoogleFonts.poppins(color: Colors.grey[400], fontSize: 16),
              ),
              const SizedBox(height: 32),
              
              // Details Card
              Container(
                padding: const EdgeInsets.all(20),
                decoration: BoxDecoration(
                  color: Colors.grey[900],
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(color: Colors.grey[800]!),
                ),
                child: Column(
                  children: [
                    _buildLocationRow(Icons.store, Colors.orange, 'Pickup', widget.pickupLocation),
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 8, horizontal: 10),
                      child: Align(
                        alignment: Alignment.centerLeft,
                        child: Icon(Icons.more_vert, color: Colors.grey, size: 20),
                      ),
                    ),
                    _buildLocationRow(Icons.location_on, Colors.blue, 'Dropoff', widget.dropoffLocation),
                    const Divider(height: 32, color: Colors.grey),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text('Distance', style: GoogleFonts.poppins(color: Colors.grey[400])),
                            Text('${widget.distanceKm} km', style: GoogleFonts.poppins(color: Colors.white, fontSize: 18, fontWeight: FontWeight.bold)),
                          ],
                        ),
                        Column(
                          crossAxisAlignment: CrossAxisAlignment.end,
                          children: [
                            Text('Fee', style: GoogleFonts.poppins(color: Colors.grey[400])),
                            Text('฿${widget.deliveryFee.toStringAsFixed(2)}', style: GoogleFonts.poppins(color: Colors.greenAccent, fontSize: 24, fontWeight: FontWeight.bold)),
                          ],
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 48),

              // Timer
              Stack(
                alignment: Alignment.center,
                children: [
                  SizedBox(
                    width: 100,
                    height: 100,
                    child: CircularProgressIndicator(
                      value: progress,
                      strokeWidth: 8,
                      backgroundColor: Colors.grey[800],
                      valueColor: AlwaysStoppedAnimation<Color>(
                        progress > 0.3 ? Colors.greenAccent : Colors.redAccent,
                      ),
                    ),
                  ),
                  Text(
                    '$_timeLeft',
                    style: GoogleFonts.poppins(
                      fontSize: 32,
                      fontWeight: FontWeight.bold,
                      color: progress > 0.3 ? Colors.white : Colors.redAccent,
                    ),
                  ),
                ],
              ),
              const Spacer(),

              // Actions
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => _rejectOffer(autoDismiss: false),
                      style: OutlinedButton.styleFrom(
                        padding: const EdgeInsets.symmetric(vertical: 16),
                        side: const BorderSide(color: Colors.redAccent, width: 2),
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      ),
                      child: Text(
                        'REJECT',
                        style: GoogleFonts.poppins(color: Colors.redAccent, fontSize: 16, fontWeight: FontWeight.bold),
                      ),
                    ),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    flex: 2,
                    child: ScaleTransition(
                      scale: _scaleAnimation,
                      child: ElevatedButton(
                        onPressed: _acceptOffer,
                        style: ElevatedButton.styleFrom(
                          backgroundColor: Colors.greenAccent[700],
                          padding: const EdgeInsets.symmetric(vertical: 16),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                          elevation: 8,
                        ),
                        child: Text(
                          'ACCEPT OFFER',
                          style: GoogleFonts.poppins(color: Colors.black, fontSize: 18, fontWeight: FontWeight.bold),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildLocationRow(IconData icon, Color iconColor, String title, String address) {
    return Row(
      children: [
        Icon(icon, color: iconColor),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: GoogleFonts.poppins(color: Colors.grey[500], fontSize: 12)),
              Text(
                address,
                style: GoogleFonts.poppins(color: Colors.white, fontSize: 14),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
      ],
    );
  }
}
