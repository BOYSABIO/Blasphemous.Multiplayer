[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$ServerProject,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidateRange(1, 255)][int]$ExpectedProtocol,
    [string]$DotnetPath = 'dotnet',
    [ValidateSet('net8.0', 'netcoreapp8.0')][string]$Framework = 'net8.0',
    [string]$PackageCache
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $ServerProject).Path
if ([IO.Path]::GetExtension($projectPath) -ne '.csproj') { throw 'ServerProject must be the server .csproj file.' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $outputPath -PathType Leaf) -or
    ((Test-Path -LiteralPath $outputPath -PathType Container) -and @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0)) {
    throw 'OutputDirectory must be new or empty. Existing server packages are never overwritten.'
}
$dotnetCommand = (Get-Command $DotnetPath -CommandType Application -ErrorAction Stop).Source
$arguments = @(
    'publish', $projectPath, '--configuration', 'Release', '--framework', $Framework,
    '--runtime', 'win-x64', '--self-contained', 'true', '--output', $outputPath,
    '-m:1', '-nr:false', '--disable-build-servers', '-p:UseSharedCompilation=false',
    '-p:RuntimeFrameworkVersion=8.0.32', '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
    '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false'
)
if ($PackageCache) {
    $cachePath = (Resolve-Path -LiteralPath $PackageCache).Path
    $arguments += @('--source', $cachePath, ('-p:RestorePackagesPath=' + $cachePath))
}
if (-not $PSCmdlet.ShouldProcess($outputPath, 'Publish an untrimmed, self-contained Windows x64 server and its unverified release manifest')) { return }
& $dotnetCommand @arguments
if ($LASTEXITCODE -ne 0) { throw "Server publish failed with exit code $LASTEXITCODE. Partial output was left for inspection." }

$projectDirectory = Split-Path -Parent $projectPath
$commonPath = Join-Path $projectDirectory "bin\Release\$Framework\win-x64\Blasphemous.Multiplayer.Common.dll"
if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) { throw 'The built common assembly was not found. No release manifest was written.' }
$common = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($commonPath))
$protocolField = $common.GetType('Blasphemous.Multiplayer.Common.Protocol', $true).GetField('VERSION')
if (-not $protocolField -or [int]$protocolField.GetRawConstantValue() -ne $ExpectedProtocol) {
    throw "The compiled protocol does not match ExpectedProtocol $ExpectedProtocol. No release manifest was written."
}
$serverPath = Join-Path $outputPath 'blas1mpserver.exe'
if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf)) { throw 'Publish did not produce blas1mpserver.exe.' }
$unexpected = @(Get-ChildItem -LiteralPath $outputPath -File | Where-Object { $_.Name -ne 'blas1mpserver.exe' })
if ($unexpected.Count -gt 0) { throw 'Publish emitted unexpected loose files. Inspect the output before packaging; no release manifest was written.' }
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($serverPath)
$numericVersion = $null
if (-not [Version]::TryParse($metadata.FileVersion, [ref]$numericVersion)) { throw 'The server executable does not have a numeric file version.' }
$manifest = [ordered]@{
    schemaVersion = 1
    version = $numericVersion.ToString()
    informationalVersion = $metadata.ProductVersion
    protocol = $ExpectedProtocol
    serverSha256 = (Get-FileHash -LiteralPath $serverPath -Algorithm SHA256).Hash.ToLowerInvariant()
    runtime = '.NET 8.0.32 win-x64 self-contained, untrimmed'
    gameplayVerified = $false
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'coop-release.json') -Encoding UTF8
Write-Host "Published server $($manifest.version), protocol $ExpectedProtocol. Gameplay verification remains pending."
Write-Host 'This build command does not start the server, open firewall rules, or mark a package ready for play.'
