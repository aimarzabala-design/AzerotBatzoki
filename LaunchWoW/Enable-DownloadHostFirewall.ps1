#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$displayName = 'AzerotBatzoki client HTTPS 8788'
$rules = @(Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue)
if ($rules.Count -gt 0) {
    $valid = foreach ($rule in $rules) {
        $port = $rule | Get-NetFirewallPortFilter
        $address = $rule | Get-NetFirewallAddressFilter
        if ($rule.Enabled -eq 'True' -and $rule.Direction -eq 'Inbound' -and
            $rule.Action -eq 'Allow' -and $rule.Profile.ToString() -eq 'Private' -and
            $port.Protocol -eq 'TCP' -and $port.LocalPort -eq '8788' -and
            $address.RemoteAddress -contains 'LocalSubnet') { $rule }
    }
    if (-not $valid) {
        throw "Ya existe una regla '$displayName' con parámetros distintos; revísala manualmente."
    }
    Write-Output 'La regla privada para TCP 8788 ya está configurada.'
    exit 0
}

New-NetFirewallRule -DisplayName $displayName -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort 8788 -Profile Private -RemoteAddress LocalSubnet | Out-Null
Write-Output 'Regla creada: TCP 8788, perfil privado, solo LocalSubnet.'
