$ErrorActionPreference = "Stop"

$version = "1.0.0"
$project = "MonkeModCleaner"
$root = $PSScriptRoot
$csproj = Join-Path $root "$project.csproj"
$outBase = Join-Path $root "publish"

if (Test-Path $outBase) { Remove-Item $outBase -Recurse -Force }

# Regular build to generate resources.pri
Write-Host "Building to generate resources.pri..." -ForegroundColor Cyan
dotnet build $csproj -c Release -r win-x64

$pri = Get-ChildItem -Path (Join-Path $root "bin") -Recurse -Filter "resources.pri" | Select-Object -First 1

if (-not $pri) {
    Write-Host "WARNING: resources.pri not found, continuing without it" -ForegroundColor Yellow
}
else {
    Write-Host "Found resources.pri at: $($pri.FullName)" -ForegroundColor Green
}

foreach ($rid in @("win-x64", "win-arm64")) {
    Write-Host "`nPublishing $rid..." -ForegroundColor Cyan
    $out = Join-Path $outBase $rid
    dotnet publish $csproj -c Release -r $rid --self-contained -p:WindowsPackageType=None -o $out

    if ($pri) { Copy-Item $pri.FullName -Destination $out -Force }
    Copy-Item (Join-Path $root "$project.ico") -Destination $out -Force
    Write-Host "$rid publish complete" -ForegroundColor Green
}

Write-Host "`nAll builds complete. Output in: $outBase" -ForegroundColor Green