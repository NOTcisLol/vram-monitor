# Publica um release no GitHub a partir da versão declarada em src/Version.cs.
#
# Fluxo esperado ao lançar uma versão nova:
#   1. altere AppInfo.Version em src/Version.cs
#   2. escreva a seção "## [X.Y.Z] - data" no CHANGELOG.md
#   3. commit e push
#   4. .\release.ps1
#
# Uso:  .\release.ps1            publica
#       .\release.ps1 -DryRun    só mostra o que faria

param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# ---------------------------------------------------------------- versão
$versionFile = Join-Path $root 'src\Version.cs'
$src = Get-Content $versionFile -Raw
$m = [regex]::Match($src, 'Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"')
if (-not $m.Success) { throw "Nao achei AppInfo.Version em $versionFile" }
$version = $m.Groups[1].Value
$tag = "v$version"
Write-Host "versao: $version" -ForegroundColor Cyan

# ---------------------------------------------------------------- changelog
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$section = [regex]::Match($changelog, "(?ms)^##\s*\[$([regex]::Escape($version))\].*?(?=^##\s*\[|\z)")
if (-not $section.Success) {
    throw "CHANGELOG.md nao tem a secao '## [$version]'. Escreva as mudancas antes de lancar."
}
$body = ($section.Value -replace "(?m)^##\s*\[$([regex]::Escape($version))\][^\r\n]*", '').Trim()
if ([string]::IsNullOrWhiteSpace($body)) { throw "A secao [$version] do CHANGELOG esta vazia." }

# ---------------------------------------------------------------- checagens
$gh = 'C:\Program Files\GitHub CLI\gh.exe'
if (-not (Test-Path $gh)) {
    $c = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $c) { throw 'GitHub CLI (gh) nao encontrado.' }
    $gh = $c.Source
}

$dirty = git status --porcelain
if ($dirty) {
    Write-Host 'AVISO: ha alteracoes nao commitadas:' -ForegroundColor Yellow
    Write-Host $dirty
    if (-not $DryRun) { throw 'Commit e push antes de lancar, senao a tag aponta para o codigo errado.' }
}

$existing = & $gh release view $tag --json tagName 2>$null
if ($LASTEXITCODE -eq 0) { throw "O release $tag ja existe. Suba a versao em src/Version.cs." }

# ---------------------------------------------------------------- build
& (Join-Path $root 'build.ps1')
$exe = Join-Path $root 'VramMonitor.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$sizeKb = [math]::Round((Get-Item $exe).Length / 1KB, 1)

$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
if ($fileVersion -notlike "$version*") {
    throw "O executavel reporta FileVersion '$fileVersion', esperado '$version'. Recompile."
}

# ---------------------------------------------------------------- notas
$repo = 'https://github.com/NOTcisLol/vram-monitor'
$notes = @"
## Baixar

**[VramMonitor.exe]($repo/releases/latest/download/VramMonitor.exe)** — $sizeKb KB, arquivo unico, sem instalador e sem dependencias.

Na primeira execucao o SmartScreen pode avisar que o publicador e desconhecido (o executavel nao e assinado digitalmente): *Mais informacoes* -> *Executar assim mesmo*. Quem preferir nao confiar no binario compila o proprio em segundos com ``powershell -ExecutionPolicy Bypass -File build.ps1`` — usa o compilador que ja vem no Windows.

SHA-256 (verifique com ``Get-FileHash VramMonitor.exe -Algorithm SHA256``):

``````
$hash
``````

$body

## Requisitos

Windows 10 1709+ ou Windows 11 com driver WDDM 2.x — e quando os contadores ``GPU Process Memory`` passaram a existir.
"@

$notesFile = Join-Path $env:TEMP "vram-release-$version.md"
[System.IO.File]::WriteAllText($notesFile, $notes, (New-Object System.Text.UTF8Encoding($false)))

if ($DryRun) {
    Write-Host "`n--- DRY RUN: release $tag ---" -ForegroundColor Yellow
    Write-Host "exe: $sizeKb KB  ·  FileVersion $fileVersion"
    Write-Host "sha256: $hash`n"
    Write-Host $notes
    return
}

& $gh release create $tag $exe --title "$tag - Monitor de VRAM" --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { throw "gh release create falhou ($LASTEXITCODE)" }

Remove-Item $notesFile -ErrorAction SilentlyContinue
Write-Host "`nrelease $tag publicado" -ForegroundColor Green
Write-Host "$repo/releases/latest/download/VramMonitor.exe"
