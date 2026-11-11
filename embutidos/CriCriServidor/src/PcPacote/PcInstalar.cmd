@echo off
setlocal
title Instalar CRICRI BR
rem Goes into the package as INSTALAR-CRICRI-BR.cmd. ASCII only; no paths inside if ( ... ) blocks, the ")" of "Program Files (x86)" would close them.
rem -ExecutionPolicy Bypass applies to this one process only; nothing here asks for administrator.
cd /d "%~dp0"
if not exist "%~dp0INSTALAR-CRICRI-BR.ps1" goto faltam
if not exist "%~dp0jogo\BepInEx\plugins\CriCriServidor.dll" goto faltam

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALAR-CRICRI-BR.ps1" %*
set "CODIGO=%ERRORLEVEL%"
echo.
pause
exit /b %CODIGO%

:faltam
echo.
echo   Faltam arquivos do pacote do CRICRI BR.
echo   Voce abriu o instalador de DENTRO do zip? Extraia o zip INTEIRO primeiro:
echo   botao direito no zip - Extrair tudo - abra a pasta extraida - dois cliques neste arquivo.
echo.
pause
exit /b 1
