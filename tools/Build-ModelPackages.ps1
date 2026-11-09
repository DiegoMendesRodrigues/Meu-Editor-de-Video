#Requires -Version 5.1
<#
.SYNOPSIS
Gera os pacotes .medmodel de todos os modelos do catálogo resources\ai-models\modelos.json.

.DESCRIPTION
Os arquivos dos modelos não ficam no repositório (são distribuídos separadamente do instalador, decisão §8 nº 5).
Baixe-os manualmente das origens indicadas no catálogo/documentation\MODELOS_IA.md para uma pasta e informe-a em
-ModelsDir. Cada entrada do catálogo vira um pacote em -Output, com licença e documentação.

.EXAMPLE
.\tools\Build-ModelPackages.ps1 -ModelsDir C:\modelos -Output C:\pacotes
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ModelsDir,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$catalogDir = Join-Path $root 'resources\ai-models'
$catalog = [System.IO.File]::ReadAllText((Join-Path $catalogDir 'modelos.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $Output | Out-Null

foreach ($m in $catalog) {
    $model = Join-Path $ModelsDir $m.file
    if (-not (Test-Path -LiteralPath $model)) {
        Write-Warning "Modelo ausente, pulando: $($m.file)"
        continue
    }
    $parameters = @{}
    if ($m.parameters) { $m.parameters.PSObject.Properties | ForEach-Object { $parameters[$_.Name] = [string]$_.Value } }
    $packArgs = @{
        Model       = $model
        Id          = $m.id
        Name        = $m.name
        Version     = $m.version
        Source      = $m.source
        Capability  = $m.capability
        Purpose     = $m.purpose
        License     = $m.license
        LicenseFile = (Join-Path $catalogDir (Join-Path 'licencas' $m.licenseFile))
        MemoryMb    = [int]$m.memoryMb
        Gpu         = [bool]$m.gpu
        Parameters  = $parameters
        Output      = (Join-Path $Output ($m.id + '.medmodel'))
    }
    & (Join-Path $PSScriptRoot 'New-MedModel.ps1') @packArgs
    Write-Host "Pacote gerado: $($m.id).medmodel"
}
