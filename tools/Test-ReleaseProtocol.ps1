param(
    [Parameter(Mandatory = $true)][string]$ReleaseDirectory,
    [ValidateRange(1, 65535)][int]$Port = 27051,
    [byte]$ExpectedProtocol = 16,
    [string]$ServerLabel = 'server under test'
)

# Tests an already-running server on localhost. Never starts or stops a process,
# changes an installed game, or opens an external connection. No SDK is needed.
$ErrorActionPreference = 'Stop'
$assemblyPath = Join-Path $ReleaseDirectory 'client\data\Blasphemous.Multiplayer.Common.dll'
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $assemblyPath))
$script:checks = 0
$script:peers = [Collections.Generic.List[object]]::new()

function Assert-Result([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw $Label }
    $script:checks++
}

function Send-Frame($Peer, [byte]$Type, [byte[]]$Body, [switch]$Fragment) {
    $frame = [Blasphemous.Multiplayer.Common.Networking.FrameCodec]::Wrap($Type, $Body)
    $stream = $Peer.Client.GetStream()
    if ($Fragment) {
        $stream.Write($frame, 0, 1)
        Start-Sleep -Milliseconds 20
        $stream.Write($frame, 1, 2)
        Start-Sleep -Milliseconds 20
        $stream.Write($frame, 3, $frame.Length - 3)
    } else {
        $stream.Write($frame, 0, $frame.Length)
    }
}

function Read-Frame($Peer, [byte]$Type, [int]$TimeoutMs = 3000, [switch]$Optional) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        for ($i = 0; $i -lt $Peer.Pending.Count; $i++) {
            if ($Peer.Pending[$i].Type -eq $Type) {
                $body = $Peer.Pending[$i].Payload
                $Peer.Pending.RemoveAt($i)
                return ,$body
            }
        }
        if ($Peer.Client.Available -gt 0) {
            $bytes = [byte[]]::new($Peer.Client.Available)
            $count = $Peer.Client.GetStream().Read($bytes, 0, $bytes.Length)
            if ($count -eq 0) { throw ('Connection closed: ' + $Peer.Name) }
            $Peer.Buffer.AddRange([byte[]]$bytes[0..($count - 1)])
            $errors = [Collections.Generic.List[string]]::new()
            $onError = [Action[string]]{ param($message) $errors.Add($message) }
            foreach ($frame in [Blasphemous.Multiplayer.Common.Networking.FrameCodec]::Drain($Peer.Buffer, $onError)) {
                $Peer.Pending.Add($frame)
            }
            if ($errors.Count -gt 0) { throw ($errors -join '; ') }
        } else {
            Start-Sleep -Milliseconds 5
        }
    }
    if ($Optional) { return $null }
    throw "Timeout waiting for packet $Type on $($Peer.Name)"
}

function New-Peer([string]$Name, [byte]$Protocol = $ExpectedProtocol, [byte]$ExpectedRefusal = 0) {
    $peer = [PSCustomObject]@{
        Name = $Name
        Client = [Net.Sockets.TcpClient]::new()
        Buffer = [Collections.Generic.List[byte]]::new()
        Pending = [Collections.Generic.List[object]]::new()
    }
    $script:peers.Add($peer)
    $peer.Client.Connect([Net.IPAddress]::Loopback, $Port)
    $peer.Client.NoDelay = $true
    $text = [Text.Encoding]::UTF8.GetBytes($Name)
    # Protocol, room name, player name, empty password, team.
    $intro = [byte[]](@($Protocol, 1, 97, $text.Length) + $text + @(0, 1))
    Send-Frame $peer 8 $intro
    $response = Read-Frame $peer 8
    Assert-Result ($response[0] -eq $ExpectedRefusal) "$Name handshake: expected $ExpectedRefusal, got $($response[0])"
    return $peer
}

function Read-Authority($Peer, [string]$ExpectedHost) {
    $body = Read-Frame $Peer 20
    [string]$scene = ''; [string]$owner = ''; [uint32]$term = 0
    $ok = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::TryReadSceneEnemyHost(
        $body, 0, [ref]$scene, [ref]$term, [ref]$owner)
    Assert-Result ($ok -and $scene -eq 'qa-room' -and $owner -eq $ExpectedHost -and $term -gt 0) 'server scene authority is valid'
    return $term
}

function Assert-NamedBody([byte[]]$Received, [string]$Name, [byte[]]$Expected, [string]$Label) {
    $prefix = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::PrefixName($Name, $Expected)
    Assert-Result ([Convert]::ToBase64String($Received) -eq [Convert]::ToBase64String($prefix)) $Label
}

