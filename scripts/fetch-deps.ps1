#Requires -Version 7.0
<#
.SYNOPSIS
  Prepara a pasta deps/ com as dependências nativas fixadas em scripts/deps.lock.json para o sistema em que roda
  (Windows, Linux ou macOS): FFmpeg, o modelo MODNet incluído e, no Windows, o Visual C++ Redistributable.

.DESCRIPTION
  Uso exclusivo em tempo de desenvolvimento/build. O aplicativo em execução nunca acessa a rede (RNF003/RNF005).
  Cada arquivo é verificado por SHA-256. Se o lock ainda não tiver hash (primeira fixação), o hash calculado
  é gravado e deve ser conferido/commitado manualmente; use -Strict no CI para recusar hashes ausentes.

  Cada entrada do lock pode ter "platforms" (windows, linux, macos; ausente = todos): as que não são do sistema
  em uso são ignoradas. Formatos de pacote: .zip, .tar.xz, .tar.gz, .tgz, .tar.bz2 (o tar é o do sistema; o
  Windows 11 traz o bsdtar). Entradas "kind": "source" só baixam e verificam tarballs de código-fonte; as
  "kind": "build" (FFmpeg no macOS) chamam o script indicado, que compila a partir dessas fontes (só no macOS).

  Layout resultante (o que src/AppContent.props copia para a pasta do aplicativo):
    Windows : deps/ffmpeg/bin/*.dll + ffmpeg.exe
    Linux   : deps/ffmpeg/lib/libav*.so.N (arquivos reais, sem links) + deps/ffmpeg/bin/ffmpeg
    macOS   : deps/ffmpeg/lib/libav*.N.dylib + deps/ffmpeg/bin/ffmpeg (compilados por scripts/macos/build-ffmpeg.sh)

.PARAMETER Strict
  Recusa entradas do lock sem sha256 (use no CI).

.PARAMETER Force
  Baixa/extrai/compila de novo mesmo que o carimbo (.version) indique que já está presente.

.PARAMETER Platform
  Força o sistema-alvo (windows, linux ou macos) em vez do sistema em que o script roda. Serve para testar a
  lógica de download, verificação e extração de outro sistema (use junto com -DepsDir em uma pasta temporária).
  Entradas "kind": "build" só rodam quando o alvo é o próprio sistema.

.PARAMETER DepsDir
  Pasta de destino no lugar de <repositório>/deps (os "target" do lock são relativos a ela).

.EXAMPLE
  pwsh scripts/fetch-deps.ps1
  pwsh scripts/fetch-deps.ps1 -Strict
  pwsh scripts/fetch-deps.ps1 -Platform linux -DepsDir $env:TEMP/deps-linux
#>
[CmdletBinding()]
param(
    [switch]$Strict,
    [switch]$Force,
    [ValidateSet('windows', 'linux', 'macos')]
    [string]$Platform,
    [string]$DepsDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $PSScriptRoot 'deps.lock.json'
$lock = Get-Content $lockPath -Raw | ConvertFrom-Json

$hostOs = if ($IsWindows) { 'windows' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
if (-not $Platform) { $Platform = $hostOs }
$depsRoot = if ($DepsDir) { [IO.Path]::GetFullPath($DepsDir) } else { Join-Path $root 'deps' }
$cache = Join-Path $depsRoot '.downloads'
$sourcesDir = Join-Path $cache 'sources'
New-Item -ItemType Directory -Force $cache | Out-Null
$lockChanged = $false
Write-Host "Sistema-alvo: $Platform (pasta: $depsRoot)"

# "target" do lock é relativo à raiz do repositório (deps/...); troca o prefixo deps/ pela pasta de destino.
function Resolve-Target([string]$target) {
    $rel = $target -replace '^deps[\\/]', ''
    return Join-Path $depsRoot $rel
}

function Test-Plataforma($dep) {
    $p = @($dep.platforms)
    return ($p.Count -eq 0) -or ($p[0] -eq $null) -or ($p -contains $Platform)
}

function Get-Sha256([string]$path) {
    return (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# Baixa (se preciso) e verifica o arquivo da entrada; devolve o caminho no cache.
function Get-ArquivoVerificado($dep, [string]$file) {
    if (-not (Test-Path $file)) {
        Write-Host "[download] $($dep.name) $($dep.version) ..."
        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        Invoke-WebRequest -Uri $dep.url -OutFile "$file.part" -UseBasicParsing
        Move-Item "$file.part" $file -Force
    }

    $hash = Get-Sha256 $file
    if ([string]::IsNullOrWhiteSpace($dep.sha256)) {
        if ($Strict) { throw "Hash ausente para $($dep.name) no lock (modo -Strict)." }
        Write-Warning "Primeira fixação de $($dep.name): sha256 = $hash (gravado no lock — confira e faça commit)."
        $dep.sha256 = $hash
        $script:lockChanged = $true
    }
    elseif ($hash -ne $dep.sha256.ToLowerInvariant()) {
        Remove-Item $file -Force
        throw "SHA-256 inválido para $($dep.name): esperado $($dep.sha256), obtido $hash. Arquivo descartado."
    }
    return $file
}

# Extrai .zip ou tarball (.tar.xz, .tar.gz, .tgz, .tar.bz2) em $dest. O tar preserva links simbólicos em Linux/macOS.
function Expand-Pacote([string]$file, [string]$dest) {
    New-Item -ItemType Directory -Force $dest | Out-Null
    if ($file -match '\.zip$') {
        Expand-Archive $file -DestinationPath $dest
        return
    }
    if ($file -notmatch '\.(tar(\.(xz|gz|bz2|zst))?|tgz)$') { throw "Formato de pacote não suportado: $file" }
    $saida = & tar -xf $file -C $dest 2>&1
    if ($LASTEXITCODE -ne 0) {
        # O bsdtar do Windows não cria links simbólicos de pacotes Linux/macOS (sem privilégio), mas extrai os arquivos
        # reais; o resto do script só usa arquivos reais. Em Linux/macOS qualquer falha do tar é fatal.
        if ($hostOs -eq 'windows' -and $Platform -ne 'windows') {
            Write-Warning "tar terminou com código $LASTEXITCODE (links simbólicos de outro sistema não criados no Windows; ignorado)."
        }
        else { throw "tar falhou (código $LASTEXITCODE) ao extrair ${file}: $($saida -join "`n")" }
    }
}

# Bibliotecas ELF versionadas (libX.so.N.M.P) viram arquivos reais com o nome do soname (libX.so.N); links e
# libX.so (só para compilar) são apagados. Assim o MSBuild não copia o mesmo arquivo três vezes (os links seriam
# copiados como arquivos) e o dlopen/DT_NEEDED encontra o nome que procura.
function Set-NomesSoname([string]$pasta) {
    $versionadas = @(Get-ChildItem -LiteralPath $pasta -File | Where-Object { $_.Name -match '^(lib.+\.so\.\d+)(\.\d+)+$' })
    if ($versionadas.Count -eq 0) { throw "Nenhuma biblioteca versionada (libX.so.N.M.P) em $pasta." }
    foreach ($f in $versionadas) {
        $soname = [regex]::Match($f.Name, '^(lib.+\.so\.\d+)(\.\d+)+$').Groups[1].Value
        $destino = Join-Path $pasta $soname
        if (Test-Path -LiteralPath $destino) { Remove-Item -LiteralPath $destino -Force }
        Move-Item -LiteralPath $f.FullName -Destination $destino
    }
    foreach ($f in @(Get-ChildItem -LiteralPath $pasta -Force | Where-Object { $_.Name -match '^lib.+\.so$' })) {
        Remove-Item -LiteralPath $f.FullName -Force
    }
    # Nada que sobrou pode ser link simbólico quebrado.
    foreach ($f in @(Get-ChildItem -LiteralPath $pasta -Force | Where-Object { $_.LinkType })) {
        Remove-Item -LiteralPath $f.FullName -Force
    }
}

foreach ($dep in $lock.dependencies) {
    if (-not (Test-Plataforma $dep)) { continue }
    $kind = if ($dep.kind) { $dep.kind } else { 'archive' }

    # ---- Fontes: só baixa e verifica (usadas pelo script de compilação da entrada "build") -------------------
    if ($kind -eq 'source') {
        $file = Join-Path $sourcesDir $dep.fileName
        Get-ArquivoVerificado $dep $file | Out-Null
        Write-Host "[ok] fonte $($dep.name) $($dep.version)"
        continue
    }

    $target = Resolve-Target $dep.target
    $stamp = Join-Path $target '.version'

    # ---- Compilação a partir das fontes (macOS) --------------------------------------------------------------
    if ($kind -eq 'build') {
        $scriptPath = Join-Path $root $dep.script
        $fontes = @($lock.dependencies | Where-Object { $dep.sources -contains $_.name })
        $chave = ($fontes | ForEach-Object { $_.sha256 }) -join ','
        $assinatura = "$($dep.version)|$((Get-Sha256 $scriptPath))|$chave"
        $assinaturaHash = [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($assinatura))).Replace('-', '').ToLowerInvariant()
        if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq "$($dep.version)|$assinaturaHash")) {
            Write-Host "[ok] $($dep.name) $($dep.version) já compilado."
            continue
        }
        if ($hostOs -ne $Platform) {
            Write-Warning "$($dep.name): compilação do sistema '$Platform' só roda nesse sistema (este é '$hostOs'); ignorada."
            continue
        }
        Write-Host "[build] $($dep.name) $($dep.version) -> $($dep.target) (pode levar bastante tempo) ..."
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        & bash $scriptPath $sourcesDir $target
        if ($LASTEXITCODE -ne 0) { throw "A compilação de $($dep.name) falhou (código $LASTEXITCODE)." }
        Set-Content $stamp "$($dep.version)|$assinaturaHash"
        Write-Host "[ok] $($dep.name) $($dep.version)"
        continue
    }

    # ---- Arquivo avulso ou pacote a extrair ------------------------------------------------------------------
    if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq "$($dep.version)|$($dep.sha256)") -and $dep.sha256) {
        Write-Host "[ok] $($dep.name) $($dep.version) já presente."
        continue
    }

    # Arquivo avulso (sem extração) quando o lock define fileName; senão, pacote (zip/tar).
    $name = if ($dep.fileName) { "$($dep.name)-$($dep.fileName)" } else { [IO.Path]::GetFileName(([Uri]$dep.url).AbsolutePath) }
    $file = Get-ArquivoVerificado $dep (Join-Path $cache $name)

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
    Expand-Pacote $file $tmp
    $src = if ($dep.extractSubdir) { Join-Path $tmp $dep.extractSubdir } else { $tmp }
    if (-not (Test-Path $src)) { throw "Pasta '$($dep.extractSubdir)' não encontrada no pacote de $($dep.name)." }
    foreach ($p in @($dep.prune)) {
        if ($p) { Remove-Item (Join-Path $src $p) -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if ($dep.soname) { Set-NomesSoname (Join-Path $src $dep.soname) }
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
