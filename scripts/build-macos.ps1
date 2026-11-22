<#
.SYNOPSIS
  Gera o aplicativo do macOS ("Meu Editor de Vídeo.app") e o instalador .dmg (osx-arm64 ou osx-x64).

.DESCRIPTION
  1. dotnet publish do aplicativo (Release, osx-arm64 ou osx-x64, self-contained) em artifacts\publish-<rid>.
  2. Monta "Meu Editor de Vídeo.app":
       Contents/Info.plist
       Contents/MacOS/        pasta inteira da publicação (MeuEditorDeVideo, ffmpeg/, libOpenCvSharpExtern.dylib, i18n/, ajuda/...)
       Contents/Resources/    MeuEditorDeVideo.icns e <idioma>.lproj/InfoPlist.strings (textos das permissões)
  3. -Sign: codesign (runtime reforçado, com carimbo de tempo) em cada Mach-O de dentro para fora e depois no pacote,
     com packaging\macos\MeuEditorDeVideo.entitlements. Identidade em $env:MED_MACOS_SIGN_IDENTITY
     (por exemplo "Developer ID Application: Nome (TEAMID)").
  4. -Notarize (implica -Sign): envia o app (zip) ao notarytool, grampeia o ticket, cria o .dmg, assina, notariza e
     grampeia o .dmg. Perfil do chaveiro em $env:MED_MACOS_NOTARY_PROFILE (xcrun notarytool store-credentials); se estiver
     num chaveiro que não é o padrão, o caminho vai em $env:MED_MACOS_NOTARY_KEYCHAIN.
  5. Saída em artifacts\macos (-OutDir):
       macOS:         MeuEditorDeVideo-<versão>-<arch>.dmg (UDZO, com atalho para /Applications) — assinado ou não
       outros sistemas: MeuEditorDeVideo-<versão>-<arch>-app.zip (só para conferir a estrutura; veja o aviso abaixo)
     e SHA256SUMS.txt.

  Fora do macOS só é possível montar a pasta .app e um .zip com os bits de execução gravados (ExternalAttributes).
  Não há codesign, plutil, iconutil nem hdiutil: o resultado NÃO é um aplicativo utilizável e não deve ser distribuído.
  Os modos de execução (chmod +x) só valem no macOS ou dentro do .zip gerado aqui.

  Nenhum segredo fica no script: identidade e perfil de notarização vêm de variáveis de ambiente.
  A permissão de gravação de tela não tem chave no Info.plist; o macOS pergunta sozinho na primeira gravação.

.EXAMPLE
  pwsh -File scripts/build-macos.ps1 -Rid osx-arm64
  $env:MED_MACOS_SIGN_IDENTITY = 'Developer ID Application: Fulano (ABCDE12345)'; $env:MED_MACOS_NOTARY_PROFILE = 'med-notary'
  pwsh -File scripts/build-macos.ps1 -Rid osx-arm64 -Sign -Notarize
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string]$Rid = 'osx-arm64',
    [switch]$SkipPublish,
    [string]$PublishDir,
    [string]$OutDir,
    [switch]$Sign,
    [switch]$Notarize,
    [string]$BundleId = 'online.aplicativoia.meueditordevideo'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
if (-not $PublishDir) { $PublishDir = Join-Path $artifacts "publish-$Rid" }
if (-not $OutDir) { $OutDir = Join-Path $artifacts 'macos' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$isMac = $IsMacOS
$arch = if ($Rid -eq 'osx-arm64') { 'arm64' } else { 'x64' }
if ($Notarize) { $Sign = $true }

$AppName = 'Meu Editor de Vídeo'
$ExeName = 'MeuEditorDeVideo'
$IconName = 'MeuEditorDeVideo'
# Identificador: o mesmo domínio do serviço de versões (aplicativoia.online) ao contrário. O tipo de arquivo .med usa o
# mesmo prefixo. Mudar o identificador depois de publicar faz o macOS tratar o app como outro (permissões, preferências).
$MinimumSystem = '12.0'   # mínimo do .NET 10 no macOS

[xml]$props = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Directory.Build.props')
$Version = (@($props.Project.PropertyGroup) | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $Version) { throw 'Versão não encontrada em Directory.Build.props.' }
$plistVersion = $Version -replace '[-+].*$', ''
Write-Host "Meu Editor de Vídeo $Version ($Rid)"

$signIdentity = $env:MED_MACOS_SIGN_IDENTITY
$notaryProfile = $env:MED_MACOS_NOTARY_PROFILE
if ($Sign) {
    if (-not $isMac) { throw '-Sign e -Notarize exigem o macOS (codesign).' }
    if (-not $signIdentity) { throw 'Defina MED_MACOS_SIGN_IDENTITY (identidade "Developer ID Application: ...") para usar -Sign.' }
}
if ($Notarize -and -not $notaryProfile) { throw 'Defina MED_MACOS_NOTARY_PROFILE (perfil criado com "xcrun notarytool store-credentials") para usar -Notarize.' }

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe terminou com código $LASTEXITCODE." }
}

