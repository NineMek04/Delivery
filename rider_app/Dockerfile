# Rider App — Flutter Web + Nginx (API/SignalR proxy to backend)
FROM ghcr.io/cirruslabs/flutter:stable AS build

WORKDIR /app

COPY pubspec.* ./
RUN flutter pub get

COPY . .

RUN flutter create . --platforms web

# Generate Isar database schemas (.g.dart files)
RUN dart run build_runner build --delete-conflicting-outputs
# Patch Isar 64-bit hash for dart2js compatibility (replace with a 53-bit safe integer)
RUN sed -i 's/-7637150241870062665/-763715024187006/g' lib/models/gps_point.g.dart

# Same-origin API via nginx in production image (API_BASE_URL empty)
ARG API_BASE_URL=
RUN flutter build web --release \
    --pwa-strategy=none \
    --dart-define=API_BASE_URL=${API_BASE_URL} \
    --dart-define=HTTP_LOGGING=false

FROM nginx:alpine

COPY --from=build /app/build/web /usr/share/nginx/html
COPY nginx/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
