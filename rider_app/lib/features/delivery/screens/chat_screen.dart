import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';
import '../../../core/signalr/chat_service.dart';
import '../providers/delivery_provider.dart';

/// หน้าจอแชทแบบเรียลไทม์ประสานงานระหว่าง ไรเดอร์-ลูกค้า-ร้านค้า (Order-bound Chat Room)
class ChatScreen extends ConsumerStatefulWidget {
  final String orderId;

  const ChatScreen({super.key, required this.orderId});

  @override
  ConsumerState<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends ConsumerState<ChatScreen> {
  final TextEditingController _messageController = TextEditingController();
  final ScrollController _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(chatServiceProvider(widget.orderId).notifier).connectAndJoin();
    });
  }

  @override
  void dispose() {
    _messageController.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  void _scrollToBottom() {
    if (_scrollController.hasClients) {
      _scrollController.animateTo(
        _scrollController.position.maxScrollExtent,
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeOut,
      );
    }
  }

  Future<void> _sendMessage() async {
    final text = _messageController.text.trim();
    if (text.isEmpty) return;

    _messageController.clear();
    final success = await ref
        .read(chatServiceProvider(widget.orderId).notifier)
        .sendMessage(text);

    if (success) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToBottom());
    } else {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('ส่งข้อความไม่สำเร็จ กรุณาลองใหม่อีกครั้ง'),
            backgroundColor: Colors.red,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final chatState = ref.watch(chatServiceProvider(widget.orderId));
    final deliveryState = ref.watch(deliveryNotifierProvider);
    
    // ค้นหาออเดอร์ในหน้างานปัจจุบันเพื่อตรวจสอบสถานะล็อกห้องแชท
    final activeOrderIndex = deliveryState.activeOrders.indexWhere((o) => o.id == widget.orderId);
    final activeOrder = activeOrderIndex != -1 ? deliveryState.activeOrders[activeOrderIndex] : null;
    
    // แชทจะถูกปิดการส่งเมื่อเสร็จสิ้น (COMPLETED) หรือยกเลิก (CANCELLED)
    final bool isChatLocked = activeOrder == null || 
        activeOrder.status == 'COMPLETED' || 
        activeOrder.status == 'CANCELLED';

    // จัดตำแหน่งเลื่อนแชทล่างสุดเมื่อมีข้อความใหม่เข้ามา
    ref.listen(chatServiceProvider(widget.orderId), (previous, next) {
      if (previous?.messages.length != next.messages.length) {
        WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToBottom());
      }
    });

    final timeFormat = DateFormat('HH:mm');

    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('แชทประสานงาน'),
            Text(
              activeOrder != null ? 'ออเดอร์: ${activeOrder.trackingCode ?? activeOrder.id.substring(0, 8)}' : 'ออเดอร์: โหลดข้อมูล...',
              style: const TextStyle(fontSize: 12, fontWeight: FontWeight.normal),
            ),
          ],
        ),
        actions: [
          Container(
            margin: const EdgeInsets.only(right: 16),
            child: Row(
              children: [
                Icon(
                  Icons.circle,
                  color: chatState.isConnected ? Colors.green : Colors.red,
                  size: 10,
                ),
                const SizedBox(width: 6),
                Text(
                  chatState.isConnected ? 'เชื่อมต่อแล้ว' : 'ขาดการเชื่อมต่อ',
                  style: const TextStyle(fontSize: 12),
                ),
              ],
            ),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            // แสดงสถานะระหว่างเชื่อมต่อระบบแชท
            if (!chatState.isConnected && chatState.isConnecting)
              Container(
                color: Colors.orange.withOpacity(0.2),
                width: double.infinity,
                padding: const EdgeInsets.all(8),
                child: const Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    SizedBox(
                      width: 14,
                      height: 14,
                      child: CircularProgressIndicator(strokeWidth: 2, color: Colors.orange),
                    ),
                    SizedBox(width: 8),
                    Text('กำลังเชื่อมต่อห้องแชท...', style: TextStyle(fontSize: 12, color: Colors.orange)),
                  ],
                ),
              ),

            // แบนเนอร์แสดงห้องสนทนาถูกล็อก
            if (isChatLocked)
              Container(
                color: Colors.red.shade100,
                width: double.infinity,
                padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 16),
                child: Row(
                  children: [
                    Icon(Icons.lock_outline, color: Colors.red.shade700, size: 20),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        'ห้องสนทนาถูกปิดแล้วเนื่องจากออเดอร์เสร็จสิ้นหรือยกเลิกแล้ว',
                        style: TextStyle(color: Colors.red.shade900, fontSize: 13, fontWeight: FontWeight.bold),
                      ),
                    ),
                  ],
                ),
              ),

            // ประวัติข้อความแชท
            Expanded(
              child: chatState.messages.isEmpty
                  ? Center(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(Icons.chat_bubble_outline, size: 48, color: Colors.grey.shade400),
                          const SizedBox(height: 16),
                          Text(
                            'ยังไม่มีข้อความสนทนาในออเดอร์นี้',
                            style: TextStyle(color: Colors.grey.shade500),
                          ),
                        ],
                      ),
                    )
                  : ListView.builder(
                      controller: _scrollController,
                      padding: const EdgeInsets.all(16),
                      itemCount: chatState.messages.length,
                      itemBuilder: (context, index) {
                        final msg = chatState.messages[index];
                        final isMe = msg.senderRole == 'Rider';

                        return Align(
                          alignment: isMe ? Alignment.centerRight : Alignment.centerLeft,
                          child: Container(
                            margin: const EdgeInsets.only(bottom: 12),
                            constraints: BoxConstraints(
                              maxWidth: MediaQuery.of(context).size.width * 0.75,
                            ),
                            child: Column(
                              crossAxisAlignment:
                                  isMe ? CrossAxisAlignment.end : CrossAxisAlignment.start,
                              children: [
                                // บ่งบอกชื่อบทบาทผู้ส่งด้านซ้าย
                                if (!isMe)
                                  Padding(
                                    padding: const EdgeInsets.only(left: 4, bottom: 4),
                                    child: Text(
                                      '${msg.senderRole} (${msg.senderId.substring(0, 4).toUpperCase()})',
                                      style: TextStyle(fontSize: 10, color: Colors.grey.shade600, fontWeight: FontWeight.bold),
                                    ),
                                  ),
                                Container(
                                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                                  decoration: BoxDecoration(
                                    color: isMe
                                        ? Theme.of(context).colorScheme.primary
                                        : Colors.grey.shade200,
                                    borderRadius: BorderRadius.only(
                                      topLeft: const Radius.circular(16),
                                      topRight: const Radius.circular(16),
                                      bottomLeft: Radius.circular(isMe ? 16 : 0),
                                      bottomRight: Radius.circular(isMe ? 0 : 16),
                                    ),
                                    boxShadow: [
                                      BoxShadow(
                                        color: Colors.black.withOpacity(0.05),
                                        blurRadius: 4,
                                        offset: const Offset(0, 2),
                                      ),
                                    ],
                                  ),
                                  child: Text(
                                    msg.message,
                                    style: TextStyle(
                                      color: isMe ? Colors.white : Colors.black87,
                                      fontSize: 14,
                                    ),
                                  ),
                                ),
                                Padding(
                                  padding: const EdgeInsets.only(top: 4, left: 4, right: 4),
                                  child: Text(
                                    timeFormat.format(msg.createdAt.toLocal()),
                                    style: TextStyle(fontSize: 9, color: Colors.grey.shade500),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
            ),

            // ส่วนสำหรับพิมพ์ข้อความส่งด้านล่าง
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: Theme.of(context).cardColor,
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withOpacity(0.05),
                    blurRadius: 10,
                    offset: const Offset(0, -2),
                  ),
                ],
              ),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _messageController,
                      enabled: chatState.isConnected && !isChatLocked,
                      decoration: InputDecoration(
                        hintText: isChatLocked
                            ? 'ห้องแชทถูกล็อกถาวรแล้ว'
                            : (chatState.isConnected ? 'พิมพ์ข้อความส่งหาลูกค้า/ร้านค้า...' : 'กำลังเชื่อมต่อระบบแชท...'),
                        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                        border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(24),
                          borderSide: BorderSide(color: Colors.grey.shade300),
                        ),
                        enabledBorder: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(24),
                          borderSide: BorderSide(color: Colors.grey.shade300),
                        ),
                        filled: true,
                        fillColor: isChatLocked
                            ? Colors.grey.shade100
                            : Theme.of(context).canvasColor,
                      ),
                      textInputAction: TextInputAction.send,
                      onSubmitted: (_) => _sendMessage(),
                    ),
                  ),
                  const SizedBox(width: 8),
                  GestureDetector(
                    onTap: (chatState.isConnected && !isChatLocked) ? _sendMessage : null,
                    child: CircleAvatar(
                      radius: 22,
                      backgroundColor: (chatState.isConnected && !isChatLocked)
                          ? Theme.of(context).colorScheme.primary
                          : Colors.grey.shade300,
                      child: Icon(
                        Icons.send,
                        color: (chatState.isConnected && !isChatLocked) ? Colors.white : Colors.grey.shade500,
                        size: 18,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
