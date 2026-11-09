$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'No se encontró el compilador de .NET Framework. Instala .NET Framework 4.8 Developer Pack.'
}

$sources = @(
    (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'Models.cs'),
    (Join-Path $PSScriptRoot 'LauncherServer.cs'),
    (Join-Path $PSScriptRoot 'LauncherForm.cs')
)
$output = Join-Path $PSScriptRoot 'LaunchWoW.exe'
$references = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.Security.dll',
    '/reference:System.Windows.Forms.dll'
)
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', ('/out:' + $output)) + $references + $sources
& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación C# (código $LASTEXITCODE)."
}

& $output --self-test
if ($LASTEXITCODE -ne 0) {
    throw "Fallaron las comprobaciones internas (código $LASTEXITCODE)."
}
Write-Output "Compilación correcta: $output"
