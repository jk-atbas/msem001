<#
.SYNOPSIS
    Sends a Wake-on-LAN Magic Packet to one or multiple mac addresses.

.DESCRIPTION
    Constructs a 102 byte large magic packet (6x 0xFF followed by 16 repetitions of the
    mac address) and sends it via UDP broadcast.

.PARAMETER MacAddress
    One or more mac addresses. Allowed are are patterns like AA:BB:CC:DD:EE:FF,
    AA-BB-CC-DD-EE-FF and AABBCCDDEEFF.

.PARAMETER Broadcast
    Broadcast address of target network. Default is 255.255.255.255.

.PARAMETER Port
    UDP target port, commonly used are port 7 or 9. Default is 9.

.PARAMETER Repeat
    Sending attempt per address. Default is 3.

.EXAMPLE
    .\Send-WakeOnLan.ps1 -MacAddress 'AA:BB:CC:DD:EE:FF' -Verbose

.EXAMPLE
    Get-Content .\clients.txt | .\Send-WakeOnLan.ps1 -Broadcast 192.168.1.255

.EXAMPLE
    Import-Csv .\clients.csv | .\Send-WakeOnLan.ps1 -WhatIf
#>

#Requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
[OutputType([void])]
param(
    [Parameter(Mandatory = $true,
               Position  = 0,
               ValueFromPipeline = $true,
               ValueFromPipelineByPropertyName = $true)]
    [ValidatePattern('^([0-9A-Fa-f]{2}[:-]?){5}[0-9A-Fa-f]{2}$')]
    [Alias('MAC', 'PhysicalAddress')]
    [string[]]$MacAddress,

    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Broadcast = '255.255.255.255',

    [Parameter(ValueFromPipelineByPropertyName = $true)]
    [ValidateRange(1, 65535)]
    [int]$Port = 9,

    [ValidateRange(1, 10)]
    [int]$Repeat = 3
)

begin {
    $udp = [System.Net.Sockets.UdpClient]::new()
    $udp.EnableBroadcast = $true
}

process {
    foreach ($address in $MacAddress) {
        $mac = $address -replace '[:-]', ''

        # 6x 0xFF and 16 times the mac
        $packet = [byte[]]::new(102)
        for ($i = 0; $i -lt 6; $i++) { $packet[$i] = 0xFF }

        $macBytes = [byte[]]::new(6)
        for ($i = 0; $i -lt 6; $i++) {
            $macBytes[$i] = [Convert]::ToByte($mac.Substring($i * 2, 2), 16)
        }
        for ($i = 1; $i -le 16; $i++) {
            [Array]::Copy($macBytes, 0, $packet, $i * 6, 6)
        }

        if ($PSCmdlet.ShouldProcess("$address via ${Broadcast}:$Port", 'Sending magic packet')) {
            try {
                for ($n = 1; $n -le $Repeat; $n++) {
                    [void]$udp.Send($packet, $packet.Length, $Broadcast, $Port)
                }
                Write-Verbose "Sent magic packet to '$address' ($Repeat x)"
            }
            catch {
                Write-Error -Message "Sending magic packet to '$address' failed: $($_.Exception.Message)" `
                            -Category ConnectionError -TargetObject $address
            }
        }
    }
}

end {
    if ($udp) { $udp.Dispose() }
}