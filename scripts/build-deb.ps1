<#
.SYNOPSIS
  Gera o pacote .deb do Meu Editor de Vídeo para Debian 12+ e Ubuntu 22.04+ (x64).

.DESCRIPTION
  1. dotnet publish do aplicativo (Release, linux-x64, self-contained) em artifacts\publish-linux-x64
     (ou usa -PublishDir com -SkipPublish).
  2. Monta a árvore do pacote:
       /opt/meu-editor-de-video/                          pasta inteira da publicação (executável, ffmpeg/, i18n/, ajuda/...)
       /usr/bin/meu-editor-de-video                       script de inicialização
       /usr/share/applications/meu-editor-de-video.desktop
       /usr/share/mime/packages/meu-editor-de-video.xml   tipo de arquivo *.med (contêiner ZIP)
       /usr/share/icons/hicolor/{256x256,64x64}/{apps,mimetypes}/...
       /usr/share/doc/meu-editor-de-video/copyright
       DEBIAN/{control,md5sums,postinst,postrm}
  3. Gera o .deb com `dpkg-deb --root-owner-group --build` quando existir; senão usa o gravador próprio deste script
     (arquivo ar com debian-binary + control.tar.gz + data.tar.gz, tar no formato GNU feito com System.Formats.Tar,
     dono root:root e modos 0755/0644 definidos pela lista de arquivos, sem depender do sistema de arquivos). O gravador
     próprio também roda no Windows e no macOS, o que permite testar o pacote fora do Linux (-ForceManualWriter força o uso).
  4. Grava SHA256SUMS.txt ao lado do pacote (artifacts\linux).

  Pré-requisitos: .NET SDK 10 e scripts\fetch-deps.ps1 já executado no Linux (FFmpeg compartilhado em deps\ffmpeg).
  O pacote não é assinado (repositórios APT assinam o índice, não o arquivo .deb).

.EXAMPLE
  pwsh -File scripts/build-deb.ps1
  pwsh -File scripts/build-deb.ps1 -SkipPublish -PublishDir /tmp/publicacao -OutDir /tmp/saida
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [ValidateSet('linux-x64')]
    [string]$Rid = 'linux-x64',
    [switch]$SkipPublish,
    [string]$PublishDir,
    [string]$OutDir,
    # Campo Maintainer do control ("Nome <email>"). Sem o parâmetro, usa a variável de ambiente MED_DEB_MAINTAINER.
    [string]$Maintainer = $env:MED_DEB_MAINTAINER,
    [switch]$ForceManualWriter
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
if (-not $PublishDir) { $PublishDir = Join-Path $artifacts "publish-$Rid" }
if (-not $OutDir) { $OutDir = Join-Path $artifacts 'linux' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- Dados do pacote ---------------------------------------------------------------------------------------------
$PackageName = 'meu-editor-de-video'
$InstallDir = "/opt/$PackageName"
$ExeName = 'MeuEditorDeVideo'
$Architecture = 'amd64'
$Homepage = 'https://meueditordevideos.aplicativoia.online/'

# Dependências da primeira versão. A lista autoritativa por distribuição é a de documentation/DEPENDENCIAS.md
# (bibliotecas nativas do FFmpeg, ONNX Runtime, OpenCvSharp e SkiaSharp); reconcilie as duas ao mudar uma delas.
# Alternativas com "|" cobrem Debian 12 (libicu72, libasound2), Ubuntu 22.04 (libicu70) e Ubuntu 24.04 (libicu74, libasound2t64).
$Depends = @(
    'libc6 (>= 2.35)',
    'libstdc++6 (>= 12)',
    'zlib1g',
    'libicu72 | libicu70 | libicu74 | libicu76',
    'libssl3 | libssl3t64',
    'libfontconfig1',
    'libfreetype6',
    'libx11-6',
    'libice6',
    'libsm6',
    'libxext6',
    'libxrandr2',
    'libxi6',
    'libxcursor1',
    'libgl1',
    'libasound2 | libasound2t64',
    'libpulse0',
    'libva2',
    # Vistos nos .so da publicação linux-x64 real (ELF NEEDED): OpenCvSharpExtern (OpenCV highgui, ligado ao GTK 3),
    # libuiohook do SharpHook (atalho global: X11, XRecord, libinput, xkbcommon, Wayland), Whisper (OpenMP) e OpenCV.
    'libgtk-3-0 | libgtk-3-0t64',
    'libxkbcommon0',
    'libxtst6',
    'libxt6 | libxt6t64',
    'libinput10',
    'libudev1',
    'libwayland-client0',
    'libdrm2',
    'libgomp1',
    'libatomic1'
)
$Recommends = @('espeak-ng', 'xdg-utils')

[xml]$props = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Directory.Build.props')
$groups = @($props.Project.PropertyGroup)
$Version = ($groups | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $Version) { throw 'Versão não encontrada em Directory.Build.props.' }
# O Debian exige "Nome <email>" no Maintainer; o endereço vem de -Maintainer ou de MED_DEB_MAINTAINER (nunca inventado).
if (-not $Maintainer) { throw 'Informe o mantenedor do pacote: -Maintainer "Nome <email>" ou a variável MED_DEB_MAINTAINER.' }
if ($Maintainer -notmatch '^[^<>]+ <[^<>\s]+@[^<>\s]+>$') { throw "Mantenedor inválido: '$Maintainer' (formato: Nome <email>)." }
Write-Host "Meu Editor de Vídeo $Version ($Rid)"

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe terminou com código $LASTEXITCODE." }
}

