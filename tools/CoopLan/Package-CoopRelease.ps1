[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$ClientBuildDirectory,
    [Parameter(Mandatory = $true)][string]$ServerDirectory,
    [Parameter(Mandatory = $true)][string]$SetupExecutable,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-PlainPath([string]$Path) {
    $pathToCheck = [IO.Path]::GetFullPath($Path)
    while ($pathToCheck) {
        if (Test-Path -LiteralPath $pathToCheck) {
            if (((Get-Item -LiteralPath $pathToCheck -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse-point paths are refused for package inputs and output.'
            }
        }
        $parent = [IO.Directory]::GetParent($pathToCheck)
        $pathToCheck = if ($parent) { $parent.FullName } else { $null }
    }
}
function Require-File([string]$Path) {
    Assert-PlainPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing package input: $Path" }
    return (Resolve-Path -LiteralPath $Path).Path
}
function Numeric-Version([string]$Value, [string]$Label) {
    $parsed = $null
    if (-not [Version]::TryParse($Value, [ref]$parsed)) { throw "$Label must have a numeric assembly version." }
    return [Version]::new($parsed.Major, $parsed.Minor, [Math]::Max(0, $parsed.Build), [Math]::Max(0, $parsed.Revision))
}
function File-Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Copy-Checked([string]$Source, [string]$Target, [string]$ExpectedHash) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Target))
    [IO.File]::Copy($Source, $Target, $false)
    if ((File-Hash $Target) -ne $ExpectedHash) { throw 'An input changed while copying; partial package retained for inspection.' }
}
function Write-Utf8([string]$Path, [string]$Value) { [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false)) }

Assert-PlainPath $ClientBuildDirectory
Assert-PlainPath $ServerDirectory
Assert-PlainPath $OutputDirectory
$clientDirectory = (Resolve-Path -LiteralPath $ClientBuildDirectory).Path
$serverDirectoryPath = (Resolve-Path -LiteralPath $ServerDirectory).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $outputPath -PathType Leaf) -or
    ((Test-Path -LiteralPath $outputPath -PathType Container) -and @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0)) {
    throw 'OutputDirectory must be new or empty. Existing packages are never overwritten.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$clientPath = Require-File (Join-Path $clientDirectory 'BlasClient.dll')
$commonPath = Require-File (Join-Path $clientDirectory 'Blasphemous.Multiplayer.Common.dll')
$tcpPath = Require-File (Join-Path $clientDirectory 'SimpleTCP.dll')
$serverPath = Require-File (Join-Path $serverDirectoryPath 'blas1mpserver.exe')
$serverManifestPath = Require-File (Join-Path $serverDirectoryPath 'coop-release.json')
$setupPath = Require-File $SetupExecutable
if ([IO.Path]::GetExtension($setupPath) -ne '.exe' -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo($setupPath).ProductName -ne 'CoopPlaySetup') {
    throw 'SetupExecutable must be the self-contained CoopPlaySetup Windows executable.'
}
$clientIdentity = [Reflection.AssemblyName]::GetAssemblyName($clientPath)
$commonIdentity = [Reflection.AssemblyName]::GetAssemblyName($commonPath)
$tcpIdentity = [Reflection.AssemblyName]::GetAssemblyName($tcpPath)
if ($clientIdentity.Name -ne 'BlasClient' -or $commonIdentity.Name -ne 'Blasphemous.Multiplayer.Common' -or $tcpIdentity.Name -ne 'SimpleTCP') {
    throw 'Client build inputs have unexpected managed assembly identities.'
}
$version = $clientIdentity.Version
$commonVersion = $commonIdentity.Version
if ($version -ne $commonVersion) { throw 'Client and common assembly versions differ.' }
$common = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($commonPath))
$protocolField = $common.GetType('Blasphemous.Multiplayer.Common.Protocol', $true).GetField('VERSION')
if (-not $protocolField -or [int]$protocolField.GetRawConstantValue() -ne 17) {
    throw 'This Warden candidate packager requires client protocol 17.'
}
$serverManifest = Get-Content -LiteralPath $serverManifestPath -Raw | ConvertFrom-Json
if ($serverManifest.schemaVersion -ne 1 -or $serverManifest.protocol -ne 17 -or
    $serverManifest.serverSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    (File-Hash $serverPath) -ne $serverManifest.serverSha256 -or
    $serverManifest.runtime -notmatch 'win-x64 self-contained, untrimmed') {
    throw 'Server release manifest, protocol, runtime or SHA-256 does not match the required self-contained protocol-17 server.'
}
$serverVersion = Numeric-Version ([string]$serverManifest.version) 'Server manifest'
$serverFileVersion = Numeric-Version ([Diagnostics.FileVersionInfo]::GetVersionInfo($serverPath).FileVersion) 'Server executable'
if ($version -ne $serverVersion -or $version -ne $serverFileVersion) { throw 'Client/common/server numeric versions differ.' }

