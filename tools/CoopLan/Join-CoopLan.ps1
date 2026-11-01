[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$HostAddress,
    [ValidateRange(1024, 65535)][int]$Port = 27051,
    [ValidateRange(1, 8)][int]$Team = 1,
    [switch]$CheckConnection
)

$ErrorActionPreference = 'Stop'
$address = $HostAddress.Trim()
if ($address.Length -eq 0 -or $address -notmatch '^[A-Za-z0-9.-]+$' -or
    [Uri]::CheckHostName($address) -notin @([UriHostNameType]::IPv4, [UriHostNameType]::Dns)) {
    throw 'Use the host IPv4 address printed by Start-CoopHost.ps1, without a port or tcp:// prefix. This client uses IPv4 or a host name.'
}
$endpoint = '{0}:{1}' -f $address, $Port
if ($endpoint.Length -gt 64) { throw 'The address and port exceed the in-game Server IP field limit of 64 characters.' }
Write-Host 'Use the same client package as the host and the other players.'
Write-Host 'Launch Play-Coop.cmd in the prepared co-op copy. Load a level, then click Multiplayer connection info at the bottom of the game window.'
Write-Host "Server IP: $endpoint"
Write-Host "Team number: $Team"
Write-Host 'Player name: choose a unique name, at most 16 characters.'
Write-Host 'Password: use the password chosen by the host.'
Write-Host 'Click Connect. Friends connect to the host LAN address; localhost refers to your own PC.'

if ($CheckConnection) {
    $client = New-Object Net.Sockets.TcpClient
    $handle = $null
    try {
        $pending = $client.BeginConnect($address, $Port, $null, $null)
        $handle = $pending.AsyncWaitHandle
        if (-not $handle.WaitOne(3000)) {
            throw "No TCP response from $endpoint within 3 seconds. Check the host is running, the address/port, the host's Private firewall rule, and LAN or guest Wi-Fi isolation."
        }
        $client.EndConnect($pending)
        Write-Host "TCP connection succeeded to $endpoint. Game version, protocol and password still need the in-game handshake."
    } catch {
        throw "LAN connection check failed: $($_.Exception.Message)"
    } finally {
        if ($handle) { $handle.Close() }
        $client.Close()
    }
}
