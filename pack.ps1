# Packs WasmBridge.Attributes and WasmBridge.Tasks into the local feed (./artifacts).
#
# Each run uses a unique timestamp-based prerelease version, so consumers referencing
# Version="1.0.0-*" always float to the newest local build on their next restore, without
# having to bump a version number by hand or clear the NuGet global-packages cache.
$ErrorActionPreference = 'Stop'

$version = "1.0.0-dev.$(Get-Date -Format 'yyyyMMddHHmmss')"
$repoRoot = $PSScriptRoot
$feed = Join-Path $repoRoot 'artifacts'

New-Item -ItemType Directory -Force -Path $feed | Out-Null

Write-Host "Packing version $version into $feed"
dotnet pack (Join-Path $repoRoot 'WasmBridge.Attributes\WasmBridge.Attributes.csproj') -c Release -o $feed -p:PackageVersion=$version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet pack (Join-Path $repoRoot 'WasmBridge.Tasks\WasmBridge.Tasks.csproj') -c Release -o $feed -p:PackageVersion=$version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done. Consumers using Version=`"1.0.0-*`" will pick this up on their next restore."