# Exactly the seven file paths in the verified published mod layout. Never copy
# a client build directory wholesale: GameLibs references also appear in bin/.
$payloadFiles = [ordered]@{
    'data/Blasphemous.Multiplayer.Common.dll' = $commonPath
    'data/Multiplayer/attacks.json' = Require-File (Join-Path $repository 'resources\data\Multiplayer\attacks.json')
    'data/Multiplayer/defenses.json' = Require-File (Join-Path $repository 'resources\data\Multiplayer\defenses.json')
    'data/Multiplayer/offenses.json' = Require-File (Join-Path $repository 'resources\data\Multiplayer\offenses.json')
    'data/SimpleTCP.dll' = $tcpPath
    'localization/Multiplayer.txt' = Require-File (Join-Path $repository 'resources\localization\Multiplayer.txt')
    'plugins/BlasClient.dll' = $clientPath
}
$payloadHashes = @{}
foreach ($entry in $payloadFiles.GetEnumerator()) {
    if ((Get-Item -LiteralPath $entry.Value).Length -gt 8MB) { throw 'Unexpectedly large mod payload input.' }
    if ($entry.Key.EndsWith('.json')) { $null = Get-Content -LiteralPath $entry.Value -Raw | ConvertFrom-Json }
    $payloadHashes[$entry.Key] = File-Hash $entry.Value
}
$packageFiles = [ordered]@{
    'Setup.exe' = $setupPath
    'server/blas1mpserver.exe' = $serverPath
    'tools/CoopLan/Start-CoopHost.ps1' = Require-File (Join-Path $PSScriptRoot 'Start-CoopHost.ps1')
    'tools/CoopLan/Join-CoopLan.ps1' = Require-File (Join-Path $PSScriptRoot 'Join-CoopLan.ps1')
    'LICENSE.md' = Require-File (Join-Path $repository 'revived\LICENSE.md')
}
$licenseRoot = Join-Path $PSScriptRoot 'licenses'
$licenseManifestPath = Require-File (Join-Path $licenseRoot 'sources.json')
$licenseManifest = Get-Content -LiteralPath $licenseManifestPath -Raw | ConvertFrom-Json
if ($licenseManifest.schemaVersion -ne 1) { throw 'Unsupported dependency-license manifest.' }
foreach ($name in @('Mono.Cecil.LICENSE.txt', 'Basalt.CommandParser.LICENSE.txt', 'SimpleTCP.LICENSE.txt', 'dotnet-LICENSE.TXT', 'dotnet-THIRD-PARTY-NOTICES.TXT')) {
    $licensePath = Require-File (Join-Path $licenseRoot $name)
    $expected = @($licenseManifest.sources | Where-Object { $_.file -ceq $name })
    if ($expected.Count -ne 1 -or $expected[0].sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        (File-Hash $licensePath) -ne $expected[0].sha256) { throw 'A pinned dependency-license notice does not match its manifest.' }
    $packageFiles.Add(('licenses/' + $name), $licensePath)
}
$packageFiles.Add('licenses/sources.json', $licenseManifestPath)
$packageHashes = @{}
foreach ($entry in $packageFiles.GetEnumerator()) { $packageHashes[$entry.Key] = File-Hash $entry.Value }
if (-not $PSCmdlet.ShouldProcess($outputPath, "Create experimental client/server package $version, protocol 17")) { return }
[void][IO.Directory]::CreateDirectory($outputPath)
$archivePath = Join-Path $outputPath 'BlasClient.zip'
$file = [IO.FileStream]::new($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($item in $payloadFiles.GetEnumerator()) {
            $entry = $archive.CreateEntry($item.Key, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $entry.ExternalAttributes = 0
            $inputStream = [IO.File]::OpenRead($item.Value)
            $outputStream = $entry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $file.Dispose() }
# Compare bytes actually put in the ZIP, catching a source rebuild during packing.
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $archive.Entries) {
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $payloadHashes[$entry.FullName]) { throw 'A mod input changed while packing; partial package retained for inspection.' }
    }
} finally { $archive.Dispose() }
foreach ($entry in $packageFiles.GetEnumerator()) {
    Copy-Checked $entry.Value (Join-Path $outputPath $entry.Key) $packageHashes[$entry.Key]
}
$payloadManifest = [ordered]@{
    schemaVersion = 1; clientArchive = 'BlasClient.zip'; clientSha256 = File-Hash $archivePath
    clientVersion = $version.ToString(); protocol = 17; gameplayVerified = $false
}
Write-Utf8 (Join-Path $outputPath 'payload-manifest.json') ($payloadManifest | ConvertTo-Json)
$matchingServer = [ordered]@{
    schemaVersion = 1; version = $version.ToString(); protocol = 17
    serverSha256 = $packageHashes['server/blas1mpserver.exe']; runtime = [string]$serverManifest.runtime
    gameplayVerified = $false
}
Write-Utf8 (Join-Path $outputPath 'server/coop-release.json') ($matchingServer | ConvertTo-Json)
$readme = @'
Blasphemous LAN co-op development candidate

