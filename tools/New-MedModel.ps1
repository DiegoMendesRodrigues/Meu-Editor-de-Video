#Requires -Version 5.1
<#
.SYNOPSIS
Empacota um modelo de IA em um pacote .medmodel (zip) para instalação no Meu Editor de Vídeo.

.DESCRIPTION
Gera o manifest.json (campos em camelCase, idênticos ao registro ModelManifest), calcula o SHA-256 e o tamanho do
arquivo do modelo e cria um arquivo .medmodel com todos os arquivos na raiz do zip:

  manifest.json   manifesto do modelo
  <modelo>        o arquivo do modelo (.onnx = ONNX, .bin = GGML)
  LICENSE.txt     texto da licença (quando -LicenseFile é informado)
  README.md       documentação (-Readme) ou um README gerado a partir dos metadados

O formato é derivado da extensão do modelo. A licença é obrigatória. Um pacote existente em -Output é substituído.

.PARAMETER Model
Caminho do arquivo do modelo (.onnx ou .bin).

.PARAMETER Id
Identificador estável do modelo (minúsculas, números, '.', '_' ou '-'; 2 a 64 caracteres). Vira o nome da pasta instalada.

.PARAMETER Name
Nome exibido ao usuário.

.PARAMETER Version
Versão do modelo (ex.: 1.0.0). Quando há mais de uma versão com a mesma capacidade, a mais alta é usada.

.PARAMETER Source
Fabricante ou origem do modelo (autores ou projeto de onde foi obtido).

.PARAMETER Capability
Função do modelo: PersonSegmentation, SpeechRecognition, SpeechEnhancement, VoiceActivity, SceneDetection ou ImageEnhancement.

.PARAMETER Purpose
Finalidade em linguagem simples, exibida ao usuário.

.PARAMETER License
Licença do modelo (identificador SPDX ou descrição). Obrigatória.

.PARAMETER LicenseFile
Arquivo com o texto da licença; será incluído no pacote como LICENSE.txt.

.PARAMETER Readme
Arquivo de documentação; será incluído no pacote como README.md. Se omitido, um README em pt-BR é gerado.

.PARAMETER MemoryMb
Memória estimada, em MB, necessária durante a execução. Padrão: 512.

.PARAMETER Gpu
Indica que o modelo pode usar a GPU (DirectML). A CPU é sempre suportada.

.PARAMETER Parameters
Tabela de parâmetros específicos do modelo (ex.: @{ inputSize = '512'; language = 'pt' }).

.PARAMETER Output
Caminho do arquivo .medmodel a ser criado.

