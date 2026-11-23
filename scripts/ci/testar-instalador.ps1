#Requires -Version 7.0
<#
.SYNOPSIS
  Testa de ponta a ponta os instaladores do Windows (Setup.exe e MSI) e o .dmg do macOS numa máquina descartável do CI.

.DESCRIPTION
  Instala, confere bibliotecas nativas e FFmpeg, abre o programa como um usuário novo (boas-vindas), abre um .med pela
  associação de arquivos, confere o log e a rede, guarda capturas em -ResultDir e desinstala.
  Apaga preferências e dados do programa do usuário atual: só roda no GitHub Actions.
  O pacote .deb é testado por scripts/ci/testar-deb.sh. Continua depois de uma falha, para mostrar todas de uma vez.
#>
[CmdletBinding()]
param([string]$ResultDir = 'InstallerResults')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Este teste instala o programa e apaga os dados dele: só roda no GitHub Actions.' }
if (-not ($IsWindows -or $IsMacOS)) { throw 'Use scripts/ci/testar-deb.sh no Linux.' }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ResultDir = (New-Item -ItemType Directory -Force -Path $ResultDir).FullName
$failures = [System.Collections.Generic.List[string]]::new()
# No macOS o programa usa ~/Library/Application Support (AppPaths.Roots); o .NET devolveria ~/.config e ~/.local/share.
if ($IsMacOS) {
    $appData = Join-Path $HOME 'Library/Application Support/MeuEditorDeVideo'
    $localData = $appData
}
else {
    $appData = Join-Path ([Environment]::GetFolderPath('ApplicationData', 'DoNotVerify')) 'MeuEditorDeVideo'
    $localData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData', 'DoNotVerify')) 'MeuEditorDeVideo'
}
$registryKey = 'HKCU:\Software\Meu Editor de Vídeo'
$sample = Join-Path ([IO.Path]::GetTempPath()) 'Projeto de instalação.med'
Copy-Item (Join-Path $root 'tests/instaladores/projeto.med') $sample -Force

function Test-Step([string]$name, [scriptblock]$body) {
    Write-Host "::group::$name"
    try {
        & $body | Out-Host
        Write-Host "OK: $name"
    } catch {
        $failures.Add("${name}: $($_.Exception.Message)")
        Write-Host "FALHA: ${name}: $($_.Exception.Message)"
    }
    Write-Host '::endgroup::'
}

function Invoke-Installer([string]$file, [string]$arguments) {
    $p = Start-Process -FilePath $file -ArgumentList $arguments -PassThru
    $null = $p.Handle
    if (-not $p.WaitForExit(900000)) { $p.Kill($true); throw "$file não terminou em 15 minutos" }
    if ($p.ExitCode -notin 0, 3010) { throw "$file $arguments terminou com código $($p.ExitCode)" }
}

function Stop-App {
    Get-Process -Name MeuEditorDeVideo -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill($true); $null = $_.WaitForExit(10000) }
}

function Clear-UserData {
    Stop-App
    foreach ($d in @($appData, $localData) | Select-Object -Unique) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force } }
    if ($IsWindows -and (Test-Path -LiteralPath $registryKey)) { Remove-Item -LiteralPath $registryKey -Recurse -Force }
}

function Set-Preferences {
    $now = [DateTimeOffset]::Now.ToString('o')
    if ($IsWindows) {
        $null = New-Item -Path $registryKey -Force
        foreach ($v in @{ Name = 'Teste'; Language = 'pt-BR'; ThemeColor = 'Dark'; LastCheck = $now }.GetEnumerator()) {
            $null = New-ItemProperty -LiteralPath $registryKey -Name $v.Key -Value $v.Value -PropertyType String -Force
        }
        $null = New-ItemProperty -LiteralPath $registryKey -Name CheckForNewVersions -Value 0 -PropertyType DWord -Force
    } else {
        $null = New-Item -ItemType Directory -Force -Path $appData
        @{ Name = 'Teste'; Language = 'pt-BR'; ThemeColor = 'Dark'; CheckForNewVersions = $false; LastCheck = $now } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $appData 'preferences.json')
    }
}

