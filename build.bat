@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:: ── Parse arguments ────────────────────────────────────────────────────────────
set "CONFIG=Release"
for %%a in (%*) do (
  if /i "%%a"=="--debug" set "CONFIG=Debug"
)
echo Configuration: !CONFIG!

:: ── Locate vswhere ────────────────────────────────────────────────────────────
set "PFX86=%ProgramFiles(x86)%"
set "VSWHERE=%PFX86%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo vswhere not found. Is Visual Studio installed?
  exit /b 1
)

:: ── Locate MSBuild (write to temp file to avoid for/f quoting issues) ─────────
set "TMPOUT=%TEMP%\msbuild_path.tmp"
"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" > "%TMPOUT%" 2>nul
set "MSBUILD="
for /f "usebackq tokens=*" %%i in ("%TMPOUT%") do set "MSBUILD=%%i"
del "%TMPOUT%" 2>nul
if "!MSBUILD!"=="" (
  echo Could not locate MSBuild via vswhere.
  exit /b 1
)
echo MSBuild: !MSBUILD!

:: ── Build React UI ─────────────────────────────────────────────────────────
echo Building React UI...
pushd "%~dp0AOSharp.UI"
call npm ci --prefer-offline 2>&1
if errorlevel 1 (
  echo npm ci failed.
  popd
  exit /b 1
)
call npm run build 2>&1
if errorlevel 1 (
  echo React build failed.
  popd
  exit /b 1
)
popd

:: ── Build managed projects ────────────────────────────────────────────────────
echo Building managed projects...

dotnet build AOSharp\AOSharp.csproj --configuration !CONFIG! --nologo
if errorlevel 1 (
  echo Managed build failed.
  exit /b 1
)

:: ── Build NativeHost (C++ x86) ────────────────────────────────────────────────
echo Building NativeHost ^(C++ x86^)...
"!MSBUILD!" NativeHost\NativeHost.vcxproj /p:Configuration=Release /p:Platform=Win32 /p:ManagedConfig=!CONFIG! /v:minimal /nologo
if errorlevel 1 (
  echo NativeHost build failed.
  exit /b 1
)

:: ── Copy React dist next to exe ────────────────────────────────────────────
set "BINDIR=%~dp0bin\!CONFIG!\net8.0-windows"
set "UIDIST=%~dp0AOSharp.UI\dist"
set "UIDEST=!BINDIR!\ui"
echo Copying React UI to !UIDEST!...
if exist "!UIDEST!" rd /s /q "!UIDEST!"
xcopy /e /i /q "!UIDIST!" "!UIDEST!" >nul
if errorlevel 1 (
  echo Failed to copy React UI.
  exit /b 1
)

echo.
echo Build succeeded.
exit /b 0
