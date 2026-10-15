# Builds OrbMet's release files into artifacts\:
#
#   OrbMet-<version>-setup.exe    installer (per user, no administrator rights)
#   OrbMet-<version>-win-x64.zip  the same files to unpack and run, no installer
#   *.sha256                      SHA-256 of each; OrbMet's updater will not run
#                                 an installer without its file
#   winget\<version>\             the winget manifests for this version, for a
#                                 pull request to microsoft/winget-pkgs once the
#                                 release is published (the URL points there)
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

foreach ($old in $folder, $zip, $setup, "$zip.sha256", "$setup.sha256") {
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

# "<hash>  <name>", the sha256sum format, without a byte order mark.
foreach ($file in $setup, $zip) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$file.sha256", "$hash  $(Split-Path $file -Leaf)`n")
}

# winget: the three manifests of a multi-file package, pointing at the
# installer as it will be on this version's GitHub release.
$id = "ArdaYildiz.OrbMet"
$schema = "1.10.0"
$wingetDir = Join-Path $artifacts "winget\$Version"
if (Test-Path $wingetDir) { Remove-Item $wingetDir -Recurse -Force }
New-Item -ItemType Directory -Force $wingetDir | Out-Null
$setupHash = (Get-FileHash $setup -Algorithm SHA256).Hash
$release = "https://github.com/29ardayildiz/OrbMet/releases"
$manifests = @{
    "$id.yaml" = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@
    "$id.installer.yaml" = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ReleaseDate: $(Get-Date -Format yyyy-MM-dd)
AppsAndFeaturesEntries:
- DisplayName: OrbMet
  Publisher: Arda YILDIZ
  ProductCode: '{D7931010-D851-43EC-AE6D-7D44F7B54866}_is1'
Installers:
- Architecture: x64
  InstallerUrl: $release/download/v$Version/OrbMet-$Version-setup.exe
  InstallerSha256: $setupHash
  ProductCode: '{D7931010-D851-43EC-AE6D-7D44F7B54866}_is1'
ManifestType: installer
ManifestVersion: $schema
"@
    "$id.locale.en-US.yaml" = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Arda YILDIZ
PublisherUrl: https://github.com/29ardayildiz
PublisherSupportUrl: https://github.com/29ardayildiz/OrbMet/issues
PackageName: OrbMet
PackageUrl: https://github.com/29ardayildiz/OrbMet
License: MIT
LicenseUrl: https://github.com/29ardayildiz/OrbMet/blob/main/LICENSE
ShortDescription: A small Windows panel that shows how much of your Claude Code and Codex usage limits you have used.
Description: OrbMet sits on the edge of your screen as a black strip with one ring per provider, filled to the share of the 5-hour limit used as Claude Code or Codex reports it. Hover a ring for every limit and when it resets.
Moniker: orbmet
Tags:
- claude
- claude-code
- codex
- usage
- rate-limit
- tray
ReleaseNotesUrl: $release/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@
}
foreach ($entry in $manifests.GetEnumerator()) {
    [System.IO.File]::WriteAllText((Join-Path $wingetDir $entry.Key), $entry.Value.Replace("`r`n", "`n") + "`n")
}
Write-Host "winget manifests  $wingetDir"
