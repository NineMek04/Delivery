// ignore: avoid_web_libraries_in_flutter
import 'dart:html' as html;

/// Returns the current browser window origin (e.g. "http://localhost:8080").
String getWindowOrigin() => html.window.location.origin;
