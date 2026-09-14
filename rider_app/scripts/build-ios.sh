#!/usr/bin/env bash
# =============================================================================
# Build Script for iOS (macOS only)
# =============================================================================
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "=================================================="
echo " Building Delivery App for iOS                    "
echo "=================================================="

TUNNEL_URL="${1:-}"

cd "$APP_DIR"

echo "--> Running flutter pub get..."
flutter pub get

echo "--> Installing CocoaPods dependencies..."
cd ios
pod install --repo-update
cd ..

API_FLAG=""
if [ -n "$TUNNEL_URL" ]; then
    echo "--> Pre-configuring Server Base URL: $TUNNEL_URL"
    API_FLAG="--dart-define=API_BASE_URL=$TUNNEL_URL"
else
    echo "--> Server URL will be configurable inside app (Settings > Server URL)"
fi

echo "--> Building iOS Release..."
flutter build ios --release --no-codesign $API_FLAG

echo ""
echo "=================================================="
echo " [OK] iOS Build Complete!"
echo " Location: $APP_DIR/build/ios/iphoneos/Runner.app"
echo " You can open ios/Runner.xcworkspace in Xcode to"
echo " sign and distribute to App Store or TestFlight."
echo "=================================================="
