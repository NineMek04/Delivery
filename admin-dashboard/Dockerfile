# ============================================
# Admin Dashboard — Angular 19 + Nginx
# ============================================

# Stage 1: Build Angular app
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --legacy-peer-deps

COPY . ./
RUN npm run build

# Stage 2: Serve with Nginx
FROM nginx:alpine AS runtime

# Angular 19 application builder outputs to dist/<project>/browser
COPY --from=build /app/dist/admin-dashboard/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
