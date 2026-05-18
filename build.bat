@echo off
setlocal enabledelayedexpansion

REM ==============================================================
REM  StS2 mod build and deploy
REM  Usage: build.bat [--no-deploy]
REM  Default: build + pack .pck + copy to game mods folder
REM ==============================================================

set PROJ_DIR=%~dp0StS2Mod
set MOD_NAME=ChargeDrawMod
set GAME_MODS=D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods

echo [1/4] Cleaning previous build...
rmdir /s /q "%PROJ_DIR%\bin" 2>nul
rmdir /s /q "%PROJ_DIR%\obj" 2>nul

echo [2/4] Building C# project (net9.0)...
dotnet build "%PROJ_DIR%\%MOD_NAME%.csproj" -c Debug --nologo -v q
if errorlevel 1 (
    echo [FAIL] Build failed
    exit /b %errorlevel%
)

for /r "%PROJ_DIR%\bin" %%f in (%MOD_NAME%.dll) do set DLL_PATH=%%f
if not defined DLL_PATH (
    echo [FAIL] %MOD_NAME%.dll not found
    exit /b 1
)
echo   -> %DLL_PATH%

echo [3/4] Packing .pck...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\pck_builder.ps1" "%PROJ_DIR%\%MOD_NAME%.pck" "%PROJ_DIR%\mod_manifest.json"
if errorlevel 1 (
    echo [FAIL] PCK packing failed
    exit /b %errorlevel%
)

if "%1"=="--no-deploy" (
    echo [4/4] Skipping deploy (--no-deploy)
    goto :done
)

echo [4/4] Deploying to game mods directory...
if not exist "%GAME_MODS%" (
    echo [FAIL] Game mods directory not found: %GAME_MODS%
    exit /b 1
)
copy /y "%DLL_PATH%" "%GAME_MODS%\%MOD_NAME%.dll" >nul
copy /y "%PROJ_DIR%\%MOD_NAME%.pck" "%GAME_MODS%\%MOD_NAME%.pck" >nul
if exist "%PROJ_DIR%\%MOD_NAME%.json" (
    copy /y "%PROJ_DIR%\%MOD_NAME%.json" "%GAME_MODS%\%MOD_NAME%.json" >nul
)
echo   -> %GAME_MODS%\%MOD_NAME%.dll
echo   -> %GAME_MODS%\%MOD_NAME%.pck
if exist "%GAME_MODS%\%MOD_NAME%.json" echo   -> %GAME_MODS%\%MOD_NAME%.json

:done
echo.
echo [OK] Build complete.
endlocal
