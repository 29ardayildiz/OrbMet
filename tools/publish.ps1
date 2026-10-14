# Builds OrbMet's two release packages into artifacts\:
#
#   OrbMet-<version>-win-x64.zip                   runs as is, no .NET needed
#   OrbMet-<version>-win-x64-runtime-required.zip  needs the .NET 10 Desktop Runtime
#
# The panel and the Claude status line helper are published into one folder,
# since Claude runs the helper from wherever the user unpacked OrbMet.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1 [-Version 0.1.0]

param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
$staging = Join-Path $artifacts "publish"

# Files a working package cannot do without, checked after publishing.
$required = @(
    "OrbMet.exe",
    "OrbMet.dll",
    "OrbMet.Core.dll",
    "OrbMet.Capture.exe",
    "OrbMet.Capture.dll",
    "OrbMet.Capture.runtimeconfig.json",
    "tr\OrbMet.Core.resources.dll",
    "THIRD_PARTY_NOTICES.md"
)

function Publish-Project([string]$project, [string]$output, [bool]$selfContained) {
    & dotnet publish (Join-Path $root $project) `
        -c Release `
        -r $Runtime `
        --self-contained $selfContained.ToString().ToLowerInvariant() `
        -p:Version=$Version `
        -p:DebugType=None `
        -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $project"
    }
}

$variants = @(
    @{ Name = "OrbMet-$Version-$Runtime"; SelfContained = $true },
    @{ Name = "OrbMet-$Version-$Runtime-runtime-required"; SelfContained = $false }
)

foreach ($variant in $variants) {
    $folder = Join-Path $staging $variant.Name
    $zip = Join-Path $artifacts ($variant.Name + ".zip")
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    if (Test-Path $zip) { Remove-Item $zip -Force }

    # The panel first; the helper's own publish then supplies its complete
    # set of files, which the panel's project reference copies only in part.
    Publish-Project "src\OrbMet.Desktop\OrbMet.Desktop.csproj" $folder $variant.SelfContained
    Publish-Project "src\OrbMet.Capture\OrbMet.Capture.csproj" $folder $variant.SelfContained

    Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $folder
    Copy-Item (Join-Path $root "licenses") (Join-Path $folder "licenses") -Recurse
    foreach ($optional in "LICENSE", "README.md") {
        $path = Join-Path $root $optional
        if (Test-Path $path) { Copy-Item $path $folder }
    }

    $missing = $required | Where-Object { -not (Test-Path (Join-Path $folder $_)) }
    if ($missing) {
        throw "$($variant.Name) is missing: $($missing -join ', ')"
    }

    Compress-Archive -Path (Join-Path $folder "*") -DestinationPath $zip
    $size = (Get-Item $zip).Length / 1MB
    Write-Host ("{0}.zip  {1:N1} MB" -f $variant.Name, $size)
}
