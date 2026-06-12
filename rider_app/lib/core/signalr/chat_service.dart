import 'dart:async';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:logger/logger.dart';
import 'package:signalr_netcore/signalr_client.dart';
import '../auth/auth_service.dart';
import '../config/environment.dart';

final _logger = Logger(printer: PrettyPrinter(methodCount: 0));

class ChatMessage {
  final String id;
  final String orderId;
  final String senderId;
  final String senderRole;
  final String message;
  final DateTime createdAt;

  ChatMessage({
    required this.id,
    required this.orderId,
    required this.senderId,
    required this.senderRole,
    required this.message,
    required this.createdAt,
  });

  factory ChatMessage.fromJson(Map<String, dynamic> json) {
    return ChatMessage(
      id: json['id'] ?? json['Id'] ?? '',
      orderId: json['orderId'] ?? json['OrderId'] ?? '',
      senderId: json['senderId'] ?? json['SenderId'] ?? '',
      senderRole: json['senderRole'] ?? json['SenderRole'] ?? '',
      message: json['message'] ?? json['Message'] ?? '',
      createdAt: DateTime.parse(json['createdAt'] ?? json['CreatedAt'] ?? DateTime.now().toIso8601String()),
    );
  }
}

class ChatState {
  final List<ChatMessage> messages;
  final bool isConnected;
  final bool isConnecting;
  final String? activeOrderId;

  ChatState({
    required this.messages,
    required this.isConnected,
    required this.isConnecting,
    this.activeOrderId,
  });

  ChatState copyWith({
    List<ChatMessage>? messages,
    bool? isConnected,
    bool? isConnecting,
    String? activeOrderId,
  }) {
    return ChatState(
      messages: messages ?? this.messages,
      isConnected: isConnected ?? this.isConnected,
      isConnecting: isConnecting ?? this.isConnecting,
      activeOrderId: activeOrderId ?? this.activeOrderId,
    );
  }
}

class ChatService extends StateNotifier<ChatState> {
  final Ref ref;
  final String orderId;
  HubConnection? _hubConnection;

  ChatService(this.ref, this.orderId) : super(ChatState(
    messages: [],
    isConnected: false,
    isConnecting: false,
    activeOrderId: orderId,
  )) {
    ref.onDispose(() {
      _hubConnection?.stop();
    });
  }

  Future<void> connectAndJoin() async {
    if (state.isConnected || state.isConnecting) return;

    state = state.copyWith(isConnecting: true);

    final authService = ref.read(authServiceProvider.notifier);

    _hubConnection = HubConnectionBuilder()
        .withUrl(
          Environment.chatHubUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => authService.currentToken ?? '',
          ),
        )
        .withAutomaticReconnect(retryDelays: [0, 2000, 10000, 30000])
        .build();

    _registerHandlers();

    _hubConnection!.onclose(({error}) {
      _logger.w('ChatHub disconnected for Order ${state.activeOrderId}', error: error);
      state = state.copyWith(isConnected: false, isConnecting: false);
    });

    _hubConnection!.onreconnecting(({error}) {
      _logger.i('ChatHub reconnecting...', error: error);
      state = state.copyWith(isConnected: false, isConnecting: true);
    });

    _hubConnection!.onreconnected(({connectionId}) {
      _logger.i('ChatHub reconnected: $connectionId. Re-joining order chat.');
      state = state.copyWith(isConnected: true, isConnecting: false);
      _joinRoom();
    });

    try {
      await _hubConnection!.start();
      state = state.copyWith(isConnected: true, isConnecting: false);
      _logger.i('ChatHub connected. Joining room for order ${state.activeOrderId}');
      await _joinRoom();
    } catch (e) {
      state = state.copyWith(isConnected: false, isConnecting: false);
      _logger.e('ChatHub connection failed for Order ${state.activeOrderId}', error: e);
    }
  }

  Future<void> disconnect() async {
    await _hubConnection?.stop();
    _hubConnection = null;
    state = state.copyWith(isConnected: false, isConnecting: false);
  }

  Future<void> _joinRoom() async {
    if (_hubConnection == null || state.activeOrderId == null) return;
    try {
      await _hubConnection!.invoke('JoinOrderChat', args: [state.activeOrderId!]);
    } catch (e) {
      _logger.e('Failed to join chat room for Order ${state.activeOrderId}', error: e);
    }
  }

  Future<bool> sendMessage(String text) async {
    if (_hubConnection == null || !state.isConnected || state.activeOrderId == null) {
      return false;
    }

    try {
      await _hubConnection!.invoke('SendMessage', args: [state.activeOrderId!, text]);
      return true;
    } catch (e) {
      _logger.e('Failed to send message to Order ${state.activeOrderId}', error: e);
      return false;
    }
  }

  void _registerHandlers() {
    final hub = _hubConnection!;

    hub.on('ChatHistoryReceived', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final orderId = args[0] as String;
        if (orderId != state.activeOrderId) return;

        final rawMessages = args[1] as List;
        final list = rawMessages
            .map((m) => ChatMessage.fromJson(Map<String, dynamic>.from(m as Map)))
            .toList();

        state = state.copyWith(messages: list);
        _logger.i('Loaded ${list.length} historical chat messages.');
      } catch (e) {
        _logger.e('Failed to parse ChatHistoryReceived', error: e);
      }
    });

    hub.on('MessageReceived', (args) {
      if (args == null || args.isEmpty) return;
      try {
        final orderId = args[0] as String;
        if (orderId != state.activeOrderId) return;

        final msg = ChatMessage.fromJson(Map<String, dynamic>.from(args[1] as Map));
        
        // Prevent duplicate messages if any
        if (state.messages.any((m) => m.id == msg.id)) return;

        state = state.copyWith(messages: [...state.messages, msg]);
        _logger.i('Message received: ${msg.message}');
      } catch (e) {
        _logger.e('Failed to parse MessageReceived', error: e);
      }
    });
  }
}

final chatServiceProvider =
    StateNotifierProvider.family<ChatService, ChatState, String>((ref, orderId) {
  return ChatService(ref, orderId);
});
