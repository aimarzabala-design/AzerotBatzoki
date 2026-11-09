@echo off
setlocal
set "LAUNCHER=%~dp0LaunchWoW\LaunchWoW.exe"
if not exist "%LAUNCHER%" (
  echo ERROR: No se encontro el launcher de escritorio:
  echo "%LAUNCHER%"
  echo Ejecuta LaunchWoW\Compilar.ps1 para compilarlo.
  pause
  exit /b 1
)
start "" "%LAUNCHER%"
