<#
.SYNOPSIS
  CST Avalonia Windows packaging script (#403).

.DESCRIPTION
  Publishes a self-contained win-x64 build, stages the bundled resources (xsl + dictionaries) beside the
  executable so a fresh machine can seed them, then produces:
    - a portable .zip
    - an InnoSetup installer (setup.exe), if the InnoSetup compiler (ISCC.exe) is on PATH or installed
  Both land in dist/. Distribution is via GitHub Releases + WinGet (fsnow.CSTReader); unsigned for beta. (#28)

.PARAMETER Arch
  Target architecture. Only x64 is supported for now (ARM64 deferred, #28).

.PARAMETER NoInstaller
  Skip the InnoSetup step (portable zip only).

.EXAMPLE
  ./package-windows.ps1
  ./package-windows.ps1 -NoInstaller
#>
param(
    [ValidateSet('x64')]
    [string]$Arch = 'x64',
    [switch]$NoInstaller
)

$ErrorActionPreference = 'Stop'
$RID = "win-$Arch"
$ProjectDir = $PSScriptRoot
$PublishDir = Join-Path $ProjectDir "bin\Release\net10.0\$RID\publish"
$DistDir    = Join-Path $ProjectDir "dist"

Write-Host "CST Avalonia Windows Packaging" -ForegroundColor Cyan
Write-Host "==============================" -ForegroundColor Cyan
Write-Host "Architecture: $RID"

# --- Version (from the csproj <Version>) ---
$verMatch = Select-String -Path (Join-Path $ProjectDir 'CST.Avalonia.csproj') -Pattern '<Version>(.*?)</Version>' | Select-Object -First 1
if (-not $verMatch) { throw "Could not read <Version> from CST.Avalonia.csproj - refusing to build a mislabeled release." }
$Version = $verMatch.Matches[0].Groups[1].Value
Write-Host "Version: $Version"

# --- Clean + publish ---
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
Write-Host "`nPublishing self-contained $RID build..." -ForegroundColor Yellow
dotnet publish (Join-Path $ProjectDir 'CST.Avalonia.csproj') -c Release -r $RID --self-contained
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# --- Stage bundled resources beside the exe (the app seeds %APPDATA%\CSTReader from here on first run) ---
Write-Host "`nStaging bundled resources (xsl + dictionaries)..." -ForegroundColor Yellow
foreach ($res in @('xsl','dictionaries')) {
    $src = Join-Path $ProjectDir $res
    $dst = Join-Path $PublishDir $res
    if (-not (Test-Path $src)) { throw "Bundled resource source not found: $src" }
    Copy-Item $src $dst -Recurse -Force
    $n = (Get-ChildItem $dst -Recurse -File | Measure-Object).Count
    Write-Host "  - $res : $n file(s)"
}

# Sanity: confirm CEF actually landed in the publish (the biggest packaging risk).
if (-not (Test-Path (Join-Path $PublishDir 'libcef.dll'))) { throw "libcef.dll missing from publish output - CEF not packaged." }

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

# --- Portable zip ---
$zip = Join-Path $DistDir "CST-Reader-$Version-$RID-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "`nCreating portable zip..." -ForegroundColor Yellow
# Build entries with forward-slash names. Under Windows PowerShell 5.1 (.NET Framework) BOTH Compress-Archive and
# ZipFile.CreateFromDirectory write BACKSLASH separators, which mangle when extracted on macOS/Linux. Stream each
# file so the 205 MB libcef.dll isn't buffered in memory. (fable review, #403)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path $zip) { Remove-Item $zip -Force }
$baseLen = ((Resolve-Path $PublishDir).Path.TrimEnd('\') + '\').Length
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem $PublishDir -Recurse -File)) {
            $rel = $file.FullName.Substring($baseLen).Replace('\','/')
            $entry = $archive.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
            $dst = $entry.Open()
            $src = [System.IO.File]::OpenRead($file.FullName)
            try { $src.CopyTo($dst) } finally { $src.Dispose(); $dst.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $fs.Dispose() }
Write-Host "  -> $zip  ($([math]::Round((Get-Item $zip).Length/1MB)) MB)"

# --- InnoSetup installer ---
if ($NoInstaller) { Write-Host "`nSkipping installer (-NoInstaller)."; return }

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) {
    Write-Warning "InnoSetup compiler (ISCC.exe) not found. Install it (winget install JRSoftware.InnoSetup) and re-run, or use -NoInstaller. Portable zip is ready."
    return
}

Write-Host "`nBuilding InnoSetup installer with $iscc ..." -ForegroundColor Yellow
& $iscc "/DAppVersion=$Version" "/DPublishDir=$PublishDir" "/DOutputDir=$DistDir" (Join-Path $ProjectDir 'CST.Avalonia.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $DistDir "CST-Reader-$Version-$RID-setup.exe"
if (-not (Test-Path $setup)) { throw "ISCC reported success but $setup was not produced (OutputBaseFilename drift?)." }
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
Write-Host "`nInstaller: $setup  ($([math]::Round((Get-Item $setup).Length/1MB)) MB)" -ForegroundColor Green
Write-Host "SHA256:    $sha  (for the WinGet manifest, #410)"
Write-Host "`nDone." -ForegroundColor Cyan
