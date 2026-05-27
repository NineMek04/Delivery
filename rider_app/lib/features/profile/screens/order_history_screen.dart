import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_fonts/google_fonts.dart';

// Filter state
final orderFilterProvider = StateProvider<String>((ref) => 'All');

class OrderHistoryScreen extends ConsumerWidget {
  const OrderHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final filter = ref.watch(orderFilterProvider);

    // Mock data
    final orders = [
      {'id': 'ORD-101', 'status': 'Completed', 'date': 'Today, 14:30', 'fee': 65.0, 'rating': 5.0},
      {'id': 'ORD-102', 'status': 'Completed', 'date': 'Today, 12:15', 'fee': 45.0, 'rating': 4.5},
      {'id': 'ORD-103', 'status': 'Cancelled', 'date': 'Yesterday', 'fee': 0.0, 'rating': null},
      {'id': 'ORD-104', 'status': 'Completed', 'date': 'Yesterday', 'fee': 85.0, 'rating': 5.0},
      {'id': 'ORD-105', 'status': 'Failed', 'date': 'May 24', 'fee': 0.0, 'rating': null},
    ];

    final filteredOrders = filter == 'All' 
        ? orders 
        : orders.where((o) => o['status'] == filter).toList();

    return Scaffold(
      backgroundColor: Colors.grey[100],
      appBar: AppBar(
        title: Text('Order History', style: GoogleFonts.poppins(fontWeight: FontWeight.bold, color: Colors.black87)),
        backgroundColor: Colors.white,
        elevation: 0,
        iconTheme: const IconThemeData(color: Colors.black87),
        actions: [
          IconButton(
            icon: const Icon(Icons.date_range),
            onPressed: () {
              // Show date picker in real app
            },
          )
        ],
      ),
      body: Column(
        children: [
          // Filters
          Container(
            color: Colors.white,
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Row(
                children: ['All', 'Completed', 'Cancelled', 'Failed'].map((status) {
                  final isSelected = filter == status;
                  return Padding(
                    padding: const EdgeInsets.only(right: 8.0),
                    child: FilterChip(
                      label: Text(status, style: GoogleFonts.poppins(
                        color: isSelected ? Colors.white : Colors.black87,
                        fontWeight: isSelected ? FontWeight.bold : FontWeight.normal
                      )),
                      selected: isSelected,
                      onSelected: (bool selected) {
                        ref.read(orderFilterProvider.notifier).state = status;
                      },
                      selectedColor: Colors.blueAccent,
                      backgroundColor: Colors.grey[200],
                    ),
                  );
                }).toList(),
              ),
            ),
          ),

          // List
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.all(16),
              itemCount: filteredOrders.length,
              itemBuilder: (context, index) {
                final order = filteredOrders[index];
                return _buildOrderCard(order);
              },
            ),
          )
        ],
      ),
    );
  }

  Widget _buildOrderCard(Map<String, dynamic> order) {
    Color statusColor;
    switch (order['status']) {
      case 'Completed': statusColor = Colors.green; break;
      case 'Cancelled': statusColor = Colors.grey; break;
      case 'Failed': statusColor = Colors.redAccent; break;
      default: statusColor = Colors.black;
    }

    return Card(
      elevation: 2,
      margin: const EdgeInsets.only(bottom: 12),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      child: InkWell(
        onTap: () {
          // View details
        },
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    order['id'],
                    style: GoogleFonts.poppins(fontWeight: FontWeight.bold, fontSize: 16),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                    decoration: BoxDecoration(
                      color: statusColor.withOpacity(0.1),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: statusColor),
                    ),
                    child: Text(
                      order['status'].toUpperCase(),
                      style: GoogleFonts.poppins(color: statusColor, fontSize: 10, fontWeight: FontWeight.bold),
                    ),
                  )
                ],
              ),
              const SizedBox(height: 12),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.access_time, size: 16, color: Colors.grey),
                      const SizedBox(width: 4),
                      Text(order['date'], style: GoogleFonts.poppins(color: Colors.grey[600], fontSize: 14)),
                    ],
                  ),
                  Text(
                    '฿${order['fee'].toStringAsFixed(2)}',
                    style: GoogleFonts.poppins(fontWeight: FontWeight.bold, fontSize: 16, color: Colors.black87),
                  )
                ],
              ),
              if (order['rating'] != null) ...[
                const SizedBox(height: 8),
                Row(
                  children: [
                    const Icon(Icons.star, size: 16, color: Colors.orange),
                    const SizedBox(width: 4),
                    Text('${order['rating']}', style: GoogleFonts.poppins(color: Colors.orange, fontWeight: FontWeight.bold)),
                  ],
                )
              ]
            ],
          ),
        ),
      ),
    );
  }
}