# --- 1. Publicação -----------------------------------------------------------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    Invoke-Checked dotnet @('publish', (Join-Path $root 'src/MeuEditorDeVideo.Desktop/MeuEditorDeVideo.Desktop.csproj'),
        '-c', $Configuration, '-r', $Rid, '--self-contained', 'true', '-p:MedPlataforma=macos', '-o', $PublishDir, '-nologo', '-v', 'q')
}
if (-not (Test-Path (Join-Path $PublishDir $ExeName))) { throw "Publicação não encontrada em $PublishDir (falta $ExeName)." }
$PublishDir = (Resolve-Path $PublishDir).Path

function Test-MachO([string]$file) {
    $fs = [IO.File]::OpenRead($file)
    try {
        $b = [byte[]]::new(4)
        if ($fs.Read($b, 0, 4) -ne 4) { return $false }
        # FEEDFACE/FEEDFACF (big endian), CEFAEDFE/CFFAEDFE (little endian) e fat binary CAFEBABE.
        return [BitConverter]::ToString($b) -in @('FE-ED-FA-CE', 'FE-ED-FA-CF', 'CE-FA-ED-FE', 'CF-FA-ED-FE', 'CA-FE-BA-BE')
    } finally { $fs.Dispose() }
}

# --- 2. Estrutura do .app ------------------------------------------------------------------------------------------
$appDir = Join-Path $OutDir "$AppName.app"
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
$contents = Join-Path $appDir 'Contents'
$macOsDir = Join-Path $contents 'MacOS'
$resources = Join-Path $contents 'Resources'
New-Item -ItemType Directory -Force -Path $macOsDir, $resources | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $macOsDir -Recurse -Force

# Bits de execução: todo Mach-O (programa, ffmpeg, .dylib).
$machoFiles = [System.Collections.Generic.List[string]]::new()
foreach ($f in Get-ChildItem -LiteralPath $macOsDir -Recurse -File -Force | Sort-Object FullName) {
    if (Test-MachO $f.FullName) { $machoFiles.Add($f.FullName) }
}
if ($machoFiles.Count -eq 0) { Write-Warning 'Nenhum arquivo Mach-O na publicação (esperado em uma publicação real para macOS).' }
if (-not $IsWindows) {
    foreach ($f in $machoFiles) { [IO.File]::SetUnixFileMode($f, [IO.UnixFileMode]([Convert]::ToInt32('755', 8))) }
}

# Ícone .icns.
function New-Icns([string]$destination) {
    $assets = Join-Path $root 'src/MeuEditorDeVideo.Desktop/Assets'
    $png256 = Join-Path $assets 'logo-256.png'
    $png64 = Join-Path $assets 'logo-64.png'
    if ($isMac -and (Get-Command iconutil -ErrorAction SilentlyContinue) -and (Get-Command sips -ErrorAction SilentlyContinue)) {
        $set = Join-Path ([IO.Path]::GetTempPath()) "med-$([guid]::NewGuid().ToString('N')).iconset"
        New-Item -ItemType Directory -Force -Path $set | Out-Null
        try {
            Copy-Item $png256 (Join-Path $set 'icon_256x256.png')
            Copy-Item $png256 (Join-Path $set 'icon_128x128@2x.png')
            Copy-Item $png64 (Join-Path $set 'icon_32x32@2x.png')
            foreach ($s in 128, 32, 16) { Invoke-Checked sips @('-z', $s, $s, $png256, '--out', (Join-Path $set "icon_${s}x${s}.png")) | Out-Null }
            Copy-Item (Join-Path $set 'icon_32x32.png') (Join-Path $set 'icon_16x16@2x.png')
            Invoke-Checked iconutil @('-c', 'icns', $set, '-o', $destination)
            return
        } catch { Write-Warning "iconutil falhou ($($_.Exception.Message)); usando o gravador próprio." }
        finally { Remove-Item -Recurse -Force $set -ErrorAction SilentlyContinue }
    }
    # Gravador próprio: contêiner ICNS (assinatura 'icns' + tamanho, depois blocos tipo+tamanho+dados, tudo big endian).
    # Blocos com PNG: ic08 = 256x256 e ic12 = 32x32@2x (64x64).
    $blocks = @(@('ic08', $png256), @('ic12', $png64))
    $ms = [IO.MemoryStream]::new()
    $w = [IO.BinaryWriter]::new($ms)
    function Write-BigEndian([IO.BinaryWriter]$bw, [int]$value) { $bw.Write([byte[]]@([byte](($value -shr 24) -band 255), [byte](($value -shr 16) -band 255), [byte](($value -shr 8) -band 255), [byte]($value -band 255))) }
    $payloads = $blocks | ForEach-Object { , @($_[0], [IO.File]::ReadAllBytes($_[1])) }
    $total = 8 + ($payloads | ForEach-Object { 8 + $_[1].Length } | Measure-Object -Sum).Sum
    $w.Write([Text.Encoding]::ASCII.GetBytes('icns')); Write-BigEndian $w $total
    foreach ($p in $payloads) { $w.Write([Text.Encoding]::ASCII.GetBytes($p[0])); Write-BigEndian $w (8 + $p[1].Length); $w.Write($p[1]) }
    $w.Flush()
    [IO.File]::WriteAllBytes($destination, $ms.ToArray())
}
New-Icns (Join-Path $resources "$IconName.icns")

