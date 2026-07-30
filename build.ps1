# Compila o Monitor de VRAM com o csc do .NET Framework 4 (sem SDK, sem dependencias).
# Uso:  .\build.ps1          -> gera VramMonitor.exe
#       .\build.ps1 -Run     -> compila e executa

param([switch]$Run)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe do .NET Framework 4 nao encontrado.'
}

$out = Join-Path $root 'VramMonitor.exe'
$manifest = Join-Path $root 'app.manifest'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Management.dll'
)

$args = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/debug-',
    '/langversion:5',
    "/out:$out",
    "/win32manifest:$manifest"
) + $refs + $sources

Write-Host "Compilando -> $out" -ForegroundColor Cyan
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "csc falhou com codigo $LASTEXITCODE" }

$size = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "OK: VramMonitor.exe ($size KB)" -ForegroundColor Green

if ($Run) { Start-Process $out }
