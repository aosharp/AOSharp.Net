@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:: ── Locate vswhere ────────────────────────────────────────────────────────────
set "PFX86=%ProgramFiles(x86)%"
set "VSWHERE=%PFX86%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo vswhere not found. Is Visual Studio installed?
  exit /b 1
)

:: ── Locate MSBuild (write to temp file to avoid for/f quoting issues) ─────────
set "TMPOUT=%TEMP%\msbuild_path.tmp"
"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" > "%TMPOUT%" 2>nul
set "MSBUILD="
for /f "usebackq tokens=*" %%i in ("%TMPOUT%") do set "MSBUILD=%%i"
del "%TMPOUT%" 2>nul
if "!MSBUILD!"=="" (
  echo Could not locate MSBuild via vswhere.
  exit /b 1
)
echo MSBuild: !MSBUILD!

:: ── Build managed projects ────────────────────────────────────────────────────
echo Building managed projects...
REM NativeHost is built separately with MSBuild (not in the .sln so dotnet CLI can build).

dotnet build AOSharp.Loader.sln --configuration Debug
if errorlevel 1 (
  echo Managed build failed.
  exit /b 1
)

:: ── Build NativeHost (C++ x86) ────────────────────────────────────────────────
echo Building NativeHost ^(C++ x86^)...
"!MSBUILD!" NativeHost\NativeHost.vcxproj /p:Configuration=Debug /p:Platform=Win32 /v:minimal /nologo
if errorlevel 1 (
  echo NativeHost build failed.
  exit /b 1
)

echo.
echo Build succeeded.
exit /b 0