# Info.plist.
function Escape-Xml([string]$s) { [Security.SecurityElement]::Escape($s) }
$copyright = "Copyright © $((Get-Date).Year) Diego Mendes Rodrigues — GPL-3.0-or-later"
$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleName</key>
    <string>$(Escape-Xml $AppName)</string>
    <key>CFBundleDisplayName</key>
    <string>$(Escape-Xml $AppName)</string>
    <key>CFBundleIdentifier</key>
    <string>$(Escape-Xml $BundleId)</string>
    <key>CFBundleExecutable</key>
    <string>$ExeName</string>
    <key>CFBundleIconFile</key>
    <string>$IconName</string>
    <key>CFBundleShortVersionString</key>
    <string>$plistVersion</string>
    <key>CFBundleVersion</key>
    <string>$plistVersion</string>
    <key>CFBundleDevelopmentRegion</key>
    <string>pt-BR</string>
    <key>CFBundleLocalizations</key>
    <array>
        <string>pt-BR</string>
        <string>pt-PT</string>
        <string>en</string>
        <string>es</string>
        <string>fr</string>
        <string>de</string>
    </array>
    <key>LSMinimumSystemVersion</key>
    <string>$MinimumSystem</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.video</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSHumanReadableCopyright</key>
    <string>$(Escape-Xml $copyright)</string>
    <key>NSCameraUsageDescription</key>
    <string>O Meu Editor de Vídeo usa a câmera apenas para gravar vídeo da webcam quando você pede, em Ferramentas &gt; Gravar tela. Nada é enviado para a internet.</string>
    <key>NSMicrophoneUsageDescription</key>
    <string>O Meu Editor de Vídeo usa o microfone apenas para gravar áudio e narração quando você pede. Nada é enviado para a internet.</string>
    <key>CFBundleDocumentTypes</key>
    <array>
        <dict>
            <key>CFBundleTypeName</key>
            <string>Projeto do Meu Editor de Vídeo</string>
            <key>CFBundleTypeRole</key>
            <string>Editor</string>
            <key>LSHandlerRank</key>
            <string>Owner</string>
            <key>LSItemContentTypes</key>
            <array>
                <string>$(Escape-Xml $BundleId).med</string>
            </array>
            <key>CFBundleTypeIconFile</key>
            <string>$IconName</string>
        </dict>
    </array>
    <key>UTExportedTypeDeclarations</key>
    <array>
        <dict>
            <key>UTTypeIdentifier</key>
            <string>$(Escape-Xml $BundleId).med</string>
            <key>UTTypeDescription</key>
            <string>Projeto do Meu Editor de Vídeo</string>
            <key>UTTypeConformsTo</key>
            <array>
                <string>public.zip-archive</string>
                <string>public.data</string>
            </array>
            <key>UTTypeIconFile</key>
            <string>$IconName</string>
            <key>UTTypeTagSpecification</key>
            <dict>
                <key>public.filename-extension</key>
                <array>
                    <string>med</string>
                </array>
                <key>public.mime-type</key>
                <string>application/x-meu-editor-de-video</string>
            </dict>
        </dict>
    </array>
</dict>
</plist>
"@
$utf8 = [Text.UTF8Encoding]::new($false)
$plistPath = Join-Path $contents 'Info.plist'
[IO.File]::WriteAllText($plistPath, $infoPlist.Replace("`r`n", "`n") + "`n", $utf8)

