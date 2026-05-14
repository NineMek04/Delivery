# Delivery

วิธ๊ใช้ OpenAPI Generator
# 1. เปิด Backend ก่อน (VS2022 หรือ dotnet run)
# 2. จากนั้นรัน:
    cd admin-dashboard
    npm install   # (ครั้งแรก เพื่อลง openapi-generator-cli)
    npm run generate:api


# rider_app
        # NOTE
        # Package versions อาจต้องปรับตาม compatibility กับ Dart SDK 3.9 ณ เวลา install จริง — จะใช้ flutter pub add ทีละตัวเพื่อให้ได้ version ที่ compatible
        # ต้องทำต่อ (Next Steps)
            # ลง Flutter SDK แล้วรัน flutter pub get
            # รัน code generation: dart run build_runner build --delete-conflicting-outputs
            # สร้าง .freezed.dart + .g.dart สำหรับ models + providers
            # รัน flutter analyze เพื่อตรวจ code quality
            # Implement UI จริง ใน feature screens (แทน placeholder)
            # เชื่อม BackendApi — ใส่ URL จริง, implement login flow, test SignalR