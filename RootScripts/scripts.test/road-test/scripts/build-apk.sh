#!/bin/bash
set -e

echo -e "\033[0;36m=================================================="
echo -e " Building Rider App APK for Real Road Test...     "
echo -e "==================================================\033[0m"

TUNNEL_URL=""
while [ $# -gt 0 ]; do
    case "$1" in
        -TunnelUrl|--tunnel-url|--url|-u)
            TUNNEL_URL="$2"
            shift 2 2>/dev/null || shift 1
            ;;
        *)
            if [ -z "$TUNNEL_URL" ]; then
                TUNNEL_URL="$1"
            fi
            shift
            ;;
    esac
done

if [ -z "$TUNNEL_URL" ]; then
    read -p "Enter Server Public URL (Optional, press Enter to configure in-app): " TUNNEL_URL
fi

# Trim trailing slash
TUNNEL_URL="${TUNNEL_URL%/}"

if [ -n "$TUNNEL_URL" ] && [[ ! "$TUNNEL_URL" =~ ^https?:// ]]; then
    echo -e "\033[0;31m\n[ERROR] Invalid Server Public URL: '$TUNNEL_URL'\033[0m"
    echo -e "\033[0;33mThe URL must start with http:// or https:// (e.g. https://xxxx.trycloudflare.com)\033[0m"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RIDER_APP_DIR="$(cd "$SCRIPT_DIR/../../../../rider_app" && pwd)"

if [ -n "$TUNNEL_URL" ]; then
    echo -e "\n\033[0;33mTarget Server Base URL (Pre-configured): $TUNNEL_URL\033[0m"
else
    echo -e "\n\033[0;33mTarget Server Base URL: Configurable inside app via Server Settings\033[0m"
fi

cd "$RIDER_APP_DIR"

echo -e "\n\033[0;36m--> Fetching Flutter dependencies...\033[0m"
flutter pub get

echo -e "\n\033[0;36m--> Compiling Android Release APK...\033[0m"
if [ -n "$TUNNEL_URL" ]; then
    flutter build apk --release --android-skip-build-dependency-validation --dart-define=API_BASE_URL="$TUNNEL_URL"
else
    flutter build apk --release --android-skip-build-dependency-validation
fi

echo -e "\n\033[0;32m=================================================="
echo -e " ✅ APK Build Completed Successfully!"
echo -e "==================================================\033[0m"
echo -e "\033[1;37m📁 APK Output Path:\033[0m"
echo -e "   \033[0;33m$RIDER_APP_DIR/build/app/outputs/flutter-apk/app-release.apk\033[0m\n"
echo -e "\033[1;37m📲 Next Steps for Real Phone Testing:\033[0m"
echo -e "   1. Send APK to the test phone (via USB, Drive, or LINE/Chat)"
echo -e "   2. Install and Grant 'Allow all the time' location permission"
echo -e "   3. Follow test cases in road-test/docs/03-gps-test.md\n"