# Textos das permissões e nome do app por idioma (InfoPlist.strings; a Info.plist acima é o texto em pt-BR).
$strings = [ordered]@{
    'pt-BR' = @('Meu Editor de Vídeo', 'O Meu Editor de Vídeo usa a câmera apenas para gravar vídeo da webcam quando você pede, em Ferramentas > Gravar tela. Nada é enviado para a internet.', 'O Meu Editor de Vídeo usa o microfone apenas para gravar áudio e narração quando você pede. Nada é enviado para a internet.')
    'pt-PT' = @('Meu Editor de Vídeo', 'O Meu Editor de Vídeo usa a câmara apenas para gravar vídeo da webcam quando o pede, em Ferramentas > Gravar ecrã. Nada é enviado para a internet.', 'O Meu Editor de Vídeo usa o microfone apenas para gravar áudio e narração quando o pede. Nada é enviado para a internet.')
    'en'    = @('Meu Editor de Vídeo', 'Meu Editor de Vídeo uses the camera only to record webcam video when you ask it to, under Tools > Record screen. Nothing is sent over the internet.', 'Meu Editor de Vídeo uses the microphone only to record audio and voice-overs when you ask it to. Nothing is sent over the internet.')
    'es'    = @('Meu Editor de Vídeo', 'Meu Editor de Vídeo usa la cámara solo para grabar vídeo de la webcam cuando usted lo pide, en Herramientas > Grabar pantalla. No se envía nada a internet.', 'Meu Editor de Vídeo usa el micrófono solo para grabar audio y locuciones cuando usted lo pide. No se envía nada a internet.')
    'fr'    = @('Meu Editor de Vídeo', 'Meu Editor de Vídeo utilise la caméra uniquement pour enregistrer la vidéo de la webcam à votre demande, dans Outils > Enregistrer l''écran. Rien n''est envoyé sur internet.', 'Meu Editor de Vídeo utilise le microphone uniquement pour enregistrer le son et les voix off à votre demande. Rien n''est envoyé sur internet.')
    'de'    = @('Meu Editor de Vídeo', 'Meu Editor de Vídeo verwendet die Kamera nur, um auf Ihren Wunsch Webcam-Video aufzunehmen (Werkzeuge > Bildschirm aufnehmen). Es wird nichts ins Internet gesendet.', 'Meu Editor de Vídeo verwendet das Mikrofon nur, um auf Ihren Wunsch Audio und Sprecherkommentare aufzunehmen. Es wird nichts ins Internet gesendet.')
}
function Escape-Strings([string]$s) { $s.Replace('\', '\\').Replace('"', '\"') }
foreach ($lang in $strings.Keys) {
    $dir = Join-Path $resources "$lang.lproj"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $v = $strings[$lang]
    $text = "CFBundleDisplayName = `"$(Escape-Strings $v[0])`";`nCFBundleName = `"$(Escape-Strings $v[0])`";`nNSCameraUsageDescription = `"$(Escape-Strings $v[1])`";`nNSMicrophoneUsageDescription = `"$(Escape-Strings $v[2])`";`n"
    [IO.File]::WriteAllText((Join-Path $dir 'InfoPlist.strings'), $text, $utf8)
}

# Validação do plist: plutil no macOS; fora dele, confere que é XML bem formado com as chaves esperadas.
if ($isMac -and (Get-Command plutil -ErrorAction SilentlyContinue)) {
    Invoke-Checked plutil @('-lint', $plistPath)
    foreach ($lang in $strings.Keys) { Invoke-Checked plutil @('-lint', (Join-Path $resources "$lang.lproj/InfoPlist.strings")) }
    Invoke-Checked plutil @('-lint', (Join-Path $root 'packaging/macos/MeuEditorDeVideo.entitlements'))
} else {
    foreach ($x in $plistPath, (Join-Path $root 'packaging/macos/MeuEditorDeVideo.entitlements')) {
        $doc = [xml](Get-Content -Raw -Encoding UTF8 $x)
        if ($doc.plist.dict.key.Count -lt 3) { throw "Plist inesperado: $x" }
    }
    Write-Warning 'plutil indisponível (não é macOS): Info.plist conferido apenas como XML bem formado.'
}

# --- 3. Assinatura -------------------------------------------------------------------------------------------------
if ($Sign) {
    $entitlements = Join-Path $root 'packaging/macos/MeuEditorDeVideo.entitlements'
    $exe = Join-Path $macOsDir $ExeName
    # De dentro para fora: bibliotecas e ferramentas primeiro (a mais funda primeiro), o executável principal por último.
    $inner = $machoFiles | Where-Object { $_ -ne $exe } | Sort-Object { $_.Split([IO.Path]::DirectorySeparatorChar).Count } -Descending
    foreach ($f in $inner) {
        Invoke-Checked codesign @('--force', '--timestamp', '--options', 'runtime', '--sign', $signIdentity, $f)
    }
    Invoke-Checked codesign @('--force', '--timestamp', '--options', 'runtime', '--entitlements', $entitlements, '--sign', $signIdentity, $exe)
    Invoke-Checked codesign @('--force', '--timestamp', '--options', 'runtime', '--entitlements', $entitlements, '--sign', $signIdentity, $appDir)
    Invoke-Checked codesign @('--verify', '--deep', '--strict', '--verbose=2', $appDir)
}

function Submit-ForNotarization([string]$file) {
    $arguments = @('notarytool', 'submit', $file, '--keychain-profile', $notaryProfile, '--wait')
    # Chaveiro do perfil, quando não é o padrão (o CI guarda o perfil num chaveiro temporário).
    if ($env:MED_MACOS_NOTARY_KEYCHAIN) { $arguments += @('--keychain', $env:MED_MACOS_NOTARY_KEYCHAIN) }
    Invoke-Checked xcrun $arguments
}

# --- 4. Pacote de distribuição ----------------------------------------------------------------------------------------
$baseName = "MeuEditorDeVideo-$Version-$arch"
if ($isMac) {
    if ($Notarize) {
        # Notariza e grampeia o app primeiro, para que funcione offline mesmo fora do .dmg.
        $zipForNotary = Join-Path ([IO.Path]::GetTempPath()) "$baseName-notarizacao.zip"
        if (Test-Path $zipForNotary) { Remove-Item -Force $zipForNotary }
        Invoke-Checked ditto @('-c', '-k', '--keepParent', $appDir, $zipForNotary)
        try { Submit-ForNotarization $zipForNotary } finally { Remove-Item -Force $zipForNotary -ErrorAction SilentlyContinue }
        Invoke-Checked xcrun @('stapler', 'staple', $appDir)
    }
    $dmg = Join-Path $OutDir "$baseName.dmg"
    if (Test-Path $dmg) { Remove-Item -Force $dmg }
    $stage = Join-Path ([IO.Path]::GetTempPath()) "med-dmg-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    try {
        Invoke-Checked ditto @($appDir, (Join-Path $stage "$AppName.app"))
        New-Item -ItemType SymbolicLink -Path (Join-Path $stage 'Applications') -Target '/Applications' | Out-Null
        Invoke-Checked hdiutil @('create', '-volname', $AppName, '-srcfolder', $stage, '-ov', '-format', 'UDZO', $dmg)
    } finally { Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue }
    if ($Sign) { Invoke-Checked codesign @('--force', '--timestamp', '--sign', $signIdentity, $dmg) }
    if ($Notarize) {
        Submit-ForNotarization $dmg
        Invoke-Checked xcrun @('stapler', 'staple', $dmg)
        Invoke-Checked xcrun @('stapler', 'validate', $dmg)
    }
    $result = $dmg
} else {
    Write-Warning 'Fora do macOS: gerado apenas o .zip da estrutura do .app (bits de execução gravados nele). Não é um aplicativo assinado nem testado; não distribua.'
    Add-Type -AssemblyName System.IO.Compression
    $zipPath = Join-Path $OutDir "$baseName-app.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $parent = (Split-Path $appDir -Parent).TrimEnd('\', '/')
        foreach ($f in Get-ChildItem -LiteralPath $appDir -Recurse -File -Force | Sort-Object FullName) {
            $rel = $f.FullName.Substring($parent.Length).TrimStart('\', '/').Replace('\', '/')
            $entry = $zip.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
            $mode = if ($machoFiles -contains $f.FullName) { 0x81ED } else { 0x81A4 }   # 0100755 / 0100644
            $entry.ExternalAttributes = $mode -shl 16
            $in = [IO.File]::OpenRead($f.FullName); $out = $entry.Open()
            try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
        }
    } finally { $zip.Dispose() }
    $result = $zipPath
}

$sums = Get-ChildItem -LiteralPath $OutDir -File | Where-Object { $_.Extension -in '.deb', '.zip', '.dmg' } | Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllText((Join-Path $OutDir 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), $utf8)
Write-Host "Pacote: $result ($([Math]::Round((Get-Item $result).Length / 1MB, 1)) MB)"
