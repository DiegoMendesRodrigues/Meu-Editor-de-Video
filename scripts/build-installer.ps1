# Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
<#
.SYNOPSIS
  Gera o instalador do Meu Editor de Vídeo (F12, CA022): publicação self-contained, MSI (WiX 6) e Setup.exe (Burn,
  com o Visual C++ Redistributable).

.DESCRIPTION
  1. dotnet publish do aplicativo (Release, win-x64, self-contained) em artifacts\publish.
  2. Converte LICENSE.md (GPL-3.0) em RTF para a tela de licença do instalador (artifacts\License.rtf).
  3. Compila installer\Msi\MeuEditorDeVideo.Installer.wixproj -> artifacts\installer-build\MeuEditorDeVideo-<versão>-x64.msi.
  4. Compila installer\Bundle\MeuEditorDeVideo.Setup.wixproj -> artifacts\installer-build\MeuEditorDeVideo-<versão>-Setup.exe.
  5. (Opcional) Assina executável, MSI e Setup.exe com signtool quando um certificado é informado
     (-CertificateThumbprint ou -CertificatePath/-CertificatePassword). Sem certificado, nada é assinado
     (ver documentation/INSTALADOR.md, "Assinatura de código").
  6. Copia os arquivos desta versão para artifacts\installer, sem apagar as outras, e atualiza o SHA256SUMS.txt.

  Pré-requisitos: .NET SDK 10, scripts\fetch-deps.ps1 já executado (deps\ffmpeg, deps\ai-models, deps\vcredist).
  O SDK do WiX vem do NuGet (WixToolset.Sdk 6.0.2); não é preciso instalar nada.

.EXAMPLE
  pwsh -File scripts\build-installer.ps1
  pwsh -File scripts\build-installer.ps1 -CertificateThumbprint 0123ABCD... -TimestampUrl http://timestamp.digicert.com
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipPublish,
    [string]$CertificateThumbprint,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$SignToolPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$out = Join-Path $artifacts 'installer'
# O MSBuild apaga da pasta de saída o que a compilação anterior gerou (as outras versões): compila numa pasta à parte
# e copia só os arquivos desta versão para artifacts\installer.
$build = Join-Path $artifacts 'installer-build'
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force -Path $artifacts, $out, $build | Out-Null

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Versão não encontrada em Directory.Build.props.' }
Write-Host "Meu Editor de Vídeo $version"

foreach ($dep in 'deps\ffmpeg', 'deps\ai-models\modnet-retrato', 'deps\vcredist\VC_redist.x64.exe') {
    if (-not (Test-Path (Join-Path $root $dep))) { throw "Dependência ausente: $dep. Execute scripts\fetch-deps.ps1." }
}

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe terminou com código $LASTEXITCODE." }
}

# 1. Publicação self-contained.
if (-not $SkipPublish) {
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    Invoke-Checked dotnet @('publish', (Join-Path $root 'src\MeuEditorDeVideo.Desktop\MeuEditorDeVideo.Desktop.csproj'),
        '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', $publish, '-nologo', '-v', 'q')
}
if (-not (Test-Path (Join-Path $publish 'MeuEditorDeVideo.exe'))) { throw "Publicação não encontrada em $publish." }

# O MSI leva tudo o que foi publicado, menos as pastas de runtimes excluídas por extenso em Package.wxs: uma pasta de
# outro sistema ou arquitetura que não esteja lá (ex.: depois de atualizar um pacote) interrompe a geração.
$wxs = Get-Content -Raw (Join-Path $root 'installer\Msi\Package.wxs')
$excluded = [regex]::Matches($wxs, '<Exclude Files="[^"]*runtimes\\([^\\"]+)\\\*\*"') | ForEach-Object { $_.Groups[1].Value }
$runtimes = Join-Path $publish 'runtimes'
if (Test-Path $runtimes) {
    $foreign = @(Get-ChildItem $runtimes -Directory | Where-Object { $_.Name -ne 'win-x64' -and $excluded -notcontains $_.Name })
    if ($foreign) { throw "Pastas de outros sistemas entrariam no MSI: runtimes\$($foreign.Name -join ', runtimes\'). Exclua-as por extenso em installer\Msi\Package.wxs." }
}

# 2. Licença em RTF (texto puro com escapes RTF; caracteres fora do ASCII como \uN?).
$license = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'LICENSE.md')
$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append('{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil Segoe UI;}}\viewkind4\uc1\pard\fs16 ')
foreach ($ch in $license.Replace("`r`n", "`n").ToCharArray()) {
    switch ($ch) {
        '\' { [void]$sb.Append('\\') }
        '{' { [void]$sb.Append('\{') }
        '}' { [void]$sb.Append('\}') }
        "`n" { [void]$sb.Append("\par`r`n") }
        default {
            if ([int]$ch -lt 128) { [void]$sb.Append($ch) }
            else { [void]$sb.Append('\u' + [int][int16][int]$ch + '?') }
        }
    }
}
[void]$sb.Append('}')
$licenseRtf = Join-Path $artifacts 'License.rtf'
[IO.File]::WriteAllText($licenseRtf, $sb.ToString(), [Text.Encoding]::ASCII)

