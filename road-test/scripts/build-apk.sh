#!/bin/bash
set -e

echo -e "\033[0;36m=================================================="
echo -e " Building Rider App APK for Real Road Test...     "
echo -e "==================================================\033[0m"

TUNNEL_URL="$1"

if [ -z "$TUNNEL_URL" ]; then
    read -p "Enter Server Public URL (e.g. https://xxxx.trycloudflare.com): " TUNNEL_URL
fi

if [ -z "$TUNNEL_URL" ]; then
    echo -e "\033[0;31m\n[ERROR] Server Public URL is required to build the APK.\033[0m"
    echo -e "\033[0;33mUsage: bash road-test/scripts/build-apk.sh 'https://xxxx.trycloudflare.com'\n\033[0m"
    exit 1
fi

# Trim trailing slash
TUNNEL_URL="${TUNNEL_URL%/}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RIDER_APP_DIR="$SCRIPT_DIR/../../rider_app"

echo -e "\n\033[0;33mTarget Server Base URL: $TUNNEL_URL\033[0m"
cd "$RIDER_APP_DIR"

echo -e "\n\033[0;36m--> Fetching Flutter dependencies...\033[0m"
flutter pub get

echo -e "\n\033[0;36m--> Compiling Android Release APK...\033[0m"
flutter build apk --release --android-skip-build-dependency-validation --dart-define=API_BASE_URL="$TUNNEL_URL"

echo -e "\n\033[0;32m=================================================="
echo -e " ✅ APK Build Completed Successfully!"
echo -e "==================================================\033[0m"
echo -e "\033[1;37m📁 APK Output Path:\033[0m"
echo -e "   \033[0;33m$RIDER_APP_DIR/build/app/outputs/flutter-apk/app-release.apk\033[0m\n"
echo -e "\033[1;37m📲 Next Steps for Real Phone Testing:\033[0m"
echo -e "   1. Send APK to the test phone (via USB, Drive, or LINE/Chat)"
echo -e "   2. Install and Grant 'Allow all the time' location permission"
echo -e "   3. Follow test cases in road-test/docs/03-gps-test.md\n"
