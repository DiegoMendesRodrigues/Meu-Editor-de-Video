<#
.SYNOPSIS
  Bateria offline (F12, CA015, CA016, RNF003): executa o Meu Editor de Vídeo e verifica que o processo não abre nenhuma
  conexão de rede (TCP ou UDP, exceto loopback) enquanto é usado.

.DESCRIPTION
  Inicia o executável (ou se conecta a um processo já aberto com -ProcessId), amostra as conexões TCP e os pontos UDP do
  processo a cada -IntervalMs milissegundos durante -Seconds segundos e, se informado, roda ao mesmo tempo um roteiro de
  automação de interface (-FlowScript, por exemplo um fluxo UIA que importa mídia, aplica IA e exporta).
  Termina com código 1 se alguma conexão externa aparecer.

  Funciona no Windows, no Linux e no macOS. A amostragem usa Get-NetTCPConnection/Get-NetUDPEndpoint no Windows,
  `ss -tunap` filtrado pelo PID no Linux e `lsof -nP -i -a -p <pid>` no macOS. Sem -ExePath, usa o executável do build
  de desenvolvimento (src/MeuEditorDeVideo.Desktop/bin/Debug/net10.0/MeuEditorDeVideo, com .exe no Windows).

  Complementa os testes estáticos de privacidade (tests\MeuEditorDeVideo.UnitTests\Architecture\PrivacyTests.cs), que
  garantem que só o assembly MeuEditorDeVideo.ModelDownloads usa rede — e somente quando o usuário clica em Instalar na
  ferramenta "Baixar modelos de IA" (não exercitada por esta bateria).

.EXAMPLE
  pwsh -File scripts\offline-check.ps1 -Seconds 60
  pwsh -File scripts\offline-check.ps1 -ExePath "$env:LOCALAPPDATA\Apps\Meu Editor de Vídeo\MeuEditorDeVideo.exe" -FlowScript .\fluxo.ps1
  pwsh -File scripts/offline-check.ps1 -ExePath /opt/meu-editor-de-video/MeuEditorDeVideo
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [int]$ProcessId,
    [int]$Seconds = 45,
    [int]$IntervalMs = 250,
    [string]$FlowScript,
    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$onWindows = $PSVersionTable.PSEdition -eq 'Desktop' -or [bool](Get-Variable IsWindows -ValueOnly -ErrorAction SilentlyContinue)
$onLinux = [bool](Get-Variable IsLinux -ValueOnly -ErrorAction SilentlyContinue)
$onMac = [bool](Get-Variable IsMacOS -ValueOnly -ErrorAction SilentlyContinue)
if (-not $ExePath) {
    $exeName = if ($onWindows) { 'MeuEditorDeVideo.exe' } else { 'MeuEditorDeVideo' }
    $ExePath = Join-Path $root (Join-Path 'src' (Join-Path 'MeuEditorDeVideo.Desktop' (Join-Path 'bin' (Join-Path 'Debug' (Join-Path 'net10.0' $exeName)))))
}

if ($ProcessId) {
    $proc = Get-Process -Id $ProcessId
} else {
    if (-not (Test-Path $ExePath)) { throw "Executável não encontrado: $ExePath" }
    $proc = Start-Process -FilePath $ExePath -PassThru
}
Write-Host "Monitorando o processo $($proc.Id) por $Seconds s…"

function Test-Loopback([string]$address) {
    $address = $address.Trim('[', ']')
    # Endereço com escopo (fe80::1%lo0) e IPv4 mapeado em IPv6 (::ffff:127.0.0.1).
    $address = ($address -split '%')[0] -replace '^::ffff:', ''
    $address -in @('127.0.0.1', '::1', '0.0.0.0', '::', '*', '') -or $address.StartsWith('127.')
}

# Separa "endereço:porta" de ss/lsof, inclusive IPv6 entre colchetes ("[::1]:8080") e "*:5353".
function Split-HostPort([string]$endpoint) {
    $i = $endpoint.LastIndexOf(':')
    if ($i -lt 0) { return @($endpoint, '') }
    return @($endpoint.Substring(0, $i), $endpoint.Substring($i + 1))
}

