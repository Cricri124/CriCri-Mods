@echo off
setlocal
title Desinstalar CRICRI BR
rem Goes into the package as DESINSTALAR-CRICRI-BR.cmd. The same script as the installer, with -Desinstalar.
cd /d "%~dp0"
if not exist "%~dp0INSTALAR-CRICRI-BR.ps1" goto faltam

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALAR-CRICRI-BR.ps1" -Desinstalar %*
set "CODIGO=%ERRORLEVEL%"
echo.
pause
exit /b %CODIGO%

:faltam
echo.
echo   Faltam arquivos do pacote do CRICRI BR. Extraia o zip INTEIRO primeiro.
echo.
pause
exit /b 1
