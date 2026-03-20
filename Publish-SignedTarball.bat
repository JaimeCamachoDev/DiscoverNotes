@echo off
setlocal
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File ".\scripts\Publish-SignedTarball.ps1" -LoginIfNeeded %*
set EXIT_CODE=%ERRORLEVEL%
echo.
if not "%EXIT_CODE%"=="0" (
  echo Publish failed with exit code %EXIT_CODE%.
) else (
  echo Publish finished successfully.
)
pause
endlocal
