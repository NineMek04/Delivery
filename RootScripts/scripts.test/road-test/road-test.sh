#!/bin/bash
# Road Test Master Control Center for Linux/macOS Bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_FOLDER="$SCRIPT_DIR/scripts"

show_header() {
    clear
    echo "=========================================================="
    echo "   🛵 ROAD TEST MASTER CONTROL CENTER                    "
    echo "   Workspace: RootScripts/scripts.test/road-test/              "
    echo "=========================================================="
}

run_start() {
    echo -e "\n[Action] Starting Road Test Server..."
    bash "$SCRIPTS_FOLDER/start-test.sh"
}

run_health() {
    echo -e "\n[Action] Running System Health Checks..."
    bash "$SCRIPTS_FOLDER/health-check.sh"
}

run_tunnel() {
    echo -e "\n[Action] Starting Cloudflare Tunnel via Docker..."
    echo -e "NOTE: Keep this terminal running to maintain public connectivity.\n"
    docker run --rm -it --network=host cloudflare/cloudflared:latest tunnel --url http://localhost:80
}

run_build_apk() {
    local url="$1"
    echo -e "\n[Action] Building Rider App Release APK..."
    bash "$SCRIPTS_FOLDER/build-apk.sh" "$url"
}

run_reset() {
    echo -e "\n[Action] Resetting Road Test Data..."
    bash "$SCRIPTS_FOLDER/reset-test-data.sh"
}

run_stop() {
    echo -e "\n[Action] Stopping Road Test Server..."
    bash "$SCRIPTS_FOLDER/stop-test.sh"
}

run_workflow() {
    show_header
    echo -e "\n[Workflow] Step 1/3: Starting Server Environment..."
    run_start

    echo -e "\nWaiting 10 seconds for service stabilization..."
    sleep 10

    echo -e "\n[Workflow] Step 2/3: Checking Services Health..."
    run_health

    echo -e "\n----------------------------------------------------------"
    echo -e " Next Step (Step 3/3): Start Tunnel and Build APK"
    echo -e " 1. Run 'bash RootScripts/scripts.test/road-test/road-test.sh tunnel' in a separate terminal"
    echo -e " 2. Copy the generated https://xxxx.trycloudflare.com URL"
    echo -e " 3. Run 'bash RootScripts/scripts.test/road-test/road-test.sh build <URL>'"
    echo -e "----------------------------------------------------------\n"
}

ACTION="${1:-menu}"
shift 2>/dev/null || true

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

case "$ACTION" in
    start)    run_start; exit 0 ;;
    health)   run_health; exit 0 ;;
    tunnel)   run_tunnel; exit 0 ;;
    build)    run_build_apk "$TUNNEL_URL"; exit 0 ;;
    reset)    run_reset; exit 0 ;;
    stop)     run_stop; exit 0 ;;
    workflow) run_workflow; exit 0 ;;
esac

while true; do
    show_header
    echo " [1] Start Server Environment          (start-test)"
    echo " [2] Run Health Checks                 (health-check)"
    echo " [3] Open Cloudflare Tunnel via Docker (tunnel)"
    echo " [4] Build Release APK                 (build-apk)"
    echo " [5] Reset Test Telemetry & Cache      (reset-test-data)"
    echo " [6] Stop Server Environment           (stop-test)"
    echo " [7] Guided Quick-Start Workflow       (start -> health -> guide)"
    echo " [0] Exit"
    echo "=========================================================="
    read -p " Select an option (0-7): " choice

    case "$choice" in
        1) run_start; read -p "Press Enter to continue..." ;;
        2) run_health; read -p "Press Enter to continue..." ;;
        3) run_tunnel; read -p "Press Enter to continue..." ;;
        4) run_build_apk ""; read -p "Press Enter to continue..." ;;
        5) run_reset; read -p "Press Enter to continue..." ;;
        6) run_stop; read -p "Press Enter to continue..." ;;
        7) run_workflow; read -p "Press Enter to continue..." ;;
        0) echo -e "\nExiting Road Test Control Center.\n"; exit 0 ;;
        *) echo -e "\nInvalid option, please try again."; sleep 1 ;;
    esac
done
