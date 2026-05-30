param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryOwner,

    [ValidateNotNullOrEmpty()]
    [string]$RepositoryName = "EverydaySlideshow",

    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$rootFull = [System.IO.Path]::GetFullPath($root)
$artifacts = Join-Path $root "artifacts"
$releaseDir = Join-Path $artifacts "release"
$publishDir = Join-Path $artifacts "publish\portable-win-x64"
$stageDir = Join-Path $artifacts "stage"
$portableStage = Join-Path $stageDir "portable"
$dotnetLocal = Join-Path $root ".dotnet-sdk\dotnet.exe"
$dotnet = if (Test-Path $dotnetLocal) { $dotnetLocal } else { "dotnet" }
$project = Join-Path $root "src\EverydaySlideshow\EverydaySlideshow.csproj"
$solution = Join-Path $root "EverydaySlideshow.sln"
$installerScript = Join-Path $root "installer\EverydaySlideshow.iss"
$assemblyVersion = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }

function Reset-Directory([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $artifactRoot = [System.IO.Path]::GetFullPath($artifacts)
    if (!$full.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a path outside artifacts: $Path"
    }

    if (Test-Path $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $full | Out-Null
}

function Find-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 5\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
        "C:\Program Files\Inno Setup 5\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

Reset-Directory $releaseDir
Reset-Directory $publishDir
Reset-Directory $stageDir
New-Item -ItemType Directory -Force -Path $portableStage | Out-Null

& $dotnet restore $solution
& $dotnet test $solution --configuration Release
& $dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:RepositoryOwner=$RepositoryOwner `
    -p:RepositoryName=$RepositoryName

$publishedExe = Join-Path $publishDir "EverydaySlideshow.exe"
if (!(Test-Path $publishedExe)) {
    throw "Published executable was not found: $publishedExe"
}

$portableExeName = "EverydaySlideshow-$Version-portable-win-x64.exe"
$portableZipName = "EverydaySlideshow-$Version-portable-win-x64.zip"
$setupExeName = "EverydaySlideshow-$Version-setup-win-x64.exe"
$setupZipName = "EverydaySlideshow-$Version-setup-win-x64.zip"

Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $releaseDir $portableExeName)
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $portableStage "EverydaySlideshow.exe")
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root "README.ja.md") -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $portableStage

$portableZip = Join-Path $releaseDir $portableZipName
Compress-Archive -Path (Join-Path $portableStage "*") -DestinationPath $portableZip -Force

if (!$SkipInstaller) {
    $iscc = Find-InnoCompiler
    if (!$iscc) {
        throw "Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 or rerun with -SkipInstaller."
    }

    & $iscc `
        "/DAppVersion=$Version" `
        "/DSourceDir=$publishDir" `
        "/DRootDir=$rootFull" `
        "/DOutputDir=$releaseDir" `
        "/DOutputBaseFilename=EverydaySlideshow-$Version-setup-win-x64" `
        $installerScript

    $setupExe = Join-Path $releaseDir $setupExeName
    if (!(Test-Path $setupExe)) {
        throw "Installer executable was not found: $setupExe"
    }

    Compress-Archive -Path $setupExe -DestinationPath (Join-Path $releaseDir $setupZipName) -Force
}

$hashLines = Get-ChildItem -LiteralPath $releaseDir -File |
    Where-Object { $_.Extension -in ".exe", ".zip" } |
    Sort-Object Name |
    ForEach-Object {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
        "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
    }

$hashLines | Set-Content -Encoding ASCII -Path (Join-Path $releaseDir "SHA256SUMS.txt")

Write-Host "Release artifacts:"
Get-ChildItem -LiteralPath $releaseDir -File | Sort-Object Name | ForEach-Object {
    Write-Host " - $($_.Name)"
}
