@echo off
setlocal
echo.
echo  VeriPDF - building the Windows app
echo  ================================
echo.

where node >nul 2>nul || (echo  [X] Node.js is required. Install it from https://nodejs.org & pause & exit /b 1)

echo  [1/3] Installing dependencies...
call npm install || goto :fail

echo  [2/3] Bundling the app...
call npm run bundle || goto :fail

echo  [3/3] Building the installer...
call npm run dist || goto :fail

echo.
echo  Done. The installer is in the release folder:
echo    release\VeriPDF-1.0.0-x64-Setup.exe        (installer)
echo    release\VeriPDF-1.0.0-portable-win-x64.exe   (no install needed)
echo.
pause
exit /b 0

:fail
echo.
echo  Build failed. Scroll up for the error.
pause
exit /b 1
