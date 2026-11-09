<#
.SYNOPSIS
  Baixa e verifica as dependências nativas fixadas em scripts/deps.lock.json (FFmpeg e o modelo MODNet incluído).

.DESCRIPTION
  Uso exclusivo em tempo de desenvolvimento/build. O aplicativo em execução nunca acessa a rede (RNF003/RNF005).
  Cada arquivo é verificado por SHA-256. Se o lock ainda não tiver hash (primeira fixação), o hash calculado
  é gravado e deve ser conferido/commitado manualmente; use -Strict no CI para recusar hashes ausentes.

.EXAMPLE
  pwsh scripts/fetch-deps.ps1
  pwsh scripts/fetch-deps.ps1 -Strict
#>
[CmdletBinding()]
param(
    [switch]$Strict,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $PSScriptRoot 'deps.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
$cache = Join-Path $root 'deps/.downloads'
New-Item -ItemType Directory -Force $cache | Out-Null
$lockChanged = $false

foreach ($dep in $lock.dependencies) {
    $target = Join-Path $root $dep.target
    $stamp = Join-Path $target '.version'
    if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq "$($dep.version)|$($dep.sha256)") -and $dep.sha256) {
        Write-Host "[ok] $($dep.name) $($dep.version) já presente."
        continue
    }

    # Arquivo avulso (sem extração) quando o lock define fileName; senão, zip.
    $name = if ($dep.fileName) { "$($dep.name)-$($dep.fileName)" } else { [IO.Path]::GetFileName(([Uri]$dep.url).AbsolutePath) }
    $file = Join-Path $cache $name
    if (-not (Test-Path $file)) {
        Write-Host "[download] $($dep.name) $($dep.version) ..."
        Invoke-WebRequest -Uri $dep.url -OutFile "$file.part" -UseBasicParsing
        Move-Item "$file.part" $file -Force
    }

    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($dep.sha256)) {
        if ($Strict) { throw "Hash ausente para $($dep.name) no lock (modo -Strict)." }
        Write-Warning "Primeira fixação de $($dep.name): sha256 = $hash (gravado no lock — confira e faça commit)."
        $dep.sha256 = $hash
        $lockChanged = $true
    }
    elseif ($hash -ne $dep.sha256.ToLowerInvariant()) {
        Remove-Item $file -Force
        throw "SHA-256 inválido para $($dep.name): esperado $($dep.sha256), obtido $hash. Arquivo descartado."
    }

    if ($dep.fileName) {
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item $file (Join-Path $target $dep.fileName)
        Set-Content $stamp "$($dep.version)|$($dep.sha256)"
        Write-Host "[ok] $($dep.name) $($dep.version)"
        continue
    }

    Write-Host "[extract] $($dep.name) -> $($dep.target)"
    $tmp = Join-Path $cache "extract-$($dep.name)"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $file -DestinationPath $tmp
    $src = if ($dep.extractSubdir) { Join-Path $tmp $dep.extractSubdir } else { $tmp }
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Move-Item $src $target
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Set-Content $stamp "$($dep.version)|$($dep.sha256)"
    Write-Host "[ok] $($dep.name) $($dep.version)"
}

if ($lockChanged) {
    $lock | ConvertTo-Json -Depth 5 | Set-Content $lockPath -Encoding utf8
}
