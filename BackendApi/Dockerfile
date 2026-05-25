# ============================================
# BackendApi — .NET 8 Multi-stage Build
# ============================================

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . ./
ENV Jwt__Key=ThisIsADummyKeyForTestingPurposesMustBeAtLeast32Bytes!
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=build /app/publish .

# .NET 8 ใช้ port 8080 by default, แต่ docker-compose map 5000:80
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "BackendApi.dll"]
