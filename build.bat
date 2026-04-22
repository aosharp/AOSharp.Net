@echo off
setlocal
cd /d "%~dp0"
echo Building AOSharp.sln...
dotnet build AOSharp.sln
if %ERRORLEVEL% neq 0 (
  echo Build failed with error %ERRORLEVEL%
  exit /b %ERRORLEVEL%
)
echo Build succeeded.
exit /b 0
