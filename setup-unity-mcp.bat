@echo off
setlocal
cd /d "%~dp0"
set "LOG=%CD%\setup-unity-mcp.log"
echo Unity MCP setup %DATE% %TIME% > "%LOG%"

echo [1/6] Checking Unity CLI...
where unity >nul 2>nul
if errorlevel 1 (
  echo Unity CLI not found on PATH.
  echo Unity CLI not found on PATH. >> "%LOG%"
  echo Installing from the official Unity CDN...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex"
  echo.
  echo Install finished. Close this window and double-click this file again so PATH is refreshed.
  pause
  exit /b 0
)
echo Location(s) of unity on PATH - more than one line means duplicate installs:
powershell -NoProfile -Command "where.exe unity 2>&1 | Tee-Object -FilePath '%LOG%' -Append"
powershell -NoProfile -Command "unity --version 2>&1 | Tee-Object -FilePath '%LOG%' -Append"

echo.
echo [2/6] Checking for a newer Unity CLI...
powershell -NoProfile -Command "unity self-update --check 2>&1 | Tee-Object -FilePath '%LOG%' -Append"
echo.
set "ANS="
set /p "ANS=Update the Unity CLI now (beta channel)? [Y/N]: "
if /i "%ANS%"=="Y" (
  powershell -NoProfile -Command "unity self-update --yes --channel beta 2>&1 | Tee-Object -FilePath '%LOG%' -Append"
  echo.
  echo Update step finished. Close this window and double-click this file again to continue with the new version.
  echo If the update refused because of how the CLI was installed, copy the message above and send it to Claude.
  pause
  exit /b 0
)

echo.
echo [3/6] Registering Unity MCP server for the Claude app...
powershell -NoProfile -Command "unity mcp configure claude --project-path '%CD%' --yes 2>&1 | Tee-Object -FilePath '%LOG%' -Append"

echo.
echo [4/6] Checking the Pipeline package and Safe Mode - the Editor must be open for this project...
powershell -NoProfile -Command "unity pipeline list 2>&1 | Tee-Object -FilePath '%LOG%' -Append"

echo.
echo [5/6] Checking Editor connection...
powershell -NoProfile -Command "unity status 2>&1 | Tee-Object -FilePath '%LOG%' -Append"

echo.
echo [6/6] Showing which AI clients have the Unity MCP server registered...
powershell -NoProfile -Command "unity mcp configure --list 2>&1 | Tee-Object -FilePath '%LOG%' -Append"

echo.
echo NOTE: fully quit the Claude app - right-click its tray icon and choose Quit - then reopen it so it loads the Unity MCP server.
echo Done. A copy of this output was saved to setup-unity-mcp.log
pause
