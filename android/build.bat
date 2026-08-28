@echo off
if exist "C:\Program Files\Microsoft\jdk-17.0.19.10-hotspot" (
    set "JAVA_HOME=C:\Program Files\Microsoft\jdk-17.0.19.10-hotspot"
) else if exist "C:\Program Files\Eclipse Adoptium\jdk-17.0.17.10-hotspot" (
    set "JAVA_HOME=C:\Program Files\Eclipse Adoptium\jdk-17.0.17.10-hotspot"
)
set "PATH=%JAVA_HOME%\bin;%PATH%"

echo ===================================================
echo [FImageStack] Starting Android Build Process...
echo ===================================================
call gradlew.bat assembleDebug
if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===================================================
    echo [SUCCESS] Build finished successfully!
    echo APK Location: app\build\outputs\apk\debug\app-debug.apk
    echo ===================================================
) else (
    echo.
    echo ===================================================
    echo [ERROR] Build failed. Please check errors above.
    echo ===================================================
)
pause
