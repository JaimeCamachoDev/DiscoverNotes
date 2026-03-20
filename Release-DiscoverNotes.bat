@echo off
setlocal
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File ".\scripts\Release-DiscoverNotes.ps1" %*
set EXIT_CODE=%ERRORLEVEL%
echo.
if not "%EXIT_CODE%"=="0" (
  echo Release failed with exit code %EXIT_CODE%.
) else (
  echo Release finished successfully.
)
pause
endlocal
