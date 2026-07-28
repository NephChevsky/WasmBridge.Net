# Packs WasmBridge.Attributes and WasmBridge.Net.Sdk into the local feed (./artifacts).
#
# Bump the <Version> in each project's .csproj before running this to publish a new version;
# consumers pin an exact Version in their PackageReference (NuGet versions are immutable once
# pushed to nuget.org), so there's no floating version to rely on here.
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$feed = Join-Path $repoRoot 'artifacts'

New-Item -ItemType Directory -Force -Path $feed | Out-Null

Write-Host "Packing into $feed"
dotnet pack (Join-Path $repoRoot 'WasmBridge.Attributes\WasmBridge.Attributes.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet pack (Join-Path $repoRoot 'WasmBridge.Sdk\WasmBridge.Sdk.csproj') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done."