try {
    Assert-Result ([Blasphemous.Multiplayer.Common.Protocol]::VERSION -eq $ExpectedProtocol) "client uses protocol $ExpectedProtocol"
    $hostPeer = New-Peer 'QAHost'
    Send-Frame $hostPeer 3 ([Text.Encoding]::UTF8.GetBytes('qa-room'))
    $term = Read-Authority $hostPeer 'QAHost'
    $guestPeer = New-Peer 'QAGuest'
    Send-Frame $guestPeer 3 ([Text.Encoding]::UTF8.GetBytes('qa-room'))
    $guestTerm = Read-Authority $guestPeer 'QAHost'
    Assert-Result ($term -eq $guestTerm) 'both peers agree on authority term'
    $observer = New-Peer 'QAObserver'
    Send-Frame $observer 3 ([Text.Encoding]::UTF8.GetBytes('qa-other-room'))
    Send-Frame $observer 12 ([byte[]]::new(6))
    [void](Read-Frame $observer 12)
    $legacy = New-Peer 'QALegacy' ($ExpectedProtocol - 1) 11
    $duplicate = New-Peer 'QAHost' $ExpectedProtocol 20

    # A new peer receives cached player positions during its handshake. The ping
    # is a stream barrier; discard those initial positions before measuring a move.
    Send-Frame $guestPeer 12 ([byte[]]::new(6))
    [void](Read-Frame $guestPeer 12)
    for ($i = $guestPeer.Pending.Count - 1; $i -ge 0; $i--) {
        if ($guestPeer.Pending[$i].Type -eq 0) { $guestPeer.Pending.RemoveAt($i) }
    }

    $position = [byte[]]([BitConverter]::GetBytes([single]12.5) + [BitConverter]::GetBytes([single]-3))
    Send-Frame $hostPeer 0 $position
    Assert-NamedBody (Read-Frame $guestPeer 0) 'QAHost' $position 'player position relay preserves values and sender'

    $sceneHash = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::SceneHash('qa-room')
    $snapshot = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::BuildEnemyBody(
        123, 'qa-enemy', 12.5, -3, 50, $true, $true, $false, $false, $false, $false,
        0, 0, 0, 123, $true, $sceneHash, $term, 1, 1)
    Send-Frame $hostPeer 13 $snapshot -Fragment
    Assert-NamedBody (Read-Frame $guestPeer 13) 'QAHost' $snapshot 'fragmented enemy snapshot relay'
    Assert-Result ($null -eq (Read-Frame $observer 13 -TimeoutMs 250 -Optional)) 'enemy state stays inside the scene'
    Send-Frame $guestPeer 13 $snapshot
    Assert-Result ($null -eq (Read-Frame $hostPeer 13 -TimeoutMs 250 -Optional)) 'guest cannot send authoritative enemy snapshots'

    $damage = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::BuildDamage(
        123, 10, 12.5, -3, 0, 0, 1, 1, 0, 'qa-enemy', $sceneHash, $term, 1)
    Send-Frame $guestPeer 15 $damage -Fragment
    Assert-NamedBody (Read-Frame $hostPeer 15) 'QAGuest' $damage 'guest damage routes to the scene host'
    Assert-Result ($null -eq (Read-Frame $observer 15 -TimeoutMs 250 -Optional)) 'damage stays inside the scene'

    $death = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::BuildDeath(
        123, 12.5, -3, 'qa-enemy', 123, $sceneHash, $term, 1, 2)
    Send-Frame $hostPeer 16 $death
    Assert-NamedBody (Read-Frame $guestPeer 16) 'QAHost' $death 'host death packet reaches the guest'
    Send-Frame $hostPeer 4 ([byte[]]@(0))
    $newTerm = Read-Authority $guestPeer 'QAGuest'
    Assert-Result ($newTerm -gt $term) 'host migration advances the authority term'
    $replayedDeath = Read-Frame $guestPeer 16
    Assert-Result ($replayedDeath.Length -gt 0) 'server replays stored scene death during migration'

    if ($ExpectedProtocol -ge 17) {
        # Discard authority notifications from earlier joins before measuring
        # the returning preferred player's effect on this scene's ownership.
        foreach ($peer in @($hostPeer, $guestPeer)) {
            Send-Frame $peer 12 ([byte[]]::new(6))
            [void](Read-Frame $peer 12)
            for ($i = $peer.Pending.Count - 1; $i -ge 0; $i--) {
                if ($peer.Pending[$i].Type -eq 20) { $peer.Pending.RemoveAt($i) }
            }
        }
        Send-Frame $hostPeer 3 ([Text.Encoding]::UTF8.GetBytes('qa-room'))
        $continuingTerm = Read-Authority $guestPeer 'QAGuest'
        $returningTerm = Read-Authority $hostPeer 'QAGuest'
        Assert-Result ($continuingTerm -eq $newTerm -and $returningTerm -eq $newTerm) 'returning preferred player does not preempt the incumbent or advance its term'
        $survivingState = [Blasphemous.Multiplayer.Common.Networking.EnemySyncCodec]::BuildEnemyBody(
            124, 'qa-living-enemy', 9.5, -3, 37, $true, $true, $false, $false, $false, $false,
            0, 0, 0, 124, $true, $sceneHash, $newTerm, 1, 1)
        Send-Frame $guestPeer 13 $survivingState
        Assert-NamedBody (Read-Frame $hostPeer 13) 'QAGuest' $survivingState 'returning player receives the incumbent living-enemy health and pose'
        Send-Frame $guestPeer 4 ([byte[]]@(0))
        $replacementTerm = Read-Authority $hostPeer 'QAHost'
        Assert-Result ($replacementTerm -gt $newTerm) 'preferred player becomes replacement when the incumbent leaves'
    }

    Write-Output "ReleaseProtocolSmoke: PASS ($script:checks checks; protocol $ExpectedProtocol; $ServerLabel; scripted TCP peers, no Unity gameplay)"
} finally {
    foreach ($peer in $script:peers) { $peer.Client.Dispose() }
}
