param([string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$ErrorActionPreference = 'Stop'
$identity = Get-Content -LiteralPath (Join-Path $Workspace 'local\identity.json') -Raw | ConvertFrom-Json
$original = $identity.GameDirectory
function Assert-Hash([string]$Path, [string]$Expected) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Original file changed: $Path"
    }
}
Assert-Hash (Join-Path $original 'Blasphemous.exe') $identity.ExeSha256
Assert-Hash (Join-Path $original 'Modding\plugins\BlasClient.dll') $identity.InstalledClientSha256
$isolation = Get-Content -LiteralPath (Join-Path $Workspace 'local\slots\0\qa-isolation.json') -Raw | ConvertFrom-Json
foreach ($record in $isolation.Files) {
    $relative = $record.Path.Substring($isolation.GameDirectory.Length).TrimStart('\', '/')
    Assert-Hash (Join-Path $original $relative) $record.BeforeSha256
}
$manifest = @(Get-Content -LiteralPath (Join-Path $Workspace 'local\original-save-manifest.json') -Raw | ConvertFrom-Json)
foreach ($entry in $manifest) { Assert-Hash $entry.Path $entry.Sha256 }
$saveRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\TheGameKitchen\Blasphemous'
$currentPaths = @(Get-ChildItem -LiteralPath $saveRoot -Recurse -File | ForEach-Object { $_.FullName } | Sort-Object)
$oldPaths = @($manifest | ForEach-Object { $_.Path } | Sort-Object)
if (@(Compare-Object $oldPaths $currentPaths).Count -ne 0) { throw 'Original save-file list changed' }
$prefsPath = Join-Path $Workspace 'local\original-playerprefs.json'
if (Test-Path -LiteralPath $prefsPath) {
    $expected = (Get-Content -LiteralPath $prefsPath -Raw | ConvertFrom-Json | ConvertTo-Json -Depth 6 -Compress)
    $actual = Get-ItemProperty -LiteralPath 'HKCU:\Software\TheGameKitchen\Blasphemous' |
        Select-Object -Property * -ExcludeProperty PSPath,PSParentPath,PSChildName,PSDrive,PSProvider |
        ConvertTo-Json -Depth 6 -Compress
    if ($actual -ne $expected) { throw 'Original Unity preferences changed' }
}
Write-Output "OriginalState: PASS (game, installed client, patched-input originals, $($manifest.Count) saves, native preferences unchanged)"
