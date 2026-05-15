# ============================================
# Rider App — Flutter Web Build
# ============================================

# Stage 1: Build Environment
# ใช้ Image ที่มี Flutter SDK มาแล้วเพื่อความเร็ว
FROM ghcr.io/cirruslabs/flutter:stable AS build

# Set working directory
WORKDIR /app

# Copy pubspec files first for better caching
COPY pubspec.* ./
RUN flutter pub get

# Copy the rest of the app code
COPY . .

# Ensure the project is configured for web
RUN flutter create . --platforms web

# Generate code (Riverpod, Freezed, etc.)
RUN flutter pub run build_runner build --delete-conflicting-outputs || true

# Build for Web in release mode
RUN flutter build web --release

# Stage 2: Production Environment (Nginx)
FROM nginx:alpine

# Copy built assets from Stage 1 to Nginx default public directory
COPY --from=build /app/build/web /usr/share/nginx/html

# Expose port 80 (internal to container)
EXPOSE 80

# Start Nginx
CMD ["nginx", "-g", "daemon off;"]
