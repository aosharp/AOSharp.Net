@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:: ── Parse arguments ────────────────────────────────────────────────────────────
set "CONFIG=Release"
set "SKIP_UI=0"
for %%a in (%*) do (
  if /i "%%a"=="--debug"   set "CONFIG=Debug"
  if /i "%%a"=="--skip-ui" set "SKIP_UI=1"
)
echo Configuration: !CONFIG!
if "!SKIP_UI!"=="1" echo Skipping React UI build.

:: ── Locate vswhere ────────────────────────────────────────────────────────────
set "PFX86=%ProgramFiles(x86)%"
set "VSWHERE=%PFX86%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo vswhere not found. Is Visual Studio installed?
  exit /b 1
)

:: ── Locate MSBuild (write to temp file to avoid for/f quoting issues) ─────────
set "TMPOUT=%TEMP%\msbuild_path.tmp"
"%VSWHERE%" -latest -products * -prerelease -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" > "%TMPOUT%" 2>nul
set "MSBUILD="
for /f "usebackq tokens=*" %%i in ("%TMPOUT%") do set "MSBUILD=%%i"
del "%TMPOUT%" 2>nul
if "!MSBUILD!"=="" (
  echo Could not locate MSBuild via vswhere.
  exit /b 1
)
echo MSBuild: !MSBUILD!

:: ── Build React UI ─────────────────────────────────────────────────────────
if "!SKIP_UI!"=="1" goto :skip_ui
echo Building React UI...
pushd "%~dp0AOSharp.UI"
call npm ci --prefer-offline 2>&1
if errorlevel 1 (
  echo npm ci failed, falling back to npm install...
  call npm install 2>&1
  if errorlevel 1 (
    echo npm install failed.
    popd
    exit /b 1
  )
)
call npm run build 2>&1
if errorlevel 1 (
  echo React build failed.
  popd
  exit /b 1
)
popd
:skip_ui

:: ── Build managed projects ────────────────────────────────────────────────────
echo Building managed projects...

dotnet build AOSharp\AOSharp.csproj --configuration !CONFIG! --nologo
if errorlevel 1 (
  echo Managed build failed.
  exit /b 1
)

:: ── Read TargetFramework from managed csproj (used for OutDir in NativeHost) ──
set "MANAGED_TFM="
for /f "usebackq delims=" %%i in (`powershell -NoProfile -NonInteractive -Command "([xml](Get-Content '%~dp0AOSharp\AOSharp.csproj')).Project.PropertyGroup.TargetFramework"`) do set "MANAGED_TFM=%%i"
if "!MANAGED_TFM!"=="" (
  echo Could not read TargetFramework from AOSharp\AOSharp.csproj.
  exit /b 1
)
echo ManagedTfm: !MANAGED_TFM!

:: ── Locate .NET x86 host pack (nethost.h / libnethost.lib) — same as NativeHost.vcxproj
set "DOTNET_HOST_PACK_DIR="
for /f "usebackq delims=" %%i in (`powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0NativeHost\ResolveDotNetHostPack.ps1"`) do set "DOTNET_HOST_PACK_DIR=%%i"
if "!DOTNET_HOST_PACK_DIR!"=="" (
  echo Could not resolve the .NET x86 app host. Install a .NET SDK with the win-x86 app host, or set DOTNET_ROOT.
  echo See NativeHost\ResolveDotNetHostPack.ps1.
  exit /b 1
)
echo DotNet Host Pack: !DOTNET_HOST_PACK_DIR!

:: ── Build NativeHost (C++ x86) ────────────────────────────────────────────────
echo Building NativeHost ^(C++ x86^)...
"!MSBUILD!" NativeHost\NativeHost.vcxproj /p:Configuration=Release /p:Platform=Win32 /p:ManagedConfig=!CONFIG! /p:ManagedTfm=!MANAGED_TFM! /p:DotNetHostPackDir="!DOTNET_HOST_PACK_DIR!" /v:minimal /nologo
if errorlevel 1 (
  echo NativeHost build failed.
  exit /b 1
)

:: ── Copy React dist next to exe ────────────────────────────────────────────
if "!SKIP_UI!"=="1" goto :skip_ui_copy
set "BINDIR=%~dp0bin\!CONFIG!\!MANAGED_TFM!"
set "UIDIST=%~dp0AOSharp.UI\dist"
set "UIDEST=!BINDIR!\ui"
echo Copying React UI to !UIDEST!...
if exist "!UIDEST!" rd /s /q "!UIDEST!"
xcopy /e /i /q "!UIDIST!" "!UIDEST!" >nul
if errorlevel 1 (
  echo Failed to copy React UI.
  exit /b 1
)
:skip_ui_copy

echo.
echo Build succeeded.
exit /b 0
