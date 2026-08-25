@echo off
setlocal
cd /d "%~dp0"

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" ^
  -NoLogo ^
  -NoProfile ^
  -ExecutionPolicy Bypass ^
  -File "%~dp0tools\launch-fde-tutor.ps1" %*

set "launcherExitCode=%ERRORLEVEL%"
if not "%launcherExitCode%"=="0" (
  echo.
  echo FDE Tutor did not start successfully.
  echo Review the error above, then press any key to close this window.
  pause >nul
)

exit /b %launcherExitCode%
