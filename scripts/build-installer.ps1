<#
.SYNOPSIS
  Gera o instalador do Meu Editor de Vídeo (F12, CA022): publicação self-contained, MSI (WiX 6) e Setup.exe (Burn,
  com o Visual C++ Redistributable).

.DESCRIPTION
  1. dotnet publish do aplicativo (Release, win-x64, self-contained) em artifacts\publish.
  2. Converte LICENSE (GPL-3.0) em RTF para a tela de licença do instalador (artifacts\License.rtf).
  3. Compila installer\Msi\MeuEditorDeVideo.Installer.wixproj -> artifacts\installer\MeuEditorDeVideo-<versão>-x64.msi.
  4. Compila installer\Bundle\MeuEditorDeVideo.Setup.wixproj -> artifacts\installer\MeuEditorDeVideo-<versão>-Setup.exe.
  5. (Opcional) Assina executável, MSI e Setup.exe com signtool quando um certificado é informado
     (-CertificateThumbprint ou -CertificatePath/-CertificatePassword). Sem certificado, nada é assinado
     (ver documentation/INSTALADOR.md, "Assinatura de código").

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
New-Item -ItemType Directory -Force -Path $artifacts, $out | Out-Null

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
    Invoke-Checked dotnet @('publish', (Join-Path $root 'src\MeuEditorDeVideo.App\MeuEditorDeVideo.App.csproj'),
        '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', $publish, '-nologo', '-v', 'q')
}
if (-not (Test-Path (Join-Path $publish 'MeuEditorDeVideo.exe'))) { throw "Publicação não encontrada em $publish." }

# 2. Licença em RTF (texto puro com escapes RTF; caracteres fora do ASCII como \uN?).
$license = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'LICENSE')
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
$common = @('-c', $Configuration, '-nologo', '-v', 'q', "-p:Version=$version", "-p:LicenseRtf=$licenseRtf")
Invoke-Checked dotnet (@('build', (Join-Path $root 'installer\Msi\MeuEditorDeVideo.Installer.wixproj'),
    "-p:AppPublishDir=$publish\", '-o', $out) + $common)
$msi = Join-Path $out "MeuEditorDeVideo-$version-x64.msi"
if (-not (Test-Path $msi)) { throw "MSI não gerado: $msi" }
Invoke-Sign @($msi)

# 4. Setup.exe (Burn). Com certificado, o motor do bundle é desanexado, assinado e reanexado (wix burn detach/reattach).
Invoke-Checked dotnet (@('build', (Join-Path $root 'installer\Bundle\MeuEditorDeVideo.Setup.wixproj'),
    "-p:MsiPath=$msi", '-o', $out) + $common)
$setup = Join-Path $out "MeuEditorDeVideo-$version-Setup.exe"
if (-not (Test-Path $setup)) { throw "Setup.exe não gerado: $setup" }
if ($sign) {
    $wix = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\wixtoolset.sdk\6.0.2\tools\net*\wix.exe') |
        Select-Object -First 1 -ExpandProperty FullName
    $engine = Join-Path $out 'engine.exe'
    Invoke-Checked $wix @('burn', 'detach', $setup, '-engine', $engine)
    Invoke-Sign @($engine)
    Invoke-Checked $wix @('burn', 'reattach', $setup, '-engine', $engine, '-o', $setup)
    Remove-Item $engine
    Invoke-Sign @($setup)
}

Get-ChildItem $out -File | Where-Object { $_.Extension -in '.msi', '.exe' } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1:N1} MB  {2}' -f $_.Name, ($_.Length / 1MB), $hash
} | Tee-Object -FilePath (Join-Path $out 'SHA256SUMS.txt')
if (-not $sign) { Write-Host 'Sem certificado: os arquivos NÃO foram assinados (o Windows SmartScreen mostrará "editor desconhecido").' }
