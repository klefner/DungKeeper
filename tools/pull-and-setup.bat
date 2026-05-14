@echo off
setlocal EnableDelayedExpansion

echo ============================================================
echo  DungKeeper Scene Setup
echo ============================================================
echo.

:: ── Step 1: Pull latest from git ─────────────────────────────────────────────

echo [1/3] Pulling latest changes from git...
cd /d "%~dp0\.."
git pull origin claude/review-last-chat-bi5DW
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: git pull failed. Make sure you have git installed and
    echo        the DungKeeper repository cloned on this machine.
    pause
    exit /b 1
)
echo Done.
echo.

:: ── Step 2: Find Unity Editor ────────────────────────────────────────────────

echo [2/3] Locating Unity Editor...

set UNITY_EXE=

:: Check Unity Hub installs (most common location)
for /d %%V in ("%PROGRAMFILES%\Unity\Hub\Editor\*") do (
    if exist "%%V\Editor\Unity.exe" (
        set UNITY_EXE=%%V\Editor\Unity.exe
    )
)

:: Fallback: plain Unity installs
if not defined UNITY_EXE (
    for /d %%V in ("%PROGRAMFILES%\Unity*") do (
        if exist "%%V\Editor\Unity.exe" (
            set UNITY_EXE=%%V\Editor\Unity.exe
        )
    )
)

if not defined UNITY_EXE (
    echo.
    echo ERROR: Could not find Unity.exe automatically.
    echo        Please open Unity Hub manually, open the DungKeeper project,
    echo        then click: DungKeeper ^> Setup Scene  (or press Ctrl+Shift+S)
    pause
    exit /b 1
)

echo Found: !UNITY_EXE!
echo.

:: ── Step 3: Open project in Unity ────────────────────────────────────────────

echo [3/3] Opening DungKeeper in Unity...
echo        When Unity finishes loading, click:
echo          DungKeeper ^> Setup Scene
echo        (or press Ctrl+Shift+S)
echo.

set PROJECT_PATH=%~dp0\..
start "" "!UNITY_EXE!" -projectPath "%PROJECT_PATH%"

echo Unity is launching. This window can be closed.
echo.
pause
