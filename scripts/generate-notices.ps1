#Requires -Version 7.0
<#
.SYNOPSIS
    Gera THIRD-PARTY-NOTICES.txt na raiz do repositório: componentes de terceiros
    distribuídos com o Meu Editor de Vídeo e suas licenças.
.DESCRIPTION
    Fontes (todas locais, sem rede):
      - pacotes NuGet de "dotnet list package --include-transitive" (net10.0-windows),
        com os .nuspec e os arquivos de licença do cache do NuGet;
      - binários nativos: scripts/deps.lock.json e deps/ffmpeg;
      - modelo incluído (resources/ai-models/incluidos) e catálogo de modelos
        opcionais (resources/ai-models/modelos.json + licencas/).
    A saída é determinística (sem data de geração).
#>
[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$utf8Bom   = [System.Text.UTF8Encoding]::new($true)
$repo      = Split-Path -Parent $PSScriptRoot
$appProj   = Join-Path $repo 'src/MeuEditorDeVideo.App/MeuEditorDeVideo.App.csproj'
$tfm       = 'net10.0-windows'
$licDir    = Join-Path $repo 'resources/licenses'
$aiDir     = Join-Path $repo 'resources/ai-models'
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
if (-not $OutputPath) { $OutputPath = Join-Path $repo 'THIRD-PARTY-NOTICES.txt' }

$rotuloArquivo = 'arquivo de licença do pacote'
$separador     = '- - -'
$avisos        = 0

function Write-Aviso([string]$mensagem) {
    $script:avisos++
    Write-Warning $mensagem
}

function Stop-Geracao([string]$mensagem) {
    [Console]::Error.WriteLine("ERRO: $mensagem")
    exit 1
}

function Read-Texto([string]$caminho) {
    $t = [System.IO.File]::ReadAllText($caminho)
    $t = $t.Replace("`r`n", "`n").Replace("`r", "`n")
    return $t.TrimEnd()
}

function Test-NomeLicenca([string]$nome) {
    foreach ($padrao in 'LICENSE*', 'LICENCE*', 'license*.txt', 'THIRD-PARTY-NOTICES*') {
        if ($nome -like $padrao) { return $true }
    }
    return $false
}

# Procura um arquivo com nome de licença na pasta do pacote (raiz) e uma pasta abaixo dela.
function Find-ArquivoLicenca([string]$pasta) {
    $raiz = @(Get-ChildItem -LiteralPath $pasta -File | Sort-Object Name | Where-Object { Test-NomeLicenca $_.Name })
    if ($raiz.Count -gt 0) { return $raiz[0].FullName }
    foreach ($sub in @(Get-ChildItem -LiteralPath $pasta -Directory | Sort-Object Name)) {
        $hit = @(Get-ChildItem -LiteralPath $sub.FullName -File | Sort-Object Name | Where-Object { Test-NomeLicenca $_.Name })
        if ($hit.Count -gt 0) { return $hit[0].FullName }
    }
    return $null
}

function Get-PacotesProjeto {
    $json = & dotnet list $appProj package --include-transitive --format json 2>$null
    return [pscustomobject]@{ Codigo = $LASTEXITCODE; Texto = ($json -join "`n") }
}

function Read-Nuspec([string]$id, [string]$versao) {
    $pasta = Join-Path (Join-Path $nugetRoot $id.ToLowerInvariant()) $versao
    $spec  = Join-Path $pasta ($id.ToLowerInvariant() + '.nuspec')
    if (-not (Test-Path -LiteralPath $spec -PathType Leaf)) {
        Stop-Geracao "nuspec ausente: $spec (execute dotnet restore)."
    }
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($spec)
    $m   = $xml.package.metadata
    $lic = $m.license
    return [pscustomobject]@{
        Pasta      = $pasta
        LicTipo    = if ($lic) { [string]$lic.type } else { '' }
        LicValor   = if ($lic) { ([string]$lic.InnerText).Trim() } else { '' }
        LicenseUrl = ([string]$m.licenseUrl).Trim()
        ProjectUrl = ([string]$m.projectUrl).Trim()
        Copyright  = ([string]$m.copyright).Trim()
        Autores    = ([string]$m.authors).Trim()
    }
}

function Test-TextoApache([string]$caminho) {
    $t = [System.IO.File]::ReadAllText($caminho).TrimStart()
    return ($t.Length -ge 9000) -and $t.StartsWith('Apache License') -and $t.Contains('Version 2.0, January 2004')
}

function Find-TextoApache([string[]]$pastas) {
    foreach ($pasta in $pastas) {
        $arquivos = Get-ChildItem -LiteralPath $pasta -Recurse -File |
            Where-Object { $_.Length -lt 2MB -and ($_.Extension -in '.txt', '.md', '') } |
            Sort-Object FullName
        foreach ($a in $arquivos) {
            if (Test-TextoApache $a.FullName) { return $a.FullName }
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# 1. Pacotes NuGet realmente distribuídos
# ---------------------------------------------------------------------------
$resultado = Get-PacotesProjeto
if ($resultado.Codigo -ne 0) {
    Write-Host 'dotnet list falhou; executando dotnet restore...'
    & dotnet restore $appProj | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Geracao 'dotnet restore falhou.' }
    $resultado = Get-PacotesProjeto
}
if ($resultado.Codigo -ne 0) { Stop-Geracao 'dotnet list package falhou.' }

$dados   = $resultado.Texto | ConvertFrom-Json
$pacotes = @{}
foreach ($proj in @($dados.projects)) {
    foreach ($fw in @($proj.frameworks)) {
        if ($fw.framework -ne $tfm) { continue }
        foreach ($p in @($fw.topLevelPackages) + @($fw.transitivePackages)) {
            if ($null -eq $p) { continue }
            if ($p.id -like 'Microsoft.NETCore.App*' -or $p.id -like 'Microsoft.WindowsDesktop.App*') { continue }
            $chave = '{0}|{1}' -f $p.id.ToLowerInvariant(), $p.resolvedVersion
            $pacotes[$chave] = [pscustomobject]@{ Id = [string]$p.id; Versao = [string]$p.resolvedVersion }
        }
    }
}
if ($pacotes.Count -eq 0) { Stop-Geracao "Nenhum pacote encontrado para $tfm." }

$entradas = [System.Collections.Generic.List[object]]::new()
$gruposSpdx = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($p in $pacotes.Values) {
    $ns = Read-Nuspec $p.Id $p.Versao

    $arquivo = $null
    if ($ns.LicTipo -eq 'file' -and $ns.LicValor) {
        $candidato = Join-Path $ns.Pasta ($ns.LicValor -replace '/', '\')
        if (Test-Path -LiteralPath $candidato -PathType Leaf) { $arquivo = $candidato }
        else { Write-Aviso "$($p.Id) $($p.Versao): arquivo '$($ns.LicValor)' declarado no nuspec não existe; procurando outro." }
    }
    if (-not $arquivo) { $arquivo = Find-ArquivoLicenca $ns.Pasta }

    if ($ns.LicTipo -eq 'expression' -and $ns.LicValor) { $licenca = $ns.LicValor }
    elseif ($ns.LicTipo -eq 'file' -or $arquivo) { $licenca = $rotuloArquivo }
    else {
        $licenca = '(não informada)'
        Write-Aviso "$($p.Id) $($p.Versao): nuspec sem licença e sem arquivo de licença."
    }

    $grupo = $null
    if ($ns.LicTipo -eq 'expression' -and $ns.LicValor -and -not $arquivo) {
        $grupo = $ns.LicValor
        [void]$gruposSpdx.Add($grupo)
    }

    if ($ns.Copyright) { $copyright = $ns.Copyright }
    elseif ($ns.Autores) { $copyright = "(não informado; autores: $($ns.Autores))" }
    else { $copyright = '(não informado)' }

    if ($ns.ProjectUrl) { $projeto = $ns.ProjectUrl }
    elseif ($ns.LicenseUrl) { $projeto = $ns.LicenseUrl }
    else { $projeto = '(não informado)' }

    $entradas.Add([pscustomobject]@{
        Id        = $p.Id
        Versao    = $p.Versao
        Pasta     = $ns.Pasta
        Licenca   = $licenca
        Grupo     = $grupo
        Copyright = $copyright
        Projeto   = $projeto
        Texto     = if ($arquivo) { Read-Texto $arquivo } else { $null }
    })
}
$entradas = @($entradas | Sort-Object { $_.Id.ToLowerInvariant() }, { $_.Versao })

# ---------------------------------------------------------------------------
# Textos padrão das licenças SPDX
# ---------------------------------------------------------------------------
$apacheDestino = Join-Path $licDir 'Apache-2.0.txt'
if (-not (Test-Path -LiteralPath $apacheDestino -PathType Leaf)) {
    $origem = Find-TextoApache @($entradas | ForEach-Object { $_.Pasta } | Sort-Object -Unique)
    if (-not $origem) {
        Write-Host 'Texto completo da Apache-2.0 não está nos pacotes distribuídos; procurando no cache do NuGet...'
        $origem = Find-TextoApache @($nugetRoot)
    }
    if (-not $origem) { Stop-Geracao 'Texto completo da Apache-2.0 (>= 9000 caracteres) não encontrado no cache local do NuGet.' }
    New-Item -ItemType Directory -Force -Path $licDir | Out-Null
    Copy-Item -LiteralPath $origem -Destination $apacheDestino
    Write-Host "Apache-2.0: copiado para $apacheDestino (origem: $origem)"
}

function Get-TextoPadrao([string]$spdx) {
    if ($spdx -in 'GPL-3.0', 'GPL-3.0-or-later') {
        return Read-Texto (Join-Path $repo 'LICENSE')
    }
    $arq = Join-Path $licDir "$spdx.txt"
    if (Test-Path -LiteralPath $arq -PathType Leaf) { return Read-Texto $arq }
    Write-Aviso "Texto da licença '$spdx' não encontrado em resources/licenses; usando o link SPDX."
    return "Texto completo: https://spdx.org/licenses/$spdx.html"
}

# ---------------------------------------------------------------------------
# 2. Binários nativos (FFmpeg)
# ---------------------------------------------------------------------------
$lock   = Get-Content -LiteralPath (Join-Path $repo 'scripts/deps.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$ffmpeg = @($lock.dependencies) | Where-Object { $_.name -eq 'ffmpeg' } | Select-Object -First 1
if (-not $ffmpeg) { Stop-Geracao 'FFmpeg não encontrado em scripts/deps.lock.json.' }

$ffmpegDir = Join-Path $repo 'deps/ffmpeg'
$ffmpegLic = $null
if (Test-Path -LiteralPath $ffmpegDir) {
    $ffmpegLic = @(Get-ChildItem -LiteralPath $ffmpegDir -File | Sort-Object Name |
        Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -like 'COPYING*' } | Select-Object -First 1)
    if ($ffmpegLic.Count -eq 0) {
        $ffmpegLic = @(Get-ChildItem -LiteralPath (Join-Path $ffmpegDir 'doc') -File -ErrorAction SilentlyContinue |
            Sort-Object Name | Where-Object { $_.Name -like '*LICENSE*' } | Select-Object -First 1)
    }
}
if ($ffmpegLic -and $ffmpegLic.Count -gt 0) { $textoFfmpeg = Read-Texto $ffmpegLic[0].FullName }
else {
    Write-Aviso 'Arquivo de licença do FFmpeg não encontrado em deps/ffmpeg; usando o texto da GPL-3.0 do repositório.'
    $textoFfmpeg = Read-Texto (Join-Path $repo 'LICENSE')
}

# ---------------------------------------------------------------------------
# 3. Modelo de IA incluído (MODNet)
# ---------------------------------------------------------------------------
$modnetDir = Join-Path $aiDir 'incluidos/modnet-retrato'
$modnet    = Get-Content -LiteralPath (Join-Path $modnetDir 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$modnetLicArq = Join-Path $modnetDir $modnet.licenseFile
$modnetLicTexto = if (Test-Path -LiteralPath $modnetLicArq -PathType Leaf) { Read-Texto $modnetLicArq } else { $null }
if (-not $modnetLicTexto) { Stop-Geracao "Licença do MODNet ausente: $modnetLicArq" }
$modnetCopy = [regex]::Match($modnetLicTexto, '(?m)^Copyright[^\n]*').Value.Trim()
if (-not $modnetCopy) { $modnetCopy = '(não informado)' }

# ---------------------------------------------------------------------------
# 4. Modelos de IA opcionais (catálogo)
# ---------------------------------------------------------------------------
$catalogo = Get-Content -LiteralPath (Join-Path $aiDir 'modelos.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$catalogo = @($catalogo | Sort-Object { $_.id })

# ---------------------------------------------------------------------------
# Documento
# ---------------------------------------------------------------------------
$sb = [System.Text.StringBuilder]::new()
function Add-Linha([string]$texto = '') { [void]$script:sb.Append($texto).Append("`n") }
function Add-Secao([string]$titulo) {
    Add-Linha ''
    Add-Linha $titulo
    Add-Linha ('-' * $titulo.Length)
}
function Add-Texto([string]$texto) {
    Add-Linha ''
    Add-Linha $separador
    Add-Linha $texto
    Add-Linha $separador
}

$titulo = 'MEU EDITOR DE VÍDEO — AVISOS DE TERCEIROS'
Add-Linha $titulo
Add-Linha ('=' * $titulo.Length)
Add-Linha ''
Add-Linha ('O Meu Editor de Vídeo é licenciado sob GPL-3.0-or-later (arquivo LICENSE); abaixo estão os componentes de terceiros distribuídos com o aplicativo e suas licenças. Gerado por scripts/generate-notices.ps1 — não editar à mão.')

# 1. Bibliotecas
Add-Secao '1. BIBLIOTECAS (.NET / NuGet)'
foreach ($e in $entradas) {
    Add-Linha ''
    Add-Linha "$($e.Id) $($e.Versao)"
    Add-Linha "  Licença: $($e.Licenca)"
    Add-Linha "  Copyright: $($e.Copyright)"
    Add-Linha "  Projeto: $($e.Projeto)"
    if ($e.Grupo) { Add-Linha "  Texto: ver a seção 5 (TEXTOS DAS LICENÇAS), licença $($e.Grupo)" }
    if ($e.Texto) { Add-Texto $e.Texto }
}

# 2. Binários nativos
Add-Secao '2. BINÁRIOS NATIVOS'
Add-Linha ''
Add-Linha "FFmpeg $($ffmpeg.version)"
Add-Linha '  Descrição: FFmpeg (build full_build compartilhada, GPL-3.0-or-later; inclui x264, x265, libvpx, SVT-AV1, libaom, LAME, Opus, Vorbis)'
Add-Linha '  Licença: GPL-3.0-or-later'
Add-Linha '  Copyright: FFmpeg developers'
Add-Linha '  Projeto: https://ffmpeg.org'
Add-Linha "  Origem do build: $($ffmpeg.url)"
Add-Texto $textoFfmpeg
Add-Linha ''
Add-Linha 'As bibliotecas nativas dos pacotes NuGet (OpenCvSharp, SkiaSharp, HarfBuzzSharp, ONNX Runtime, DirectML, Whisper.net) constam na seção 1.'

# 3. Modelo incluído
Add-Secao '3. MODELO DE IA INCLUÍDO'
Add-Linha ''
Add-Linha "$($modnet.name) $($modnet.version)"
Add-Linha "  Licença: $($modnet.license)"
Add-Linha "  Copyright: $modnetCopy"
Add-Linha '  Projeto: https://github.com/ZHKKKe/MODNet'
Add-Linha "  Origem: $($modnet.source)"
Add-Texto $modnetLicTexto

# 4. Modelos opcionais
Add-Secao '4. MODELOS DE IA OPCIONAIS (baixados pelo usuário em Ferramentas › Modelos de IA › Baixar modelos)'
foreach ($m in $catalogo) {
    $arqLic = Join-Path $aiDir (Join-Path 'licencas' $m.licenseFile)
    if (Test-Path -LiteralPath $arqLic -PathType Leaf) { $textoModelo = Read-Texto $arqLic }
    else {
        Write-Aviso "Licença do modelo '$($m.id)' não encontrada: $arqLic"
        $textoModelo = '(texto da licença não encontrado)'
    }
    Add-Linha ''
    Add-Linha "$($m.name) $($m.version)"
    Add-Linha "  Id: $($m.id)"
    Add-Linha "  Origem: $($m.source)"
    Add-Linha "  URL: $($m.url)"
    Add-Linha "  Licença: $($m.license)"
    Add-Texto $textoModelo
}

# 5. Textos das licenças (uma vez por identificador SPDX dos pacotes sem arquivo próprio)
Add-Secao '5. TEXTOS DAS LICENÇAS'
foreach ($spdx in $gruposSpdx) {
    $texto = Get-TextoPadrao $spdx
    Add-Linha ''
    Add-Linha "[$spdx]"
    Add-Texto $texto
}

# ---------------------------------------------------------------------------
# Gravação (UTF-8 com BOM, CRLF)
# ---------------------------------------------------------------------------
$conteudo = $sb.ToString().Replace("`r`n", "`n").Replace("`n", "`r`n")
[System.IO.File]::WriteAllText($OutputPath, $conteudo, $utf8Bom)

$licencasDistintas = @($entradas | ForEach-Object { $_.Licenca } | Sort-Object -Unique).Count
Write-Output ("Pacotes: {0} | Licenças distintas: {1} | Avisos: {2} | Saída: {3}" -f $entradas.Count, $licencasDistintas, $avisos, $OutputPath)
exit 0
