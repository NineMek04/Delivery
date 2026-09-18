import 'dart:math';
import 'package:signalr_netcore/iretry_policy.dart';

/// Custom retry policy that implements SignalR's `IRetryPolicy` with a
/// randomized exponential backoff strategy (1s - 5s delay with jitter).
class JitteredRetryPolicy implements IRetryPolicy {
  @override
  int? nextRetryDelayInMilliseconds(RetryContext retryContext) {
    // Under the hood, SignalR client automatically resets the RetryContext state (including previousRetryCount)
    // to 0 when it successfully reconnects.
    final count = retryContext.previousRetryCount;
    
    // Exponential backoff base: 1000ms * 1.5^count
    final double baseDelay = 1000 * pow(1.5, count).toDouble();
    
    // Jitter: +/- 500ms
    final random = Random();
    final int jitter = random.nextInt(1001) - 500; // range: -500 to +500 ms
    
    int nextDelay = (baseDelay + jitter).round();
    
    // Bound the delay between 1,000ms and 5,000ms (Traffic smoothing guard)
    if (nextDelay < 1000) nextDelay = 1000;
    if (nextDelay > 5000) nextDelay = 5000;
    
    return nextDelay;
  }
}