# --- 1. Publicação -----------------------------------------------------------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    Invoke-Checked dotnet @('publish', (Join-Path $root 'src/MeuEditorDeVideo.Desktop/MeuEditorDeVideo.Desktop.csproj'),
        '-c', $Configuration, '-r', $Rid, '--self-contained', 'true', '-p:MedPlataforma=linux', '-o', $PublishDir, '-nologo', '-v', 'q')
}
if (-not (Test-Path (Join-Path $PublishDir $ExeName))) { throw "Publicação não encontrada em $PublishDir (falta $ExeName)." }
$PublishDir = (Resolve-Path $PublishDir).Path

# --- 2. Lista de arquivos do pacote -----------------------------------------------------------------------------
# Cada item: Path (relativo, sem "./" nem "/" inicial), Kind (dir|file|symlink), Mode (inteiro; 0755 = 493), Source
# (arquivo local), Bytes (conteúdo gerado) e Target (destino do link).
$entries = [System.Collections.Generic.List[object]]::new()
$dirSet = [System.Collections.Generic.HashSet[string]]::new()
$mode755 = [Convert]::ToInt32('755', 8)
$mode644 = [Convert]::ToInt32('644', 8)

function Add-Dir([string]$path) {
    $acc = ''
    foreach ($p in $path.Trim('/').Split('/')) {
        $acc = if ($acc) { "$acc/$p" } else { $p }
        if ($dirSet.Add($acc)) { $entries.Add([pscustomobject]@{ Path = $acc; Kind = 'dir'; Mode = $mode755; Source = $null; Bytes = $null; Target = $null }) }
    }
}
function Add-Bytes([string]$path, [byte[]]$bytes, [int]$mode) {
    Add-Dir ([IO.Path]::GetDirectoryName($path).Replace('\', '/'))
    $entries.Add([pscustomobject]@{ Path = $path; Kind = 'file'; Mode = $mode; Source = $null; Bytes = $bytes; Target = $null })
}
function Add-Text([string]$path, [string]$text, [int]$mode) {
    Add-Bytes $path ([Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n"))) $mode
}
function Test-Elf([string]$file) {
    $fs = [IO.File]::OpenRead($file)
    try {
        $b = [byte[]]::new(4)
        return ($fs.Read($b, 0, 4) -eq 4 -and $b[0] -eq 0x7F -and $b[1] -eq 0x45 -and $b[2] -eq 0x4C -and $b[3] -eq 0x46)
    } finally { $fs.Dispose() }
}

# Pasta da publicação inteira em /opt/meu-editor-de-video (ordem estável).
Add-Dir $InstallDir.TrimStart('/')
$publishItems = Get-ChildItem -LiteralPath $PublishDir -Recurse -Force |
    ForEach-Object { [pscustomobject]@{ Item = $_; Rel = $_.FullName.Substring($PublishDir.Length).TrimStart('\', '/').Replace('\', '/') } } |
    Sort-Object Rel -CaseSensitive
foreach ($p in $publishItems) {
    $item = $p.Item
    $dest = $InstallDir.TrimStart('/') + '/' + $p.Rel
    if ($item.PSIsContainer) { Add-Dir $dest; continue }
    # Executáveis: o programa, o ffmpeg, bibliotecas .so e qualquer ELF sem extensão (createdump...).
    $exec = $item.Name -eq $ExeName -or $p.Rel -eq 'ffmpeg/ffmpeg' -or $item.Name -like '*.so*' -or ($item.Extension -eq '' -and (Test-Elf $item.FullName))
    $entries.Add([pscustomobject]@{ Path = $dest; Kind = 'file'; Mode = $(if ($exec) { $mode755 } else { $mode644 }); Source = $item.FullName; Bytes = $null; Target = $null })
}

# Script de inicialização em /usr/bin (o exec mantém o PID).
Add-Text "usr/bin/$PackageName" @"
#!/bin/sh
# Inicia o Meu Editor de Vídeo (instalado em $InstallDir).
exec "$InstallDir/$ExeName" "`$@"
"@ $mode755

# Entrada de menu, com nome e descrição localizados.
$localized = [ordered]@{
    'en'    = 'Video editor with local AI'
    'es'    = 'Editor de vídeo con IA local'
    'fr'    = 'Éditeur vidéo avec IA locale'
    'de'    = 'Videoeditor mit lokaler KI'
    'pt_PT' = 'Editor de vídeo com IA local'
    'pt_BR' = 'Editor de vídeo com IA local'
}
$desktop = [System.Text.StringBuilder]::new()
[void]$desktop.Append("[Desktop Entry]`nType=Application`nVersion=1.0`nName=Meu Editor de Vídeo`nGenericName=Video Editor`nComment=Editor de vídeo com IA local`n")
foreach ($k in $localized.Keys) {
    [void]$desktop.Append("Name[$k]=Meu Editor de Vídeo`nComment[$k]=$($localized[$k])`n")
}
[void]$desktop.Append("Exec=$PackageName %f`nIcon=$PackageName`nTerminal=false`nCategories=AudioVideo;Video;AudioVideoEditing;`nMimeType=application/x-meu-editor-de-video;`nStartupWMClass=$ExeName`nKeywords=video;editor;timeline;vídeo;`n")
Add-Text "usr/share/applications/$PackageName.desktop" $desktop.ToString() $mode644

# Tipo de arquivo do projeto (.med é um contêiner ZIP com manifest.json e project.json).
Add-Text "usr/share/mime/packages/$PackageName.xml" @'
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/x-meu-editor-de-video">
    <comment>Projeto do Meu Editor de Vídeo</comment>
    <comment xml:lang="en">Meu Editor de Vídeo project</comment>
    <comment xml:lang="es">Proyecto de Meu Editor de Vídeo</comment>
    <comment xml:lang="fr">Projet Meu Editor de Vídeo</comment>
    <comment xml:lang="de">Meu-Editor-de-Vídeo-Projekt</comment>
    <comment xml:lang="pt">Projeto do Meu Editor de Vídeo</comment>
    <comment xml:lang="pt_BR">Projeto do Meu Editor de Vídeo</comment>
    <sub-class-of type="application/zip"/>
    <icon name="application-x-meu-editor-de-video"/>
    <glob pattern="*.med"/>
  </mime-type>
</mime-info>
'@ $mode644

# Ícones (o mesmo desenho para o aplicativo e para o tipo de arquivo).
$assets = Join-Path $root 'src/MeuEditorDeVideo.Desktop/Assets'
foreach ($size in 256, 64) {
    $png = Join-Path $assets "logo-$size.png"
    if (-not (Test-Path $png)) { throw "Ícone não encontrado: $png" }
    $bytes = [IO.File]::ReadAllBytes($png)
    Add-Bytes "usr/share/icons/hicolor/${size}x${size}/apps/$PackageName.png" $bytes $mode644
    Add-Bytes "usr/share/icons/hicolor/${size}x${size}/mimetypes/application-x-meu-editor-de-video.png" $bytes $mode644
}

# Direitos autorais no formato legível por máquina do Debian.
$year = (Get-Date).Year
Add-Text "usr/share/doc/$PackageName/copyright" @"
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Meu Editor de Vídeo
Upstream-Contact: $Maintainer
Source: $Homepage

Files: *
Copyright: $year Diego Mendes Rodrigues
License: GPL-3.0+

License: GPL-3.0+
 Este programa é software livre: você pode redistribuí-lo e/ou modificá-lo
 sob os termos da Licença Pública Geral GNU, conforme publicada pela Free
 Software Foundation, na versão 3 da Licença ou (a seu critério) qualquer
 versão posterior.
 .
 Este programa é distribuído na esperança de que seja útil, mas SEM NENHUMA
 GARANTIA; sem mesmo a garantia implícita de COMERCIALIZAÇÃO ou ADEQUAÇÃO A UM
 PROPÓSITO PARTICULAR.
 .
 O texto completo da licença está em /usr/share/common-licenses/GPL-3 e em
 $InstallDir/LICENSE.txt. Os avisos e licenças dos componentes de terceiros
 (FFmpeg, ONNX Runtime, OpenCV, Avalonia e outros) estão em
 $InstallDir/THIRD-PARTY-NOTICES.txt.
"@ $mode644

# md5sums de todo arquivo regular (DEBIAN fica de fora) e tamanho instalado em KiB.
$md5 = [System.Text.StringBuilder]::new()
$installedBytes = 0L
foreach ($e in $entries) {
    if ($e.Kind -ne 'file') { continue }
    $data = if ($e.Source) { [IO.File]::ReadAllBytes($e.Source) } else { $e.Bytes }
    $installedBytes += $data.LongLength
    $hash = [BitConverter]::ToString([Security.Cryptography.MD5]::HashData($data)).Replace('-', '').ToLowerInvariant()
    [void]$md5.Append("$hash  $($e.Path)`n")
}
$installedKiB = [long][Math]::Ceiling($installedBytes / 1024.0)

# --- 3. Arquivos de controle -------------------------------------------------------------------------------------
$control = @"
Package: $PackageName
Version: $Version
Architecture: $Architecture
Maintainer: $Maintainer
Installed-Size: $installedKiB
Depends: $($Depends -join ', ')
Recommends: $($Recommends -join ', ')
Section: video
Priority: optional
Homepage: $Homepage
Description: editor de vídeo não linear com IA 100% local
 Meu Editor de Vídeo é um editor de vídeo não linear com linha do tempo,
 efeitos, transições, texto, mixagem de áudio, legendas automáticas e
 recursos de inteligência artificial que rodam inteiramente neste computador.
 O programa não envia seus vídeos para a internet.
"@.Replace("`r`n", "`n") + "`n"

# Atualiza os bancos de menus, tipos de arquivo e ícones; se a ferramenta não existir, o pacote continua válido.
$maintainerScript = @'
#!/bin/sh
set -e
if [ "$1" = "configure" ] || [ "$1" = "triggered" ] || [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
    command -v update-mime-database >/dev/null 2>&1 && update-mime-database /usr/share/mime >/dev/null 2>&1 || true
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
'@.Replace("`r`n", "`n") + "`n"

$utf8 = [Text.UTF8Encoding]::new($false)
$controlFiles = @(
    @{ Name = 'control'; Bytes = $utf8.GetBytes($control); Mode = $mode644 },
    @{ Name = 'md5sums'; Bytes = $utf8.GetBytes($md5.ToString()); Mode = $mode644 },
    @{ Name = 'postinst'; Bytes = $utf8.GetBytes($maintainerScript); Mode = $mode755 },
    @{ Name = 'postrm'; Bytes = $utf8.GetBytes($maintainerScript); Mode = $mode755 }
)

$debFile = Join-Path $OutDir "${PackageName}_${Version}_$Architecture.deb"
if (Test-Path $debFile) { Remove-Item -Force $debFile }

# --- 4a. Gravador próprio: tar GNU (System.Formats.Tar) + arquivo ar ------------------------------------------------
$epoch = if ($env:SOURCE_DATE_EPOCH) { [DateTimeOffset]::FromUnixTimeSeconds([long]$env:SOURCE_DATE_EPOCH) } else { [DateTimeOffset]::UtcNow }

function Add-TarEntry($tar, [string]$name, [Formats.Tar.TarEntryType]$type, [int]$mode, [byte[]]$data, [string]$link) {
    $entry = [Formats.Tar.GnuTarEntry]::new($type, $name)
    $entry.Mode = [IO.UnixFileMode]$mode
    $entry.Uid = 0; $entry.Gid = 0; $entry.UserName = 'root'; $entry.GroupName = 'root'
    $entry.ModificationTime = $epoch
    if ($type -eq [Formats.Tar.TarEntryType]::SymbolicLink) { $entry.LinkName = $link }
    if ($null -ne $data) { $entry.DataStream = [IO.MemoryStream]::new($data) }
    $tar.WriteEntry($entry)
}
function New-TarGz([scriptblock]$fill) {
    $ms = [IO.MemoryStream]::new()
    $gz = [IO.Compression.GZipStream]::new($ms, [IO.Compression.CompressionLevel]::Optimal, $true)
    $tar = [Formats.Tar.TarWriter]::new($gz, [Formats.Tar.TarEntryFormat]::Gnu, $true)
    try { & $fill $tar } finally { $tar.Dispose(); $gz.Dispose() }
    return , $ms.ToArray()
}
function Write-ArMember([IO.Stream]$out, [string]$name, [byte[]]$data, [long]$mtime) {
    $header = '{0,-16}{1,-12}{2,-6}{3,-6}{4,-8}{5,-10}`' -f $name, $mtime, 0, 0, '100644', $data.Length
    $h = [Text.Encoding]::ASCII.GetBytes($header + "`n")
    if ($h.Length -ne 60) { throw "Cabeçalho ar inválido ($($h.Length) bytes)." }
    $out.Write($h, 0, 60)
    $out.Write($data, 0, $data.Length)
    if ($data.Length % 2 -eq 1) { $out.WriteByte(10) }
}
function Write-DebManually {
    $file = [Formats.Tar.TarEntryType]::RegularFile
    $dir = [Formats.Tar.TarEntryType]::Directory
    $controlTar = New-TarGz {
        param($tar)
        Add-TarEntry $tar './' $dir $mode755 $null $null
        foreach ($f in $controlFiles) { Add-TarEntry $tar "./$($f.Name)" $file $f.Mode $f.Bytes $null }
    }
    $dataTar = New-TarGz {
        param($tar)
        Add-TarEntry $tar './' $dir $mode755 $null $null
        foreach ($e in $entries) {
            if ($e.Kind -eq 'dir') { Add-TarEntry $tar "./$($e.Path)/" $dir $e.Mode $null $null }
            elseif ($e.Kind -eq 'symlink') { Add-TarEntry $tar "./$($e.Path)" ([Formats.Tar.TarEntryType]::SymbolicLink) $e.Mode $null $e.Target }
            else {
                $data = if ($e.Source) { [IO.File]::ReadAllBytes($e.Source) } else { $e.Bytes }
                Add-TarEntry $tar "./$($e.Path)" $file $e.Mode $data $null
            }
        }
    }
    $out = [IO.File]::Create($debFile)
    try {
        $out.Write([Text.Encoding]::ASCII.GetBytes("!<arch>`n"), 0, 8)
        $mtime = $epoch.ToUnixTimeSeconds()
        Write-ArMember $out 'debian-binary' ([Text.Encoding]::ASCII.GetBytes("2.0`n")) $mtime
        Write-ArMember $out 'control.tar.gz' $controlTar $mtime
        Write-ArMember $out 'data.tar.gz' $dataTar $mtime
    } finally { $out.Dispose() }
}

# --- 4b. dpkg-deb (Linux): árvore em disco com os modos corretos --------------------------------------------------
function Write-DebWithDpkg {
    $stage = Join-Path ([IO.Path]::GetTempPath()) "med-deb-$([guid]::NewGuid().ToString('N'))"
    try {
        foreach ($e in $entries) {
            $target = Join-Path $stage $e.Path
            if ($e.Kind -eq 'dir') { New-Item -ItemType Directory -Force -Path $target | Out-Null }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                if ($e.Source) { Copy-Item -LiteralPath $e.Source -Destination $target } else { [IO.File]::WriteAllBytes($target, $e.Bytes) }
            }
        }
        $debian = Join-Path $stage 'DEBIAN'
        New-Item -ItemType Directory -Force -Path $debian | Out-Null
        foreach ($f in $controlFiles) { [IO.File]::WriteAllBytes((Join-Path $debian $f.Name), $f.Bytes) }
        foreach ($e in $entries) { [IO.File]::SetUnixFileMode((Join-Path $stage $e.Path), [IO.UnixFileMode]$e.Mode) }
        foreach ($f in $controlFiles) { [IO.File]::SetUnixFileMode((Join-Path $debian $f.Name), [IO.UnixFileMode]$f.Mode) }
        [IO.File]::SetUnixFileMode($debian, [IO.UnixFileMode]$mode755)
        [IO.File]::SetUnixFileMode($stage, [IO.UnixFileMode]$mode755)
        Invoke-Checked dpkg-deb @('--root-owner-group', '--build', $stage, $debFile)
    } finally {
        if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    }
}

$hasDpkg = $IsLinux -and [bool](Get-Command dpkg-deb -ErrorAction SilentlyContinue)
if ($hasDpkg -and -not $ForceManualWriter) { Write-Host 'Gerando com dpkg-deb…'; Write-DebWithDpkg }
else { Write-Host 'Gerando com o gravador próprio (ar + tar.gz)…'; Write-DebManually }

# --- 5. Conferência e SHA-256 -------------------------------------------------------------------------------------
if ($hasDpkg) {
    Invoke-Checked dpkg-deb @('--info', $debFile)
    Invoke-Checked dpkg-deb @('--contents', $debFile)
}
$sums = Get-ChildItem -LiteralPath $OutDir -File | Where-Object { $_.Extension -in '.deb', '.zip', '.dmg' } | Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name }
[IO.File]::WriteAllText((Join-Path $OutDir 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), $utf8)
Write-Host "Pacote: $debFile ($([Math]::Round((Get-Item $debFile).Length / 1MB, 1)) MB)"