EXPERIMENTAL. The Warden is the only boss admitted to synchronization.
Other bosses remain excluded. Full-campaign co-op is unsupported.
This package build checks files, hashes and versions; it does not verify gameplay.

Every player needs their own supported Windows game and the same package.
Keep Steam running. Use a separate source copy containing BepInEx 5,
Modding API >=3.0.0, Cheat Console >=1.1.0 and MultiFolderLoader.
Do not install prerequisites into the normal game for this isolated setup.
Official prerequisite installer: https://github.com/BrandenEK/Blasphemous.Modding.Installer

From this package directory in PowerShell, replace the quoted placeholders:
  .\Setup.exe --game-directory '<owned source game with prerequisites>' --destination '<new co-op copy>' --payload-manifest .\payload-manifest.json
The destination must be new or empty. Setup verifies the supported executable
and payload hashes, copies physical files, and gives the copy its own saves/.
Existing saves are not imported. Original game files and saves are not written.
Steam achievements remain enabled; cloud-storage APIs are disabled in the copy.
Launch Play-Coop.cmd inside the prepared copy. Load a level.

One player hosts from the package directory:
  .\tools\CoopLan\Start-CoopHost.ps1 -ServerDirectory .\server -MaxPlayers 4
The console prints LAN addresses and stays open; Escape stops the server.
Default port: TCP 27051. If occupied, choose -Port and use it on all clients.
Optional firewall setup: repeat the host command in Administrator PowerShell
with -ConfigureFirewall. The rule is app-specific, Private, LocalSubnet only.

In each game, click Multiplayer connection info at the bottom of the screen.
Host: Server IP localhost:27051. Friends: host's printed LAN IPv4:27051.
Choose unique player names and the same team, such as 1, then Connect.
If the host sets -Password, enter that same password on every client.
Friends can check TCP reachability first:
  .\tools\CoopLan\Join-CoopLan.ps1 -HostAddress '<host-LAN-IPv4>' -CheckConnection
Router forwarding and Steam relay are not required on the same LAN.

All clients and the server must match the versions/protocol in the manifests.
The server manifest intentionally keeps gameplayVerified=false.
No game assets, game assemblies, private profiles, saves or QA bots are included.
Dependency license notices and their pinned source URLs are under licenses/.
'@
Write-Utf8 (Join-Path $outputPath 'README.txt') ($readme + "`r`n`r`nAssembly version: $version; protocol: 17.`r`n")
$checksums = [ordered]@{}
Get-ChildItem -LiteralPath $outputPath -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($outputPath.TrimEnd('\', '/').Length + 1).Replace('\', '/')
    $checksums[$relative] = File-Hash $_.FullName
}
Write-Utf8 (Join-Path $outputPath 'checksums.json') ([ordered]@{
    schemaVersion = 1; version = $version.ToString(); protocol = 17; gameplayVerified = $false; files = $checksums
} | ConvertTo-Json -Depth 4)
Write-Host "Created experimental package $version, protocol 17. No gameplay verification or publishing was performed."
