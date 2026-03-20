@echo off
setlocal
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File ".\scripts\Start-UpmRelease.ps1" -ManualSign %*
set EXIT_CODE=%ERRORLEVEL%
echo.
if not "%EXIT_CODE%"=="0" (
  echo Release launcher failed with exit code %EXIT_CODE%.
) else (
  echo Release launcher finished successfully.
)
pause
endlocal