# Logotipo nas telas do MSI (WixUI): faixa superior 493×58 (logo à direita) e tela de boas-vindas/conclusão 493×312
# (logo na coluna esquerda de 164 px; o texto do WixUI ocupa o lado direito).
Add-Type -AssemblyName System.Drawing
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'src\MeuEditorDeVideo.Desktop\Assets\logo-256.png'))
function Save-Bmp([string]$file, [int]$width, [int]$height, [int]$x, [int]$y, [int]$size, [System.Drawing.Color]$panel, [int]$panelWidth) {
    $bmp = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::White)
    if ($panelWidth -gt 0) { $g.FillRectangle((New-Object System.Drawing.SolidBrush $panel), 0, 0, $panelWidth, $height) }
    $g.DrawImage($logo, $x, $y, $size, $size)
    $g.Dispose(); $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}
$bannerBmp = Join-Path $artifacts 'Banner.bmp'
$dialogBmp = Join-Path $artifacts 'Dialog.bmp'
Save-Bmp $bannerBmp 493 58 (493 - 52 - 6) 3 52 ([System.Drawing.Color]::White) 0
Save-Bmp $dialogBmp 493 312 22 96 120 ([System.Drawing.Color]::FromArgb(240, 242, 247)) 164
$logo.Dispose()

# Assinatura opcional (antes de empacotar, para que o executável dentro do MSI também saia assinado).
$sign = $CertificateThumbprint -or $CertificatePath
function Invoke-Sign([string[]]$files) {
    if (-not $sign) { return }
    $tool = $SignToolPath
    if (-not $tool) {
        $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $tool) { throw 'signtool.exe não encontrado (Windows SDK). Informe -SignToolPath.' }
    $signArgs = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', '/d', 'Meu Editor de Vídeo')
    if ($CertificateThumbprint) { $signArgs += @('/sha1', $CertificateThumbprint) }
    else { $signArgs += @('/f', $CertificatePath); if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) } }
    Invoke-Checked $tool ($signArgs + $files)
}
Invoke-Sign @((Join-Path $publish 'MeuEditorDeVideo.exe'))

# 3. MSI.
$common = @('-c', $Configuration, '-nologo', '-v', 'q', "-p:Version=$version", "-p:LicenseRtf=$licenseRtf", "-p:BannerBmp=$bannerBmp", "-p:DialogBmp=$dialogBmp")
Invoke-Checked dotnet (@('build', (Join-Path $root 'installer\Msi\MeuEditorDeVideo.Installer.wixproj'),
    "-p:AppPublishDir=$publish\", '-o', $build) + $common)
$msi = Join-Path $build "MeuEditorDeVideo-$version-x64.msi"
if (-not (Test-Path $msi)) { throw "MSI não gerado: $msi" }
Invoke-Sign @($msi)

# Revisão do Setup.exe: horas desde 2026-01-01 (cresce a cada compilação em outra hora; o Burn trata a nova como
# atualização da anterior; limite de 65535 por parte da versão, cerca de 7 anos).
$bundleRevision = [int](([DateTime]::UtcNow - [DateTime]'2026-01-01').TotalHours)

# 4. Setup.exe (Burn). Com certificado, o motor do bundle é desanexado, assinado e reanexado (wix burn detach/reattach).
Invoke-Checked dotnet (@('build', (Join-Path $root 'installer\Bundle\MeuEditorDeVideo.Setup.wixproj'),
    "-p:MsiPath=$msi", "-p:BundleRevision=$bundleRevision", '-o', $build) + $common)
$setup = Join-Path $build "MeuEditorDeVideo-$version-Setup.exe"
if (-not (Test-Path $setup)) { throw "Setup.exe não gerado: $setup" }
if ($sign) {
    $wix = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\wixtoolset.sdk\6.0.2\tools\net472\x64\wix.exe') |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $wix) { throw 'wix.exe do WixToolset.Sdk 6.0.2 não encontrado no cache do NuGet.' }
    $engine = Join-Path $build 'engine.exe'
    Invoke-Checked $wix @('burn', 'detach', $setup, '-engine', $engine)
    Invoke-Sign @($engine)
    Invoke-Checked $wix @('burn', 'reattach', $setup, '-engine', $engine, '-o', $setup)
    Remove-Item $engine
    Invoke-Sign @($setup)
}

# Copia para um nome provisório e substitui: o destino pode ser um link físico do próprio arquivo compilado
# (o MSBuild cria esses links) e o Copy-Item recusa copiar um arquivo sobre ele mesmo.
Get-ChildItem $build -File -Filter "MeuEditorDeVideo-$version-*" | ForEach-Object {
    $novo = Join-Path $out "$($_.Name).novo"
    Copy-Item -LiteralPath $_.FullName -Destination $novo -Force
    Move-Item -LiteralPath $novo -Destination (Join-Path $out $_.Name) -Force
}

# SHA256SUMS.txt: recalcula os arquivos presentes e mantém as linhas das versões que já saíram da pasta.
$sums = Join-Path $out 'SHA256SUMS.txt'
$lines = @{}
if (Test-Path $sums) {
    Get-Content $sums | Where-Object { $_.Trim() } | ForEach-Object { $lines[($_.Trim() -split ' ')[0]] = $_ }
}
Get-ChildItem $out -File | Where-Object { $_.Extension -in '.msi', '.exe' } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines[$_.Name] = '{0}  {1:N1} MB  {2}' -f $_.Name, ($_.Length / 1MB), $hash
}
$lines.Keys | Sort-Object | ForEach-Object { $lines[$_] } | Tee-Object -FilePath $sums
if (-not $sign) { Write-Host 'Sem certificado: os arquivos NÃO foram assinados (o Windows SmartScreen mostrará "editor desconhecido").' }