.EXAMPLE
.\tools\New-MedModel.ps1 -Model C:\modelos\modnet.onnx -Id modnet-photographic -Name "MODNet (fotográfico)" `
    -Version 1.0.0 -Source "ZHKKKe/MODNet" -Capability PersonSegmentation -Purpose "Remoção de fundo" `
    -License "Apache-2.0" -LicenseFile C:\modelos\LICENSE.txt -Gpu -Parameters @{ inputSize = '512' } `
    -Output C:\pacotes\modnet-photographic.medmodel
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Model,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9][a-z0-9._-]{1,63}$')]
    [string]$Id,

    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [ValidateSet('PersonSegmentation', 'SpeechRecognition', 'SpeechEnhancement', 'VoiceActivity', 'SceneDetection', 'ImageEnhancement')]
    [string]$Capability,

    [Parameter(Mandatory = $true)]
    [string]$Purpose,

    [Parameter(Mandatory = $true)]
    [string]$License,

    [string]$LicenseFile,

    [string]$Readme,

    [int]$MemoryMb = 512,

    [switch]$Gpu,

    [hashtable]$Parameters = @{},

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'

# Codificação UTF-8 sem BOM para os arquivos de texto do pacote.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# ---------- validação das entradas ----------

if (-not (Test-Path -LiteralPath $Model -PathType Leaf)) { throw "Arquivo do modelo não encontrado: $Model" }
$modelPath = (Resolve-Path -LiteralPath $Model).Path
$fileName = [System.IO.Path]::GetFileName($modelPath)

switch ([System.IO.Path]::GetExtension($modelPath).ToLowerInvariant()) {
    '.onnx' { $format = 'ONNX' }
    '.bin'  { $format = 'GGML' }
    default { throw "Extensão de modelo não suportada: '$([System.IO.Path]::GetExtension($modelPath))'. Use .onnx (ONNX) ou .bin (GGML)." }
}

$reservedNames = @('manifest.json', 'LICENSE.txt', 'README.md')
if ($reservedNames -contains $fileName) { throw "O nome '$fileName' é reservado no pacote. Renomeie o arquivo do modelo." }

if ([string]::IsNullOrWhiteSpace($License)) { throw 'A licença do modelo é obrigatória.' }

if ($LicenseFile -and -not (Test-Path -LiteralPath $LicenseFile -PathType Leaf)) { throw "Arquivo de licença não encontrado: $LicenseFile" }
if ($Readme -and -not (Test-Path -LiteralPath $Readme -PathType Leaf)) { throw "Arquivo de documentação não encontrado: $Readme" }

$outputPath = [System.IO.Path]::GetFullPath($Output)
$outputDir = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not (Test-Path -LiteralPath $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

# ---------- metadados ----------

if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
    $sha256 = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
else {
    # Fallback para ambientes sem o módulo Microsoft.PowerShell.Utility completo.
    $hashStream = [System.IO.File]::OpenRead($modelPath)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $sha256 = ([BitConverter]::ToString($sha.ComputeHash($hashStream)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $hashStream.Dispose()
    }
}
$sizeBytes = [long](Get-Item -LiteralPath $modelPath).Length

$paramsOrdered = [ordered]@{}
foreach ($key in $Parameters.Keys) { $paramsOrdered[[string]$key] = [string]$Parameters[$key] }

$licenseEntry = $null
if ($LicenseFile) { $licenseEntry = 'LICENSE.txt' }

$manifest = [ordered]@{
    id          = $Id
    name        = $Name
    version     = $Version
    source      = $Source
    capability  = $Capability
    purpose     = $Purpose
    format      = $format
    file        = $fileName
    sizeBytes   = $sizeBytes
    memoryMb    = $MemoryMb
    cpu         = $true
    gpu         = [bool]$Gpu
    license     = $License
    licenseFile = $licenseEntry
    sha256      = $sha256
    documentation = 'README.md'
    packageFormat = 1
    parameters  = $paramsOrdered
}

# ---------- montagem em pasta temporária ----------

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('medmodel-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    $manifestPath = Join-Path $staging 'manifest.json'
    $json = $manifest | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($manifestPath, $json, $utf8NoBom)

    $readmePath = Join-Path $staging 'README.md'
    if ($Readme) {
        Copy-Item -LiteralPath $Readme -Destination $readmePath
    }
    else {
        $paramLines = @()
        foreach ($key in $paramsOrdered.Keys) { $paramLines += ('- `{0}`: {1}' -f $key, $paramsOrdered[$key]) }
        if ($paramLines.Count -eq 0) { $paramLines = @('- (nenhum)') }

        $gpuText = if ($Gpu) { 'sim (DirectML), com retorno para CPU' } else { 'não (somente CPU)' }
        $readmeLines = @(
            "# $Name",
            '',
            "**Finalidade:** $Purpose",
            '',
            "**Origem:** $Source",
            '',
            "**Versão:** $Version",
            '',
            "**Licença:** $License",
            '',
            '## Requisitos',
            '',
            "- Memória estimada: $MemoryMb MB",
            "- Usa GPU: $gpuText",
            '',
            '## Parâmetros',
            ''
        ) + $paramLines + @(
            '',
            '## Instalação',
            '',
            'Importe este pacote pelo gerenciador de modelos do Meu Editor de Vídeo. O pacote não é baixado de forma automática.'
        )
        [System.IO.File]::WriteAllText($readmePath, ($readmeLines -join "`r`n"), $utf8NoBom)
    }

    $entries = New-Object System.Collections.Generic.List[object]
    $entries.Add([pscustomobject]@{ Path = $manifestPath; Name = 'manifest.json' })
    $entries.Add([pscustomobject]@{ Path = $modelPath; Name = $fileName })
    if ($LicenseFile) { $entries.Add([pscustomobject]@{ Path = (Resolve-Path -LiteralPath $LicenseFile).Path; Name = 'LICENSE.txt' }) }
    $entries.Add([pscustomobject]@{ Path = $readmePath; Name = 'README.md' })

    # ---------- zip ----------

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }

    $zip = [System.IO.Compression.ZipFile]::Open($outputPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Path, $entry.Name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

$packageSize = [math]::Round((Get-Item -LiteralPath $outputPath).Length / 1MB, 2)
Write-Host "Pacote criado: $outputPath ($packageSize MB)"
Write-Host "SHA-256 do modelo: $sha256"
