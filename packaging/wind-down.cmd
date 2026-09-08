@echo off
setlocal
set "WIND_DOWN_CLI=%~dp0WindDown.Cli.ps1"
if "%~1"=="" goto launch
if not "%~2"=="" goto badargs
if /i "%~1"=="launch" goto launch
if /i "%~1"=="open" goto launch
if /i "%~1"=="install" goto install
if /i "%~1"=="repair" goto repair
if /i "%~1"=="uninstall" goto uninstall
if /i "%~1"=="help" goto help
if /i "%~1"=="-h" goto help
if /i "%~1"=="--help" goto help
if /i "%~1"=="/?" goto help
echo Unknown Wind Down command: %~1 1>&2
goto help_error

:launch
start "" "%~dp0Wind Down.exe"
exit /b 0

:install
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& $env:WIND_DOWN_CLI install"
exit /b %ERRORLEVEL%

:repair
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& $env:WIND_DOWN_CLI repair"
exit /b %ERRORLEVEL%

:uninstall
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& $env:WIND_DOWN_CLI uninstall"
exit /b %ERRORLEVEL%

:help
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& $env:WIND_DOWN_CLI help"
exit /b %ERRORLEVEL%

:badargs
echo Wind Down accepts one command at a time. 1>&2
:help_error
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& $env:WIND_DOWN_CLI help"
exit /b 2
