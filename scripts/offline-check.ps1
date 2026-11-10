<#
.SYNOPSIS
  Bateria offline (F12, CA015, CA016, RNF003): executa o Meu Editor de Vídeo e verifica que o processo não abre nenhuma
  conexão de rede (TCP ou UDP, exceto loopback) enquanto é usado.

.DESCRIPTION
  Inicia o executável (ou se conecta a um processo já aberto com -ProcessId), amostra as conexões TCP e os pontos UDP do
  processo a cada -IntervalMs milissegundos durante -Seconds segundos e, se informado, roda ao mesmo tempo um roteiro de
  automação de interface (-FlowScript, por exemplo um fluxo UIA que importa mídia, aplica IA e exporta).
  Termina com código 1 se alguma conexão externa aparecer.

  Complementa os testes estáticos de privacidade (tests\MeuEditorDeVideo.UnitTests\Architecture\PrivacyTests.cs), que
  garantem que só o assembly MeuEditorDeVideo.ModelDownloads usa rede — e somente quando o usuário clica em Instalar na
  ferramenta "Baixar modelos de IA" (não exercitada por esta bateria).

.EXAMPLE
  pwsh -File scripts\offline-check.ps1 -Seconds 60
  pwsh -File scripts\offline-check.ps1 -ExePath "$env:LOCALAPPDATA\Apps\Meu Editor de Vídeo\MeuEditorDeVideo.exe" -FlowScript .\fluxo.ps1
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
if (-not $ExePath) { $ExePath = Join-Path $root 'src\MeuEditorDeVideo.App\bin\Debug\net10.0-windows\win-x64\MeuEditorDeVideo.exe' }

if ($ProcessId) {
    $proc = Get-Process -Id $ProcessId
} else {
    if (-not (Test-Path $ExePath)) { throw "Executável não encontrado: $ExePath" }
    $proc = Start-Process -FilePath $ExePath -PassThru
}
Write-Host "Monitorando o processo $($proc.Id) por $Seconds s…"

function Test-Loopback([string]$address) {
    $address -in @('127.0.0.1', '::1', '0.0.0.0', '::') -or $address.StartsWith('127.')
}

$flow = $null
if ($FlowScript) { $flow = Start-Process pwsh -ArgumentList @('-NoProfile', '-File', $FlowScript) -PassThru -WindowStyle Hidden }

$violations = [System.Collections.Generic.List[string]]::new()
$samples = 0
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline -and -not $proc.HasExited) {
    $samples++
    foreach ($c in @(Get-NetTCPConnection -OwningProcess $proc.Id -ErrorAction SilentlyContinue)) {
        if (-not (Test-Loopback $c.RemoteAddress)) {
            $violations.Add(('{0:HH:mm:ss.fff} TCP {1}:{2} -> {3}:{4} ({5})' -f (Get-Date), $c.LocalAddress, $c.LocalPort, $c.RemoteAddress, $c.RemotePort, $c.State))
        }
    }
    foreach ($u in @(Get-NetUDPEndpoint -OwningProcess $proc.Id -ErrorAction SilentlyContinue)) {
        if (-not (Test-Loopback $u.LocalAddress)) {
            $violations.Add(('{0:HH:mm:ss.fff} UDP {1}:{2}' -f (Get-Date), $u.LocalAddress, $u.LocalPort))
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
