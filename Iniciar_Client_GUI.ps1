$launcher = Join-Path $PSScriptRoot 'LaunchWoW\AzerotBatzoki.exe'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw "No se encontró el launcher de escritorio: $launcher. Ejecuta LaunchWoW\Compilar.ps1."
}
Start-Process -FilePath $launcher
