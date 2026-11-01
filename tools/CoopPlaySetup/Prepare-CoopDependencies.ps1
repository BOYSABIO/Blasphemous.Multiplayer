[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$AssetDirectory
)

# These are inputs to the player-copy builder, never installers or scripts to run.
# AssetDirectory allows a completely offline package build from the same pins.
$ErrorActionPreference = 'Stop'
$pins = @(
    @{ id='bepinex'; file='BepInEx_x64_5.4.21.0.zip'; sha256='2AF69FE0AAF821E95C4CD3E4D53860E667C54648F97DCA6F971A5BCD3C22AA34'; url='https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip' },
    @{ id='multifolder'; file='BepInEx.MultiFolderLoader.dll'; sha256='6FB2CB30CB0E25BA3FDAA7F6E4CF9FDC860BB4FD2574DEDF690B5FE1C2248F6C'; url='https://github.com/BepInEx/BepInEx.MultiFolderLoader/releases/download/v1.3.1/BepInEx.MultiFolderLoader.dll' },
    @{ id='api'; file='ModdingAPI-3.0.1.zip'; sha256='B74FE3900B563483D090FD7F01939FE7D60BA5711E83D7996396A274E7156FF7'; url='https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/download/3.0.1/ModdingAPI.zip' },
    @{ id='console'; file='CheatConsole-1.1.0.zip'; sha256='2280E5B4FD82A88395AAE5F841A4B64C47CF24A22C0603F02EFB220662067DB8'; url='https://github.com/BrandenEK/Blasphemous.CheatConsole/releases/download/1.1.0/CheatConsole.zip' },
    @{ file='BepInEx-LICENSE.txt'; sha256='E6E534EF6F4347B6449407EE046A3D09CB0174C6F688C996AD0BED94B74B3933'; url='https://raw.githubusercontent.com/BepInEx/BepInEx/v5.4.21/LICENSE' },
    @{ file='MultiFolderLoader-LICENSE.txt'; sha256='00DE20316BB5F2B227A7219384785E0970AB0C800DAF374C1962E370C33497DE'; url='https://raw.githubusercontent.com/BepInEx/BepInEx.MultiFolderLoader/v1.3.1/LICENSE' },
    @{ file='ModdingAPI-LICENSE.md'; sha256='436CAB80543D96500519E67477D0EC36530CC6B7F8F076333386D8030EE099C6'; url='https://raw.githubusercontent.com/BrandenEK/Blasphemous.ModdingAPI/3.0.1/docs/LICENSE.md' },
    @{ file='CheatConsole-LICENSE.md'; sha256='D02BBE8E956B5A4F39CAEE418474210B5F4E13D37F203D4B543ABCAA1170BA7B'; url='https://raw.githubusercontent.com/BrandenEK/Blasphemous.CheatConsole/1.1.0/docs/LICENSE.md' },
    @{ file='BepInEx-5.4.21-source.zip'; sha256='62F4EEF48415E96F08778F939B10D6ACDC8744AB217518DB608D89DBAF15F08A'; url='https://github.com/BepInEx/BepInEx/archive/refs/tags/v5.4.21.zip' }
)
function Assert-PlainPath([string]$Path) {
    for ($check = [IO.Path]::GetFullPath($Path); $check; ) {
        if ((Test-Path -LiteralPath $check) -and
            (((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'Dependency paths cannot be reparse points.'
        }
        $parent = [IO.Directory]::GetParent($check)
        $check = if ($parent) { $parent.FullName } else { $null }
    }
}
function Assert-Hash([string]$Path, [string]$Expected) {
    Assert-PlainPath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Pinned dependency SHA-256 mismatch or missing file: $([IO.Path]::GetFileName($Path))"
    }
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
Assert-PlainPath $outputPath
if ((Test-Path -LiteralPath $outputPath -PathType Leaf) -or
    ((Test-Path -LiteralPath $outputPath -PathType Container) -and @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0)) {
    throw 'OutputDirectory must be new or empty. Existing bundles are never overwritten.'
}
if ($AssetDirectory) {
    $assetPath = (Resolve-Path -LiteralPath $AssetDirectory).Path
    foreach ($pin in $pins) { Assert-Hash (Join-Path $assetPath $pin.file) $pin.sha256 }
}
if (-not $PSCmdlet.ShouldProcess($outputPath, 'Prepare nine hash-checked official dependency assets/notices and their manifest')) { return }
[void][IO.Directory]::CreateDirectory($outputPath)
foreach ($pin in $pins) {
    $destination = Join-Path $outputPath $pin.file
    if ($AssetDirectory) { [IO.File]::Copy((Join-Path $assetPath $pin.file), $destination, $false) }
    else { Invoke-WebRequest -Uri $pin.url -OutFile $destination }
    Assert-Hash $destination $pin.sha256
}
$manifest = [ordered]@{
    schemaVersion = 1
    assets = @($pins | Where-Object { $_.ContainsKey('id') } | ForEach-Object {
        [ordered]@{ id=$_.id; file=$_.file; sha256=$_.sha256.ToLowerInvariant(); url=$_.url }
    })
    notices = @($pins | Where-Object { -not $_.ContainsKey('id') } | ForEach-Object {
        [ordered]@{ file=$_.file; sha256=$_.sha256.ToLowerInvariant(); url=$_.url }
    })
}
[IO.File]::WriteAllText((Join-Path $outputPath 'dependencies.json'), ($manifest | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output (Join-Path $outputPath 'dependencies.json')
