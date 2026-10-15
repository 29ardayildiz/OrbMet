# Builds OrbMet's release files into artifacts\:
#
#   OrbMet-<version>-setup.exe    installer (per user, no administrator rights)
#   OrbMet-<version>-win-x64.zip  the same files to unpack and run, no installer
#
# Both carry the self-contained build, so no .NET install is needed. The panel
# and the Claude status line helper are published into one folder, since
# Claude runs the helper from wherever OrbMet lives.
#
# The installer needs Inno Setup 6 (winget install JRSoftware.InnoSetup).
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1 [-Version 0.2.0]

param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [string]$Iscc
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
$staging = Join-Path $artifacts "publish"
$name = "OrbMet-$Version-$Runtime"
$folder = Join-Path $staging $name
$zip = Join-Path $artifacts "$name.zip"
$setup = Join-Path $artifacts "OrbMet-$Version-setup.exe"

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

function Publish-Project([string]$project) {
    & dotnet publish (Join-Path $root $project) `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:Version=$Version `
        -p:DebugType=None `
        -o $folder
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $project"
    }
}

function Find-Iscc {
    if ($Iscc) { return $Iscc }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $found = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($found) { return $found }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw "Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup"
}

foreach ($old in $folder, $zip, $setup) {
    if (Test-Path $old) { Remove-Item $old -Recurse -Force }
}

# The panel first; the helper's own publish then supplies its complete set of
# files, which the panel's project reference copies only in part.
Publish-Project "src\OrbMet.Desktop\OrbMet.Desktop.csproj"
Publish-Project "src\OrbMet.Capture\OrbMet.Capture.csproj"

Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $folder
Copy-Item (Join-Path $root "licenses") (Join-Path $folder "licenses") -Recurse
foreach ($optional in "LICENSE", "README.md") {
    $path = Join-Path $root $optional
    if (Test-Path $path) { Copy-Item $path $folder }
}

$missing = $required | Where-Object { -not (Test-Path (Join-Path $folder $_)) }
if ($missing) {
    throw "$name is missing: $($missing -join ', ')"
}

# Compress-Archive and ZipFile.CreateFromDirectory in Windows PowerShell both
# write "licenses\x" entry names; the zip format wants "/".
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $folder -Recurse -File) {
        $entry = $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}
Write-Host ("{0}  {1:N1} MB" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB))

& (Find-Iscc) /Qp "/DAppVersion=$Version" "/DSourceDir=$folder" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot "installer\OrbMet.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed"
}
Write-Host ("{0}  {1:N1} MB" -f (Split-Path $setup -Leaf), ((Get-Item $setup).Length / 1MB))
