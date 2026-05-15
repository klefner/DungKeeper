@echo off
echo Syncing DungKeeper assets to Unity project...

set UNITY_PROJECT=%USERPROFILE%\DungKeeper
set ASSETS_SOURCE=%~dp0..\Assets

if not exist "%UNITY_PROJECT%\Assets" (
    echo ERROR: Unity project not found at %UNITY_PROJECT%
    pause
    exit /b 1
)

xcopy /E /Y /I "%ASSETS_SOURCE%\*" "%UNITY_PROJECT%\Assets\"

echo.
echo Done! Switch to Unity - it will detect the new files and recompile automatically.
pause
