param(
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root ".dotnet-sdk\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$configuration = if ($Release) { "Release" } else { "Debug" }

& $dotnet build (Join-Path $root "EverydaySlideshow.sln") --configuration $configuration
& $dotnet test (Join-Path $root "EverydaySlideshow.sln") --configuration $configuration --no-build
