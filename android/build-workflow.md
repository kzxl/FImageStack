---
desc: Quy trình build và xuất file APK ứng dụng FImageStack Pro Macro trên Android
---

### 1. Build file APK Android (1-Click)
// turbo
```powershell
# Chạy script build.bat trong thư mục android/
cd "e:\15. Other\FStack\android"
.\build.bat
```

### 2. Cài đặt trực tiếp vào điện thoại qua ADB
// turbo
```powershell
D:\AndroidSDK\platform-tools\adb.exe install -r "app\build\outputs\apk\debug\app-debug.apk"
```

### 3. Vị trí file APK xuất xưởng
* **Debug APK:** `android/app/build/outputs/apk/debug/app-debug.apk`
