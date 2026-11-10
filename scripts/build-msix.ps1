<#
.SYNOPSIS
  Gera o pacote MSIX do Meu Editor de Vídeo para enviar à Microsoft Store (Partner Center).

.DESCRIPTION
  1. dotnet publish do aplicativo (Release, win-x64, self-contained) em artifacts\publish (pulável com -SkipPublish).
  2. Monta o layout do pacote em artifacts\store\layout: aplicativo publicado, ícones gerados a partir de
     Imagens\Ícone (todas as escalas e tamanhos do Windows) e AppxManifest.xml (installer\Store\Package.appxmanifest
     preenchido com installer\Store\store.json e a versão de Directory.Build.props).
  3. Gera resources.pri (makepri) para o Windows escolher o ícone certo em cada tamanho.
  4. Empacota com makeappx em artifacts\store\MeuEditorDeVideo_<versão>_x64.msix.
  5. Gera artifacts\store\listagem\ (logotipo 300×300 da página da loja).

  O pacote enviado à Store NÃO precisa ser assinado: a Microsoft assina na publicação.
  Para instalar e testar localmente antes do envio, use -TestSign (certificado autoassinado; ver
  documentation/MICROSOFT_STORE.md, "Testar o pacote no seu computador").

  Pré-requisitos: .NET SDK 10, Windows SDK (makeappx, makepri, signtool) e scripts\fetch-deps.ps1 já executado.

.EXAMPLE
  pwsh -File scripts\build-msix.ps1
  pwsh -File scripts\build-msix.ps1 -SkipPublish -TestSign
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipPublish,
    [switch]$TestSign,
    [string]$StoreConfig,
    [string]$WindowsSdkBin
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$out = Join-Path $artifacts 'store'
$layout = Join-Path $out 'layout'
$priRoot = Join-Path $out 'pri'
if (-not $StoreConfig) { $StoreConfig = Join-Path $root 'installer\Store\store.json' }

function Invoke-Checked([string]$exe, [string[]]$arguments, [switch]$Quiet) {
    if ($Quiet) {
        $output = & $exe @arguments 2>&1
        if ($LASTEXITCODE -ne 0) { $output | Write-Host; throw "$(Split-Path -Leaf $exe) terminou com código $LASTEXITCODE." }
        return
    }
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $exe) terminou com código $LASTEXITCODE." }
}

function Find-SdkTool([string]$name) {
    if ($WindowsSdkBin) { return Join-Path $WindowsSdkBin $name }
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name não encontrado. Instale o Windows SDK ou informe -WindowsSdkBin." }
    $tool.FullName
}

# ---------------------------------------------------------------- Versão e identidade
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Versão não encontrada em Directory.Build.props.' }
$parts = @($version.Split('-')[0].Split('.') | ForEach-Object { [int]$_ })
while ($parts.Count -lt 3) { $parts += 0 }
# A Store exige 4 partes com a última igual a 0 (reservada para a própria Store).
$msixVersion = '{0}.{1}.{2}.0' -f $parts[0], $parts[1], $parts[2]

$store = Get-Content -Raw -Encoding UTF8 $StoreConfig | ConvertFrom-Json
$identity = @{
    IdentityName         = $store.identityName
    Publisher            = $store.publisher
    PublisherDisplayName = $store.publisherDisplayName
    DisplayName          = $store.displayName
}
$placeholders = $identity.Keys | Where-Object { "$($identity[$_])" -like 'PREENCHER*' -or -not $identity[$_] }
if ($placeholders) {
    if (-not $TestSign) {
        throw "Preencha a identidade do Partner Center em $StoreConfig ($($placeholders -join ', ')). " +
              'Ver documentation/MICROSOFT_STORE.md. Para só testar localmente, use -TestSign.'
    }
    Write-Warning 'Identidade da Store não preenchida: usando identidade de TESTE (o pacote não serve para envio).'
    $identity.IdentityName = 'MeuEditorDeVideo.Teste'
    $identity.Publisher = 'CN=Meu Editor de Video (teste local)'
    $identity.PublisherDisplayName = 'Teste local'
}
Write-Host "Meu Editor de Vídeo $msixVersion ($($identity.IdentityName))"

foreach ($dep in 'deps\ffmpeg', 'deps\ai-models\modnet-retrato') {
    if (-not (Test-Path (Join-Path $root $dep))) { throw "Dependência ausente: $dep. Execute scripts\fetch-deps.ps1." }
}

# ---------------------------------------------------------------- 1. Publicação
if (-not $SkipPublish) {
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    Invoke-Checked dotnet @('publish', (Join-Path $root 'src\MeuEditorDeVideo.App\MeuEditorDeVideo.App.csproj'),
        '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', $publish, '-nologo', '-v', 'q')
}
if (-not (Test-Path (Join-Path $publish 'MeuEditorDeVideo.exe'))) { throw "Publicação não encontrada em $publish." }

# ---------------------------------------------------------------- 2. Layout
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $layout, $priRoot | Out-Null
Copy-Item -Recurse -Force (Join-Path $publish '*') $layout
# Arquivos de depuração não vão para a Store.
Get-ChildItem $layout -Recurse -Include '*.pdb' | Remove-Item -Force

Add-Type -AssemblyName System.Drawing
$iconDir = Join-Path $root 'Imagens\Ícone'
$iconLarge = [System.Drawing.Image]::FromFile((Join-Path $iconDir 'icone.png'))
$iconSmall = [System.Drawing.Image]::FromFile((Join-Path $iconDir 'icone_pequeno.png'))

