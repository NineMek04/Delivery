#!/bin/bash
# OSRM Offline Map Builder and Setup Helper v1.0
set -e

echo -e "\n\033[36m============================================================"
echo -e " 🗺️  OSRM Offline Map Compiler & Setup Helper v1.0"
echo -e "============================================================\033[0m\n"

DATA_DIR="./osrm_data"
if [ ! -d "$DATA_DIR" ]; then
    echo "📁 Creating OSRM Data Directory: $DATA_DIR ..."
    mkdir -p "$DATA_DIR"
fi

PBF_FILE="$DATA_DIR/udon-thani.osm.pbf"
if [ ! -f "$PBF_FILE" ]; then
    echo -e "\033[33m🌐 Downloading Thailand OpenStreetMap data (~180MB) from Geofabrik...\033[0m"
    curl -L -o "$PBF_FILE" https://download.geofabrik.de/asia/thailand-latest.osm.pbf
    echo -e "\033[32m✅ Download completed successfully!\033[0m"
else
    echo -e "\033[32m📦 Raw map data detected. Skipping download...\033[0m"
fi

echo -e "\n\033[33m🚀 Starting OSRM Toolchain compilation via Docker...\033[0m"

echo -e "\n\033[36m[1/3] Extracting road networks (osrm-extract)...\033[0m"
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-extract -p /usr/local/share/osrm/profiles/car.lua /data/udon-thani.osm.pbf

echo -e "\n\033[36m[2/3] Partitioning street cells (osrm-partition)...\033[0m"
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-partition /data/udon-thani.osrm

echo -e "\n\033[36m[3/3] Customizing street route metrics (osrm-customize)...\033[0m"
docker run --rm --user root -v "$(pwd)/osrm_data:/data" osrm/osrm-backend osrm-customize /data/udon-thani.osrm

echo -e "\n\033[32m============================================================"
echo -e " 🎉 OSRM OFFLINE COMPILATION COMPLETED SUCCESSFULLY!"
echo -e "============================================================\033[0m"
echo "Offline map files compiled inside: $DATA_DIR"
echo -e "Next step: restart your osrm container to launch offline speed:"
echo -e "\033[36m   docker-compose restart osrm\033[0m\n"
