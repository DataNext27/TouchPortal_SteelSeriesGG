# Builds the release .tpp package (run from the repo root: .\pack.ps1).
$ErrorActionPreference = "Stop"

$csproj = Join-Path $PSScriptRoot "TPSteelSeriesGG\TPSteelSeriesGG.csproj"
[xml]$proj = Get-Content $csproj
$version = $proj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> property found in the csproj" }

$publishDir = Join-Path $PSScriptRoot "publish"
$staging = Join-Path $publishDir "TPSteelSeriesGG"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Single-file, self-contained, compressed; symbols embedded in the exe (no .pdb to ship).
dotnet publish $csproj -c Release `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $staging
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# A .tpp is a zip whose root contains the plugin folder itself.
$tpp = Join-Path $publishDir "TPSteelSeriesGG_v$version.tpp"
Compress-Archive -Path $staging -DestinationPath "$tpp.zip" -CompressionLevel Optimal
Rename-Item "$tpp.zip" $tpp

$sizeMb = [math]::Round((Get-Item $tpp).Length / 1MB, 1)
Write-Host "Packaged: $tpp ($sizeMb MB)" -ForegroundColor Green
