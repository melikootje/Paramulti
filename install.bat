@echo off
setlocal enabledelayedexpansion
title ParalivesMultiplayer Installer

:: ============================================================
:: ParalivesMultiplayer installer
:: Builds the mod and deploys it to the Paralives BepInEx folder
:: ============================================================

set "BUILD_CONFIG=Release"
if not "%~1"=="" set "BUILD_CONFIG=%~1"

set "SCRIPT_DIR=%~dp0"

echo.
echo  ========================================
echo   ParalivesMultiplayer Installer
echo   Build config: %BUILD_CONFIG%
echo  ========================================
echo.

:: --- Find Paralives directory ---
set "PARALIVES_DIR="
if not "%PARALIVES_DIR_ENV%"=="" set "PARALIVES_DIR=%PARALIVES_DIR_ENV%"

if "%PARALIVES_DIR%"=="" (
    for %%d in (
        "%ProgramFiles(x86)%\Steam\steamapps\common\Paralives"
        "C:\Program Files\Steam\steamapps\common\Paralives"
        "C:\SteamLibrary\steamapps\common\Paralives"
        "D:\SteamLibrary\steamapps\common\Paralives"
        "E:\SteamLibrary\steamapps\common\Paralives"
    ) do (
        if exist "%%~d" (
            set "PARALIVES_DIR=%%~d"
            goto :found_game
        )
    )
    echo [!] Paralives directory not found in default Steam locations.
    echo     Set PARALIVES_DIR environment variable and try again:
    echo     set PARALIVES_DIR=C:\your\path\to\Paralives
    pause
    exit /b 1
)
:found_game

set "PLUGINS_DIR=%PARALIVES_DIR%\BepInEx\plugins"

echo [+] Paralives directory: %PARALIVES_DIR%
echo [+] Plugins target:    %PLUGINS_DIR%
echo.

:: --- Check dotnet ---
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [-] dotnet SDK not found in PATH. Install .NET SDK 10.0+ first.
    echo     https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
echo [+] dotnet found:
dotnet --version
echo.

:: --- Check plugins directory ---
if not exist "%PLUGINS_DIR%" (
    echo [!] Plugins directory not found, creating: %PLUGINS_DIR%
    mkdir "%PLUGINS_DIR%" 2>nul
    if %ERRORLEVEL% neq 0 (
        echo [-] Failed to create plugins directory.
        pause
        exit /b 1
    )
)

:: --- Restore ---
echo [+] Restoring NuGet packages...
dotnet restore "%SCRIPT_DIR%src\ParalivesMultiplayer\ParalivesMultiplayer.csproj" --verbosity quiet 2>&1
if %ERRORLEVEL% neq 0 (
    echo [-] NuGet restore failed.
    pause
    exit /b 1
)

:: --- Run tests ---
echo [+] Running unit tests...
cd /d "%SCRIPT_DIR%"
dotnet test "%SCRIPT_DIR%src\ParalivesMultiplayer.Tests\ParalivesMultiplayer.Tests.csproj" --verbosity quiet --no-restore 2>&1
if %ERRORLEVEL% neq 0 (
    echo [-] Tests failed. Fix the issues and try again.
    pause
    exit /b 1
)
echo [+] All tests passed.
echo.

:: --- Build ---
echo [+] Building (%BUILD_CONFIG%)...
dotnet build "%SCRIPT_DIR%src\ParalivesMultiplayer\ParalivesMultiplayer.csproj" -c "%BUILD_CONFIG%" --verbosity quiet --no-restore 2>&1
if %ERRORLEVEL% neq 0 (
    echo [-] Build failed.
    pause
    exit /b 1
)

set "BUILD_DIR=%SCRIPT_DIR%src\ParalivesMultiplayer\bin\%BUILD_CONFIG%\netstandard2.0"

if not exist "%BUILD_DIR%\ParalivesMultiplayer.dll" (
    echo [-] Build output not found at %BUILD_DIR%
    pause
    exit /b 1
)

echo [+] Build succeeded.
echo.

:: --- Backup existing plugin ---
if exist "%PLUGINS_DIR%\ParalivesMultiplayer.dll" (
    echo [+] Backing up existing plugin to ParalivesMultiplayer.dll.bak
    copy /y "%PLUGINS_DIR%\ParalivesMultiplayer.dll" "%PLUGINS_DIR%\ParalivesMultiplayer.dll.bak" >nul
)

:: --- Deploy ---
echo [+] Deploying to %PLUGINS_DIR%
copy /y "%BUILD_DIR%\ParalivesMultiplayer.dll" "%PLUGINS_DIR%\" >nul
copy /y "%BUILD_DIR%\ParalivesMultiplayer.Protocol.dll" "%PLUGINS_DIR%\" >nul

if exist "%BUILD_DIR%\ParalivesMultiplayer.pdb" (
    copy /y "%BUILD_DIR%\ParalivesMultiplayer.pdb" "%PLUGINS_DIR%\" >nul
)

if exist "%BUILD_DIR%\ParalivesMultiplayer.Protocol.pdb" (
    copy /y "%BUILD_DIR%\ParalivesMultiplayer.Protocol.pdb" "%PLUGINS_DIR%\" >nul
)

echo.
echo [+] Deployed files:
dir /b "%PLUGINS_DIR%\ParalivesMultiplayer*" 2>nul | findstr /i "ParalivesMultiplayer"

echo.
echo  ========================================
echo   Done. Launch Paralives to load the mod.
echo   F5 = host   F6 = connect   F7 = disconnect
echo   Check BepInEx\LogOutput.log for status.
echo  ========================================
echo.
pause
endlocal