# Desenha o ícone centralizado numa tela transparente; $scale = fração da menor dimensão ocupada pelo ícone.
function Save-Icon([string]$file, [int]$width, [int]$height, [double]$scale) {
    $source = if ([Math]::Min($width, $height) * $scale -le 48) { $iconSmall } else { $iconLarge }
    $bmp = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [int][Math]::Round([Math]::Min($width, $height) * $scale)
    $g.DrawImage($source, [int](($width - $size) / 2), [int](($height - $size) / 2), $size, $size)
    $g.Dispose()
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$images = Join-Path $priRoot 'Images'
foreach ($s in @(@{ Q = 'scale-100'; F = 1 }, @{ Q = 'scale-125'; F = 1.25 }, @{ Q = 'scale-150'; F = 1.5 },
                 @{ Q = 'scale-200'; F = 2 }, @{ Q = 'scale-400'; F = 4 })) {
    $f = $s.F; $q = $s.Q
    Save-Icon "$images\Square44x44Logo.$q.png" ([int](44 * $f)) ([int](44 * $f)) 1.0
    Save-Icon "$images\Square150x150Logo.$q.png" ([int](150 * $f)) ([int](150 * $f)) 0.66
    Save-Icon "$images\Wide310x150Logo.$q.png" ([int](310 * $f)) ([int](150 * $f)) 0.66
    Save-Icon "$images\SmallTile.$q.png" ([int](71 * $f)) ([int](71 * $f)) 0.7
    Save-Icon "$images\LargeTile.$q.png" ([int](310 * $f)) ([int](310 * $f)) 0.6
    Save-Icon "$images\StoreLogo.$q.png" ([int](50 * $f)) ([int](50 * $f)) 1.0
}
# Barra de tarefas, menu Iniciar, Explorer: tamanhos exatos, com e sem placa.
foreach ($t in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    Save-Icon "$images\Square44x44Logo.targetsize-$t.png" $t $t 1.0
    Save-Icon "$images\Square44x44Logo.targetsize-${t}_altform-unplated.png" $t $t 1.0
    Save-Icon "$images\Square44x44Logo.targetsize-${t}_altform-lightunplated.png" $t $t 1.0
    Save-Icon "$images\FileLogo.targetsize-$t.png" $t $t 1.0
}
foreach ($s in 1, 2) { Save-Icon "$images\FileLogo.scale-$($s * 100).png" (44 * $s) (44 * $s) 1.0 }

$listing = Join-Path $out 'listagem'
Save-Icon "$listing\logotipo-300x300.png" 300 300 0.9
Save-Icon "$listing\logotipo-150x150.png" 150 150 0.9
Save-Icon "$listing\logotipo-71x71.png" 71 71 0.9
$iconLarge.Dispose(); $iconSmall.Dispose()

$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'installer\Store\Package.appxmanifest')
$manifest = $manifest.Replace('$Version$', $msixVersion)
foreach ($k in $identity.Keys) { $manifest = $manifest.Replace("`$$k`$", [System.Security.SecurityElement]::Escape($identity[$k])) }
if ($manifest -match '\$[A-Za-z]+\$') { throw "Marcador não preenchido no manifesto: $($Matches[0])" }
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $priRoot 'AppxManifest.xml'), $manifest, $utf8)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, $utf8)

# ---------------------------------------------------------------- 3. resources.pri (só os ícones são indexados)
$makepri = Find-SdkTool 'makepri.exe'
$priConfig = Join-Path $out 'priconfig.xml'
Invoke-Checked $makepri @('createconfig', '/cf', $priConfig, '/dq', 'pt-BR', '/pv', '10.0.0', '/o') -Quiet
# Pacote único (não bundle): todas as escalas ficam no resources.pri, sem pacotes de recursos separados.
[xml]$cfg = Get-Content -Raw $priConfig
foreach ($node in @($cfg.SelectNodes('//packaging'))) { [void]$node.ParentNode.RemoveChild($node) }
$cfg.Save($priConfig)
Invoke-Checked $makepri @('new', '/pr', $priRoot, '/cf', $priConfig, '/mn', (Join-Path $priRoot 'AppxManifest.xml'),
    '/of', (Join-Path $layout 'resources.pri'), '/o') -Quiet
Copy-Item -Recurse -Force $images (Join-Path $layout 'Images')

# ---------------------------------------------------------------- 4. Pacote
$makeappx = Find-SdkTool 'makeappx.exe'
$msix = Join-Path $out ("MeuEditorDeVideo_{0}_x64.msix" -f $msixVersion)
Invoke-Checked $makeappx @('pack', '/d', $layout, '/p', $msix, '/o') -Quiet

if ($TestSign) {
    $subject = $identity.Publisher
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
            -FriendlyName 'Meu Editor de Vídeo - teste do pacote MSIX' -CertStoreLocation Cert:\CurrentUser\My `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
    $cer = Join-Path $out 'certificado-de-teste.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    $signtool = Find-SdkTool 'signtool.exe'
    Invoke-Checked $signtool @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint, $msix) -Quiet
    Write-Host ''
    Write-Host 'Pacote assinado com certificado de TESTE. Para instalar neste computador (PowerShell como administrador):'
    Write-Host "  Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host "  Add-AppxPackage -Path '$msix'"
}

$hash = (Get-FileHash -Algorithm SHA256 $msix).Hash.ToLowerInvariant()
Write-Host ''
Write-Host "Pacote: $msix"
Write-Host "SHA-256: $hash"
Write-Host "Tamanho: $([Math]::Round((Get-Item $msix).Length / 1MB, 1)) MB"
if (-not $TestSign) { Write-Host 'Envie este arquivo em Partner Center › Envio › Pacotes (não precisa assinar).' }