# Conexões TCP e pontos UDP do processo, no mesmo formato nos três sistemas.
function Get-ProcessSockets([int]$procId) {
    $found = [System.Collections.Generic.List[object]]::new()
    if ($onWindows) {
        foreach ($c in @(Get-NetTCPConnection -OwningProcess $procId -ErrorAction SilentlyContinue)) {
            $found.Add([pscustomobject]@{ Proto = 'TCP'; LocalAddr = $c.LocalAddress; LocalPort = $c.LocalPort; RemoteAddr = $c.RemoteAddress; RemotePort = $c.RemotePort; State = $c.State })
        }
        foreach ($u in @(Get-NetUDPEndpoint -OwningProcess $procId -ErrorAction SilentlyContinue)) {
            $found.Add([pscustomobject]@{ Proto = 'UDP'; LocalAddr = $u.LocalAddress; LocalPort = $u.LocalPort; RemoteAddr = ''; RemotePort = ''; State = '' })
        }
    } elseif ($onLinux) {
        # ss -H -tunap: Netid State Recv-Q Send-Q Local:Port Peer:Port Process (users:(("nome",pid=123,fd=4))).
        foreach ($line in @(& ss -H -tunap 2>$null)) {
            if ($line -notmatch "pid=$procId[,)]") { continue }
            $f = $line.Trim() -split '\s+'
            if ($f.Count -lt 6) { continue }
            $local = Split-HostPort $f[4]
            $peer = Split-HostPort $f[5]
            $found.Add([pscustomobject]@{ Proto = $f[0].ToUpperInvariant(); LocalAddr = $local[0]; LocalPort = $local[1]; RemoteAddr = $peer[0]; RemotePort = $peer[1]; State = $f[1] })
        }
    } elseif ($onMac) {
        # lsof -F: um registro por arquivo aberto ("P" protocolo, "n" nome local->remoto, "TST=" estado).
        $proto = ''; $name = ''; $state = ''
        foreach ($line in @(& lsof -nP -i -a -p $procId -F fPnT 2>$null) + @('f')) {
            if ($line.StartsWith('f') -or $line.StartsWith('p')) {
                if ($name) {
                    $ends = $name -split '->'
                    $local = Split-HostPort $ends[0]
                    $peer = if ($ends.Count -gt 1) { Split-HostPort $ends[1] } else { @('', '') }
                    $found.Add([pscustomobject]@{ Proto = $proto; LocalAddr = $local[0]; LocalPort = $local[1]; RemoteAddr = $peer[0]; RemotePort = $peer[1]; State = $state })
                }
                $name = ''; $state = ''
            } elseif ($line.StartsWith('P')) { $proto = $line.Substring(1) }
            elseif ($line.StartsWith('n')) { $name = $line.Substring(1) }
            elseif ($line.StartsWith('TST=')) { $state = $line.Substring(4) }
        }
    } else {
        throw 'Sistema operacional não suportado por offline-check.ps1.'
    }
    return $found
}

$flow = $null
if ($FlowScript) { $flow = Start-Process pwsh -ArgumentList @('-NoProfile', '-File', $FlowScript) -PassThru -WindowStyle Hidden }

$violations = [System.Collections.Generic.List[string]]::new()
$samples = 0
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
    $samples++
    foreach ($c in Get-ProcessSockets $proc.Id) {
        if ($c.Proto -like 'UDP*') {
            # UDP não tem conexão: um ponto local que não seja loopback, ou um destino externo (lsof), é violação.
            if (-not (Test-Loopback $c.LocalAddr) -or ($c.RemoteAddr -and -not (Test-Loopback $c.RemoteAddr))) {
                $violations.Add(('{0:HH:mm:ss.fff} UDP {1}:{2} -> {3}:{4}' -f (Get-Date), $c.LocalAddr, $c.LocalPort, $c.RemoteAddr, $c.RemotePort))
            }
        } elseif (-not (Test-Loopback $c.RemoteAddr)) {
            $violations.Add(('{0:HH:mm:ss.fff} TCP {1}:{2} -> {3}:{4} ({5})' -f (Get-Date), $c.LocalAddr, $c.LocalPort, $c.RemoteAddr, $c.RemotePort, $c.State))
        }
    }
    Start-Sleep -Milliseconds $IntervalMs
}

if ($flow -and -not $flow.HasExited) { $flow.WaitForExit(30000) | Out-Null }
if (-not $KeepOpen -and -not $ProcessId -and -not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; if (-not $proc.WaitForExit(10000)) { $proc.Kill() } }

$unique = $violations | Sort-Object -Unique
Write-Host "Amostras: $samples"
if ($unique.Count -gt 0) {
    Write-Host "FALHA: o aplicativo usou a rede:" -ForegroundColor Red
    $unique | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host 'OK: nenhuma conexão de rede externa (TCP/UDP) durante a execução.' -ForegroundColor Green
exit 0
