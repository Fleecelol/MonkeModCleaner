$ErrorActionPreference = "Stop"

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    Write-Host "ERROR: Inno Setup not found at $iscc" -ForegroundColor Red
    exit 1
}

$issFile = Join-Path $PSScriptRoot "MonkeModCleaner.iss"

Write-Host "Building x64 installer..." -ForegroundColor Cyan
& $iscc "/DArch=x64" "/DRID=win-x64" $issFile

Write-Host "Building ARM64 installer..." -ForegroundColor Cyan
& $iscc "/DArch=arm64" "/DRID=win-arm64" $issFile

Write-Host "`nInstallers created in: installers\" -ForegroundColor Green