function Get-AppLog {
    $dir = Join-Path $localData 'logs'
    if (-not (Test-Path -LiteralPath $dir)) { return '' }
    return (@(Get-ChildItem -LiteralPath $dir -File | ForEach-Object { Get-Content -LiteralPath $_.FullName }) -join "`n")
}

function Wait-AppLog([string]$text, [int]$seconds = 120) {
    for ($i = 0; $i -lt $seconds; $i++) {
        if ((Get-AppLog).Contains($text)) { return }
        Start-Sleep -Seconds 1
    }
    Write-Host (Get-AppLog)
    throw "o log não registrou '$text' em $seconds s"
}

function Save-Screen([string]$name) {
    $file = Join-Path $ResultDir "$name.png"
    try {
        if ($IsWindows) {
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
            $bitmap = [Drawing.Bitmap]::new($b.Width, $b.Height)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $graphics.CopyFromScreen($b.Location, [Drawing.Point]::Empty, $b.Size)
            $bitmap.Save($file)
            $graphics.Dispose(); $bitmap.Dispose()
        } else {
            $p = Start-Process screencapture -ArgumentList "-x `"$file`"" -PassThru
            if (-not $p.WaitForExit(20000)) { $p.Kill() }
        }
    } catch { Write-Host "captura indisponível: $($_.Exception.Message)" }
}

function Test-NativeLibraries([string]$dir) {
    if ($IsWindows) {
        $names = 'onnxruntime.dll', 'onnxruntime-genai.dll', 'DirectML.dll', 'OpenCvSharpExtern.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'SDL3.dll', 'whisper.dll', 'avcodec-63.dll'
        $folder = 'win-x64'
    } else {
        $names = 'libOpenCvSharpExtern.dylib', 'libSkiaSharp.dylib', 'libHarfBuzzSharp.dylib', 'libSDL3.dylib', 'libwhisper.dylib', 'libavcodec.63.dylib'
        $arm = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64'
        $folder = $arm ? 'macos-arm64' : 'macos-x64'
        # O ONNX Runtime não publica bibliotecas para o Mac Intel.
        if ($arm) { $names += 'libonnxruntime.dylib', 'libonnxruntime-genai.dylib' }
    }
    # Em outro processo: com a biblioteca carregada aqui, o Restart Manager do MSI encerraria este script ao desinstalar.
    # No macOS o programa carrega as libggml antes da libwhisper (dependências por @rpath): o teste faz o mesmo.
    $loader = @'
import ctypes, glob, os, sys
path = sys.argv[1]
if 'whisper' in os.path.basename(path):
    pending = glob.glob(os.path.join(os.path.dirname(path), 'libggml*.dylib'))
    for _ in range(len(pending)):
        for dependency in list(pending):
            try:
                ctypes.CDLL(dependency, mode=ctypes.RTLD_GLOBAL)
                pending.remove(dependency)
            except OSError:
                pass
ctypes.CDLL(path)
'@
    $bad = foreach ($name in $names) {
        $found = @(Get-ChildItem -LiteralPath $dir -Recurse -File | Where-Object { $_.Name -eq $name })
        $file = @($found | Where-Object { $_.FullName.Contains($folder) }) + $found | Select-Object -First 1
        if (-not $file) { "$name ausente"; continue }
        try {
            if ($IsWindows) {
                $env:MED_TESTE_DLL = $file.FullName
                $out = & pwsh -NoProfile -Command '$null = [Runtime.InteropServices.NativeLibrary]::Load($env:MED_TESTE_DLL)' 2>&1
            } else {
                $out = & python3 -c $loader $file.FullName 2>&1
            }
            if ($LASTEXITCODE -ne 0) { throw "$out" }
            Write-Host "carregou $name"
        } catch { "$name não carrega: $($_.Exception.Message)" }
    }
    if ($bad) { throw ($bad -join '; ') }
}

function Test-FFmpeg([string]$dir) {
    $exe = $IsWindows ? (Join-Path $dir 'ffmpeg/ffmpeg.exe') : (Join-Path $dir 'ffmpeg/bin/ffmpeg')
    $out = Join-Path ([IO.Path]::GetTempPath()) 'instalação-teste.mp4'
    & $exe -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=320x180:rate=25' -f lavfi -i 'sine=frequency=440' -t 1 -c:v libx264 -pix_fmt yuv420p -c:a aac $out
    if ($LASTEXITCODE -ne 0) { throw "o FFmpeg instalado terminou com código $LASTEXITCODE" }
    if ((Get-Item -LiteralPath $out).Length -lt 1000) { throw 'vídeo de teste vazio' }
}

function Test-InstalledApp([string]$dir, [string]$prefix) {
    $exe = Join-Path $dir ($IsWindows ? 'MeuEditorDeVideo.exe' : 'MeuEditorDeVideo')

    Test-Step "${prefix}: bibliotecas nativas" { Test-NativeLibraries $dir }
    Test-Step "${prefix}: FFmpeg instalado" { Test-FFmpeg $dir }

    Test-Step "${prefix}: primeira abertura" {
        Clear-UserData
        $p = Start-Process -FilePath $exe -PassThru
        Wait-AppLog 'iniciado (Avalonia)'
        Start-Sleep -Seconds 8
        Save-Screen "$prefix-boas-vindas"
        if ($p.HasExited) { throw "o programa fechou sozinho (código $($p.ExitCode))" }
        if ((Get-AppLog).Contains('Novo projeto sem título criado')) { throw 'a janela de boas-vindas não apareceu' }
        Stop-App
    }

    Test-Step "${prefix}: .med pela associação de arquivos" {
        Clear-UserData
        Set-Preferences
        if ($IsWindows) { Start-Process -FilePath $sample }
        else {
            & open $sample
            if ($LASTEXITCODE -ne 0) { throw "open terminou com código $LASTEXITCODE" }
        }
        Wait-AppLog 'Projeto aberto: formato'
        Start-Sleep -Seconds 12
        Save-Screen "$prefix-editor"
        if (-not (Get-Process -Name MeuEditorDeVideo -ErrorAction SilentlyContinue)) { throw 'o programa fechou depois de abrir o projeto' }
        $log = Get-AppLog
        Write-Host $log
        if ($log -match '\[(ERR|FTL)\]') { throw 'há erros no log do programa' }
    }
    Stop-App

    Test-Step "${prefix}: sem uso da rede" {
        Clear-UserData
        Set-Preferences
        & pwsh -NoProfile -File (Join-Path $root 'scripts/offline-check.ps1') -ExePath $exe -Seconds 20
        if ($LASTEXITCODE -ne 0) { throw 'offline-check falhou' }
        if (-not (Get-AppLog).Contains('Novo projeto sem título criado')) { throw 'o editor não abriu' }
    }
    Stop-App
    $log = Join-Path $localData 'logs'
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $ResultDir "$prefix-logs") -Recurse -Force }
}

function Find-WindowsInstall([string[]]$bases) {
    foreach ($base in $bases) {
        $dir = Join-Path $base 'Meu Editor de Vídeo'
        if (Test-Path -LiteralPath (Join-Path $dir 'MeuEditorDeVideo.exe')) { return $dir }
    }
    return $null
}

if ($IsWindows) {
    $out = Join-Path $root 'artifacts/installer'
    $kinds = @(
        @{ Name = 'setup'; Filter = '*Setup.exe'; Bases = @($env:ProgramFiles) },
        @{ Name = 'msi'; Filter = '*.msi'; Bases = @("$env:LOCALAPPDATA\Programs", "$env:LOCALAPPDATA\Apps", $env:ProgramFiles) })
    foreach ($kind in $kinds) {
        $package = Get-ChildItem $out -Filter $kind.Filter | Select-Object -First 1
        $log = Join-Path $ResultDir $kind.Name
        Test-Step "$($kind.Name): instalar" {
            if ($kind.Name -eq 'setup') { Invoke-Installer $package.FullName "/install /quiet /norestart /log `"$log-instalar.log`"" }
            else { Invoke-Installer 'msiexec.exe' "/i `"$($package.FullName)`" /qn /norestart /l*v `"$log-instalar.log`"" }
        }
        $dir = Find-WindowsInstall $kind.Bases
        if (-not $dir) { $failures.Add("$($kind.Name): programa não encontrado depois de instalar"); continue }
        Write-Host "instalado em $dir"
        Test-InstalledApp $dir $kind.Name
        Test-Step "$($kind.Name): desinstalar" {
            if ($kind.Name -eq 'setup') { Invoke-Installer $package.FullName "/uninstall /quiet /norestart /log `"$log-desinstalar.log`"" }
            else { Invoke-Installer 'msiexec.exe' "/x `"$($package.FullName)`" /qn /norestart /l*v `"$log-desinstalar.log`"" }
            if (Test-Path -LiteralPath (Join-Path $dir 'MeuEditorDeVideo.exe')) { throw 'o executável continuou instalado' }
            $left = @(Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue)
            if ($left) { throw "sobraram arquivos: $(($left | Select-Object -First 5).Name -join ', ')" }
            foreach ($hive in 'HKCU:', 'HKLM:') {
                if (Test-Path "$hive\Software\Classes\MeuEditorDeVideo.Projeto\shell\open\command") { throw "a associação do .med ficou em $hive" }
            }
        }
    }
} else {
    $dmg = Get-ChildItem (Join-Path $root 'artifacts/macos') -Filter '*.dmg' | Select-Object -First 1
    $app = '/Applications/Meu Editor de Vídeo.app'
    $lsregister = '/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister'
    Test-Step 'dmg: instalar' {
        $mount = Join-Path ([IO.Path]::GetTempPath()) "med-$([guid]::NewGuid().ToString('N'))"
        $null = New-Item -ItemType Directory -Path $mount
        & hdiutil attach $dmg.FullName -nobrowse -readonly -mountpoint $mount
        if ($LASTEXITCODE -ne 0) { throw 'o .dmg não montou' }
        try {
            & ditto (Join-Path $mount 'Meu Editor de Vídeo.app') $app
            if ($LASTEXITCODE -ne 0) { throw 'a cópia para Aplicativos falhou' }
        } finally { & hdiutil detach $mount -force }
        # A pasta de montagem e a de empacotamento também têm o .app: registra só o instalado.
        $built = Join-Path $root 'artifacts/macos/Meu Editor de Vídeo.app'
        if (Test-Path -LiteralPath $built) { & $lsregister -u $built }
        & $lsregister -f $app
    }
    if (Test-Path -LiteralPath $app) {
        Test-Step 'dmg: assinatura do pacote' {
            & codesign --verify --deep --strict --verbose=2 $app 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'assinatura do .app ausente ou inválida' }
        }
        # Sem Developer ID e notarização o Gatekeeper recusa: registrado no log, sem reprovar.
        Write-Host "Gatekeeper: $(& spctl --assess --type execute --verbose $app 2>&1)"
        Test-InstalledApp (Join-Path $app 'Contents/MacOS') 'dmg'
        Test-Step 'dmg: remover' {
            Remove-Item -LiteralPath $app -Recurse -Force
            if (Test-Path -LiteralPath $app) { throw 'o aplicativo não foi removido' }
        }
    }
}

if ($failures.Count) {
    Write-Host "Falhas no teste do instalador: $($failures.Count)"
    $failures | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
Write-Host 'Instalador aprovado.'
exit 0
