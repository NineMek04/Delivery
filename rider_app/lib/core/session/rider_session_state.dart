import '../../models/dispatch_offer.dart';

class RiderSessionState {
  final bool isOnline;
  final bool isTransitioning;
  final String? error;
  final DispatchOffer? incomingOffer;

  const RiderSessionState({
    this.isOnline = false,
    this.isTransitioning = false,
    this.error,
    this.incomingOffer,
  });

  RiderSessionState copyWith({
    bool? isOnline,
    bool? isTransitioning,
    String? error,
    DispatchOffer? incomingOffer,
    bool clearOffer = false,
  }) {
    return RiderSessionState(
      isOnline: isOnline ?? this.isOnline,
      isTransitioning: isTransitioning ?? this.isTransitioning,
      error: error,
      incomingOffer: clearOffer ? null : (incomingOffer ?? this.incomingOffer),
    );
  }
}
