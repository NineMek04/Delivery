# Rider App — Flutter Web + Nginx (API/SignalR proxy to backend)
FROM ghcr.io/cirruslabs/flutter:stable AS build

WORKDIR /app

COPY pubspec.* ./
RUN flutter pub get

COPY . .

RUN flutter create . --platforms web

# Same-origin API via nginx in production image (API_BASE_URL empty)
ARG API_BASE_URL=
RUN flutter build web --release \
    --pwa-strategy=none \
    --dart-define=API_BASE_URL=${API_BASE_URL} \
    --dart-define=HTTP_LOGGING=false \
    --dart-define=ENABLE_MOCK_GPS=true

FROM nginx:alpine

COPY --from=build /app/build/web /usr/share/nginx/html
COPY nginx/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
