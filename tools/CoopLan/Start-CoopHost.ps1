[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$ServerDirectory,
    [ValidateRange(1024, 65535)][int]$Port = 27051,
    [ValidateRange(1, 255)][int]$MaxPlayers = 10,
    [AllowEmptyString()][string]$Password = '',
    [switch]$ConfigureFirewall
)

$ErrorActionPreference = 'Stop'
$serverDirectoryPath = (Resolve-Path -LiteralPath $ServerDirectory).Path
$serverExecutable = Join-Path $serverDirectoryPath 'blas1mpserver.exe'
if (-not (Test-Path -LiteralPath $serverExecutable -PathType Leaf)) {
    throw "No matching server executable found at $serverExecutable. Select the server folder from the same package as every client."
}
if ($Password.Length -gt 32 -or @($Password.ToCharArray() | Where-Object {
    -not ([char]::IsLetterOrDigit($_) -or '_-.'.Contains([string]$_))
}).Count -gt 0) {
    throw 'The in-game password field supports at most 32 letters, digits, underscores, dots and hyphens.'
}

$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($serverExecutable)
$manifestPath = Join-Path $serverDirectoryPath 'coop-release.json'
Write-Host "Server: $($metadata.ProductVersion)"
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (-not $manifest.serverSha256 -or
        (Get-FileHash -LiteralPath $serverExecutable -Algorithm SHA256).Hash -ne $manifest.serverSha256) {
        throw 'The server does not match coop-release.json. Re-extract the matching package before hosting.'
    }
    Write-Host "Package: $($manifest.version); protocol: $($manifest.protocol)"
    if (-not $manifest.gameplayVerified) { Write-Host 'Candidate build: gameplay verification is pending.' }
} else {
    Write-Host 'No release manifest is present. Confirm every player has the matching client package.'
}

$listeners = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if (@($listeners | Where-Object { $_.Port -eq $Port }).Count -gt 0) {
    throw "TCP port $Port already has a listener. Keep that process unchanged; select another unused -Port and use it on every client."
}

Write-Host "Host player's Server IP: localhost:$Port"
Write-Host 'Friends on this LAN can use one of these Server IP values:'
$addressCount = 0
foreach ($adapter in [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
    if ($adapter.OperationalStatus -ne [Net.NetworkInformation.OperationalStatus]::Up -or
        $adapter.NetworkInterfaceType -eq [Net.NetworkInformation.NetworkInterfaceType]::Loopback -or
        $adapter.NetworkInterfaceType -eq [Net.NetworkInformation.NetworkInterfaceType]::Tunnel) { continue }
    foreach ($address in $adapter.GetIPProperties().UnicastAddresses) {
        if ($address.Address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { continue }
        $text = $address.Address.ToString()
        if ($text -like '127.*' -or $text -like '169.254.*' -or $text -eq '0.0.0.0') { continue }
        Write-Host ("  {0}:{1}  ({2})" -f $text, $Port, $adapter.Name)
        $addressCount++
    }
}
if ($addressCount -eq 0) { Write-Host 'No active LAN IPv4 address was found. Connect this PC to the shared LAN before friends join.' }
Write-Host 'Use unique player names and the same team, for example team 1.'
Write-Host 'Enter the same password on every client. Press Escape in this console to stop the server.'

if ($ConfigureFirewall -and $PSCmdlet.ShouldProcess("TCP $Port for $serverExecutable on Private/LocalSubnet", 'Configure the single Blasphemous co-op firewall rule')) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Firewall setup needs elevation. Open PowerShell as Administrator and repeat this command with -ConfigureFirewall, or run without that switch if a suitable rule already exists.'
    }
    if (-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        throw 'Windows NetSecurity commands are unavailable. Add one inbound TCP rule for this server executable and port, Private profile, LocalSubnet remote addresses.'
    }
    $ruleName = "Blasphemous.Coop.LAN.TCP.$Port"
    $ruleGroup = 'Blasphemous Co-op LAN'
    $existing = Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
    if ($existing -and $existing.Group -ne $ruleGroup) {
        throw "Firewall rule name $ruleName is already owned by another configuration; no rule was changed."
    }
    $settings = @{
        Direction = 'Inbound'; Action = 'Allow'; Enabled = 'True'; Profile = 'Private'
        Protocol = 'TCP'; LocalPort = $Port; Program = $serverExecutable; RemoteAddress = 'LocalSubnet'
    }
    if ($existing) {
        Set-NetFirewallRule -InputObject $existing @settings | Out-Null
    } else {
        New-NetFirewallRule -Name $ruleName -DisplayName "Blasphemous co-op LAN TCP $Port" -Group $ruleGroup @settings | Out-Null
    }
    Write-Host "Configured one app-specific Private firewall rule: $ruleName"
    $profiles = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)
    if (-not @($profiles | Where-Object { $_.NetworkCategory -eq 'Private' }).Count) {
        Write-Host 'No active Private network profile was found. This rule applies only to a trusted LAN marked Private in Windows network settings; this script does not change that setting.'
    }
}

$serverArguments = @('--port', [string]$Port, '--max-players', [string]$MaxPlayers)
if ($Password.Length -gt 0) { $serverArguments += @('--password', $Password) }
if ($PSCmdlet.ShouldProcess("$serverExecutable on TCP $Port with limit $MaxPlayers", 'Start the matching co-op server')) {
    Push-Location -LiteralPath $serverDirectoryPath
    try {
        & $serverExecutable @serverArguments
        if ($LASTEXITCODE -ne 0) { throw "The co-op server exited with code $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
}
