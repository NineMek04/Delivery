# Flutter ProGuard Rules
-keep class io.flutter.app.** { *; }
-keep class io.flutter.plugin.**  { *; }
-keep class io.flutter.util.**  { *; }
-keep class io.flutter.view.**  { *; }
-keep class io.flutter.**  { *; }
-keep class io.flutter.plugins.**  { *; }

# Keep Geolocator & Location plugins
-keep class com.baseflow.geolocator.** { *; }
-keep class com.baseflow.geolocator_android.** { *; }

# Keep Flutter Secure Storage
-keep class com.it_nomads.fluttersecurestorage.** { *; }

# Keep Image Picker
-keep class io.flutter.plugins.imagepicker.** { *; }

# Keep Audio Players & Vibration
-keep class xyz.luan.audioplayers.** { *; }
-keep class com.chavesgu.vibration.** { *; }

# Keep SQLite
-keep class com.tekartik.sqflite.** { *; }

# Prevent obfuscation of serializable / model classes
-keepattributes *Annotation*,EnclosingMethod,Signature,InnerClasses
-dontwarn javax.annotation.**
-dontwarn com.google.android.play.core.**
-dontwarn io.flutter.embedding.engine.deferredcomponents.**

