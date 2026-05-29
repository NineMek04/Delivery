import 'package:flutter/material.dart';

class StoreListScreen extends StatelessWidget {
  const StoreListScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('เลือกร้านอาหาร')),
      body: const Center(child: Text('รายการร้านอาหาร (จะมาเร็วๆ นี้)')),
    );
  }
}
