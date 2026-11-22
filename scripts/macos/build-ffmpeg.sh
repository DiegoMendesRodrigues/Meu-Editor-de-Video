#!/usr/bin/env bash
# Compila o FFmpeg 9.0.x COMPARTILHADO (GPL) para o macOS, na arquitetura do sistema em que roda (uname -m:
# arm64 ou x86_64), a partir das fontes fixadas em scripts/deps.lock.json (entradas kind "source").
#
# Uso (chamado por scripts/fetch-deps.ps1; também dá para rodar à mão):
#   bash scripts/macos/build-ffmpeg.sh <pasta-das-fontes> <pasta-de-saida>
#   ex.: bash scripts/macos/build-ffmpeg.sh deps/.downloads/sources deps/ffmpeg
#
# Pré-requisitos (instalar uma vez): Xcode Command Line Tools (clang, make), cmake, pkg-config (ou pkgconf) e,
# só em Mac Intel, nasm.    brew install cmake pkg-config nasm
#
# Resultado (mesma estrutura do Linux, lida por FFmpegRuntime/FFmpegPlatform e por src/AppContent.props):
#   <saída>/bin/ffmpeg                 executável de linha de comando (gravação de tela, webcam, microfone)
#   <saída>/lib/libavcodec.63.dylib    e demais libav*/libsw* com o nome do "soname" (arquivos reais, sem links)
#   <saída>/LICENSE.txt                GPL-3.0 do FFmpeg
# Os nomes de instalação são @rpath/libX.N.dylib; as bibliotecas têm LC_RPATH @loader_path e o ffmpeg tem
# @executable_path/../lib, então tudo funciona depois de copiado para ffmpeg/lib e ffmpeg/bin do aplicativo.
# As bibliotecas externas (x264, x265, libvpx, SVT-AV1, libaom, LAME, Opus, Vorbis) são ligadas estaticamente.
#
# NÃO TESTADO em um Mac (escrito sem acesso a um): veja documentation/DEPENDENCIAS.md.

set -euo pipefail

if [ "$#" -ne 2 ]; then
    echo "Uso: $0 <pasta-das-fontes> <pasta-de-saida>" >&2
    exit 2
fi
if [ "$(uname -s)" != "Darwin" ]; then
    echo "Este script só roda no macOS." >&2
    exit 2
fi

SOURCES="$(cd "$1" && pwd)"
mkdir -p "$2"
OUT="$(cd "$2" && pwd)"

ARCH="$(uname -m)"                 # arm64 ou x86_64
case "$ARCH" in
    arm64|x86_64) ;;
    *) echo "Arquitetura não suportada: $ARCH" >&2; exit 2 ;;
esac
JOBS="$(sysctl -n hw.ncpu)"
DARWIN_MAJOR="$(uname -r | cut -d. -f1)"

# Nomes dos arquivos de fonte: os mesmos "fileName" do lock.
SRC_FFMPEG="$SOURCES/ffmpeg-9.0.2.tar.xz"
SRC_X264="$SOURCES/x264-0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee.tar.gz"
SRC_X265="$SOURCES/x265-4.3.tar.gz"
SRC_VPX="$SOURCES/libvpx-1.17.0.tar.gz"
SRC_SVT="$SOURCES/SVT-AV1-4.3.1.tar.gz"
SRC_AOM="$SOURCES/libaom-3.15.3.tar.gz"
SRC_LAME="$SOURCES/lame-3.100.tar.gz"
SRC_OPUS="$SOURCES/opus-1.6.1.tar.gz"
SRC_OGG="$SOURCES/libogg-1.3.6.tar.xz"
SRC_VORBIS="$SOURCES/libvorbis-1.3.7.tar.xz"

for f in "$SRC_FFMPEG" "$SRC_X264" "$SRC_X265" "$SRC_VPX" "$SRC_SVT" "$SRC_AOM" "$SRC_LAME" "$SRC_OPUS" "$SRC_OGG" "$SRC_VORBIS"; do
    [ -f "$f" ] || { echo "Fonte ausente: $f (rode scripts/fetch-deps.ps1)" >&2; exit 1; }
done

need() { command -v "$1" >/dev/null 2>&1 || { echo "Ferramenta ausente: $1 (brew install $2)" >&2; exit 1; }; }
need cmake cmake
need make "Xcode Command Line Tools (xcode-select --install)"
need clang "Xcode Command Line Tools (xcode-select --install)"
need libtool "Xcode Command Line Tools (xcode-select --install)"
need install_name_tool "Xcode Command Line Tools (xcode-select --install)"
need otool "Xcode Command Line Tools (xcode-select --install)"
need codesign "Xcode Command Line Tools (xcode-select --install)"
command -v pkg-config >/dev/null 2>&1 || command -v pkgconf >/dev/null 2>&1 || { echo "Ferramenta ausente: pkg-config (brew install pkg-config)" >&2; exit 1; }
if [ "$ARCH" = "x86_64" ]; then need nasm nasm; fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/med-ffmpeg.XXXXXX")"
if [ "${MED_KEEP_BUILD:-0}" != "1" ]; then trap 'rm -rf "$WORK"' EXIT; else echo "Pasta de trabalho mantida: $WORK"; fi
PREFIX="$WORK/prefix"       # bibliotecas externas estáticas
STAGE="$WORK/stage"         # instalação do FFmpeg antes de arrumar a saída
mkdir -p "$PREFIX/lib/pkgconfig" "$STAGE"

# Mesmo alvo mínimo do restante do aplicativo no macOS; código independente de posição para ligar nos dylibs.
export MACOSX_DEPLOYMENT_TARGET="11.0"
export CFLAGS="-O2 -fPIC -mmacosx-version-min=11.0"
export CXXFLAGS="-O2 -fPIC -mmacosx-version-min=11.0"
export LDFLAGS="-mmacosx-version-min=11.0"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
# Só as fontes do PREFIX: ignora bibliotecas do Homebrew (/opt/homebrew, /usr/local) que mudariam o resultado.
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"

extract() { # extract <tarball> <nome>: extrai para $WORK/src/<nome> sem o diretório de topo
    mkdir -p "$WORK/src/$2"
    tar -xf "$1" -C "$WORK/src/$2" --strip-components=1
}

step() { echo; echo "=== $* ==="; }

CMAKE_COMMON=(
    -DCMAKE_BUILD_TYPE=Release
    -DCMAKE_INSTALL_PREFIX="$PREFIX"
    -DCMAKE_INSTALL_LIBDIR=lib
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON
    -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5
    -DBUILD_SHARED_LIBS=OFF
)

# ---------------------------------------------------------------- libogg / libvorbis / Opus / LAME
step "libogg"
extract "$SRC_OGG" libogg
(cd "$WORK/src/libogg" && ./configure --prefix="$PREFIX" --disable-shared --enable-static --with-pic && make -j"$JOBS" && make install)

step "libvorbis"
extract "$SRC_VORBIS" libvorbis
# O configure do 1.3.7 passa -force_cpusubtype_ALL, que o ld atual da Apple recusa.
sed -i '' 's/-force_cpusubtype_ALL//g' "$WORK/src/libvorbis/configure"
(cd "$WORK/src/libvorbis" && ./configure --prefix="$PREFIX" --disable-shared --enable-static --with-pic --with-ogg="$PREFIX" --disable-examples --disable-docs && make -j"$JOBS" && make install)

step "Opus"
extract "$SRC_OPUS" opus
(cd "$WORK/src/opus" && ./configure --prefix="$PREFIX" --disable-shared --enable-static --with-pic --disable-doc --disable-extra-programs && make -j"$JOBS" && make install)

step "LAME"
extract "$SRC_LAME" lame
(
    cd "$WORK/src/lame"
    # O config.sub do LAME 3.100 (2008) não conhece arm64-apple-darwin: usa o do Opus (recente) ou o do automake.
    for dir in "$WORK/src/opus" /opt/homebrew/share/automake-* /usr/local/share/automake-* /usr/share/automake-*; do
        if [ -f "$dir/config.sub" ] && [ -f "$dir/config.guess" ]; then
            cp "$dir/config.sub" "$dir/config.guess" .
            break
        fi
    done
    ./configure --prefix="$PREFIX" --disable-shared --enable-static --with-pic --disable-frontend --disable-decoder --disable-gtktest
    make -j"$JOBS"
    make install
)

# ---------------------------------------------------------------- x264
step "x264"
extract "$SRC_X264" x264
(cd "$WORK/src/x264" && ./configure --prefix="$PREFIX" --enable-static --enable-pic --disable-cli --disable-lavf --disable-swscale --disable-ffms --disable-gpac --disable-lsmash && make -j"$JOBS" && make install)

# ---------------------------------------------------------------- x265 (8, 10 e 12 bits em uma só biblioteca)
step "x265"
extract "$SRC_X265" x265
(
    cd "$WORK/src/x265"
    X265_COMMON=("${CMAKE_COMMON[@]}" -DENABLE_SHARED=OFF -DENABLE_CLI=OFF -DENABLE_LIBNUMA=OFF)
    # O CMake 4 recusa políticas fixadas em OLD.
    sed -i '' -E 's/cmake_policy\(SET (CMP0025|CMP0054) OLD\)/cmake_policy(SET \1 NEW)/g' source/CMakeLists.txt
    cmake -S source -B build-12 "${X265_COMMON[@]}" -DHIGH_BIT_DEPTH=ON -DMAIN12=ON -DEXPORT_C_API=OFF
    cmake --build build-12 --target x265-static -j "$JOBS"
    cmake -S source -B build-10 "${X265_COMMON[@]}" -DHIGH_BIT_DEPTH=ON -DEXPORT_C_API=OFF
    cmake --build build-10 --target x265-static -j "$JOBS"
    cmake -S source -B build-8 "${X265_COMMON[@]}" \
        -DEXTRA_LIB="x265_main10.a;x265_main12.a" -DEXTRA_LINK_FLAGS="-L$WORK/src/x265/build-10 -L$WORK/src/x265/build-12" \
        -DLINKED_10BIT=ON -DLINKED_12BIT=ON
    cp build-10/libx265.a build-8/x265_main10.a
    cp build-12/libx265.a build-8/x265_main12.a
    cmake --build build-8 --target x265-static -j "$JOBS"
    mv build-8/libx265.a build-8/libx265_main.a
    libtool -static -o build-8/libx265.a build-8/libx265_main.a build-8/x265_main10.a build-8/x265_main12.a
    # Instala cabeçalhos e x265.pc (o install não recompila: libx265.a, agora unida, está mais nova que os objetos).
    cmake --install build-8
    cp build-8/libx265.a "$PREFIX/lib/libx265.a"
    # Se a versão não vier do repositório, o x265.pc fica com "unknown" e o FFmpeg recusa; corrige.
    if [ ! -f "$PREFIX/lib/pkgconfig/x265.pc" ]; then
        # O x265 só gera o .pc quando descobre a própria versão.
        cat > "$PREFIX/lib/pkgconfig/x265.pc" <<PC
prefix=$PREFIX
Name: x265
Description: H.265/HEVC
Version: 4.3
Libs: -L$PREFIX/lib -lx265
Libs.private: -lc++
Cflags: -I$PREFIX/include
PC
    fi
    if ! grep -Eq '^Version: [0-9]' "$PREFIX/lib/pkgconfig/x265.pc"; then
        sed -i '' 's/^Version: .*/Version: 4.3/' "$PREFIX/lib/pkgconfig/x265.pc"
    fi
)

# ---------------------------------------------------------------- libvpx
step "libvpx"
extract "$SRC_VPX" libvpx
(
    cd "$WORK/src/libvpx"
    if [ "$ARCH" = "arm64" ]; then VPX_ARCH="arm64"; else VPX_ARCH="x86_64"; fi
    # O configure do libvpx só conhece versões do Darwin até a que existia no lançamento: tenta a atual e recua.
    ok=0
    for ((m = DARWIN_MAJOR; m >= DARWIN_MAJOR - 6 && m >= 20; m--)); do
        if ./configure --prefix="$PREFIX" --target="$VPX_ARCH-darwin$m-gcc" --disable-shared --enable-static --enable-pic \
            --disable-examples --disable-tools --disable-docs --disable-unit-tests \
            --enable-vp9-highbitdepth --enable-runtime-cpu-detect >/dev/null 2>&1; then
            ok=1; break
        fi
    done
    [ "$ok" = 1 ] || { echo "libvpx: o configure não aceitou nenhum alvo darwin (última tentativa: $VPX_ARCH-darwin$m-gcc)." >&2; exit 1; }
    make -j"$JOBS"
    make install
)

# ---------------------------------------------------------------- SVT-AV1
step "SVT-AV1"
extract "$SRC_SVT" svtav1
(
    cd "$WORK/src/svtav1"
    # "build-med": as fontes já têm Build/ (o disco do macOS não diferencia maiúsculas).
    cmake -S . -B build-med "${CMAKE_COMMON[@]}" -DBUILD_APPS=OFF -DBUILD_TESTING=OFF -DSVT_AV1_LTO=OFF
    cmake --build build-med -j "$JOBS"
    cmake --install build-med
)

# ---------------------------------------------------------------- libaom
step "libaom"
extract "$SRC_AOM" libaom
(
    cd "$WORK/src/libaom"
    cmake -S . -B build-med "${CMAKE_COMMON[@]}" -DENABLE_DOCS=OFF -DENABLE_EXAMPLES=OFF -DENABLE_TESTS=OFF -DENABLE_TOOLS=OFF -DCONFIG_PIC=1
    cmake --build build-med -j "$JOBS"
    cmake --install build-med
)

# ---------------------------------------------------------------- FFmpeg
step "FFmpeg 9.0.2 ($ARCH)"
extract "$SRC_FFMPEG" ffmpeg
(
    cd "$WORK/src/ffmpeg"
    # -liconv explícito: com --disable-autodetect, o configure só procura a iconv na libc, e no macOS ela é à parte.
    ./configure \
        --prefix="$STAGE" \
        --enable-shared --disable-static --enable-pic \
        --enable-gpl --enable-version3 \
        --disable-debug --disable-doc --disable-ffplay --disable-ffprobe \
        --enable-libx264 --enable-libx265 --enable-libvpx --enable-libsvtav1 --enable-libaom \
        --enable-libmp3lame --enable-libopus --enable-libvorbis \
        --disable-autodetect --enable-zlib --enable-bzlib --enable-iconv \
        --enable-avfoundation --enable-appkit --enable-coreimage \
        --enable-videotoolbox --enable-audiotoolbox \
        --pkg-config-flags="--static" \
        --extra-cflags="-I$PREFIX/include -mmacosx-version-min=11.0" \
        --extra-ldflags="-L$PREFIX/lib -mmacosx-version-min=11.0" \
        --extra-libs="-liconv" \
        --install-name-dir="@rpath" \
        --extra-ldsoflags="-Wl,-rpath,@loader_path" \
        --extra-ldexeflags="-Wl,-rpath,@executable_path/../lib" \
        || { tail -n 60 ffbuild/config.log; exit 1; }
    make -j"$JOBS"
    make install
)

# ---------------------------------------------------------------- Saída: arquivos reais com o nome do soname
step "Organizando a saída em $OUT"
rm -rf "$OUT/bin" "$OUT/lib"
mkdir -p "$OUT/bin" "$OUT/lib"
cp "$STAGE/bin/ffmpeg" "$OUT/bin/ffmpeg"
for f in "$STAGE"/lib/lib*.dylib; do
    name="$(basename "$f")"
    # libavcodec.63.dylib (soname) entra; libavcodec.63.1.102.dylib e libavcodec.dylib não.
    if [[ "$name" =~ ^lib[a-z]+\.[0-9]+\.dylib$ ]]; then
        cp -L "$f" "$OUT/lib/$name"
    fi
done
cp "$WORK/src/ffmpeg/COPYING.GPLv3" "$OUT/LICENSE.txt"
chmod 755 "$OUT/bin/ffmpeg"

# Garante os rpaths mesmo que alguma opção do configure não tenha valido (add_rpath em duplicidade só avisa).
for f in "$OUT"/lib/*.dylib; do
    otool -l "$f" | grep -q "path @loader_path " || install_name_tool -add_rpath "@loader_path" "$f" 2>/dev/null || true
done
otool -l "$OUT/bin/ffmpeg" | grep -q "path @executable_path/../lib " || install_name_tool -add_rpath "@executable_path/../lib" "$OUT/bin/ffmpeg" 2>/dev/null || true
# install_name_tool invalida a assinatura; no Apple Silicon o código sem assinatura (mesmo ad hoc) não executa.
for f in "$OUT"/lib/*.dylib "$OUT/bin/ffmpeg"; do codesign --force --sign - "$f"; done

# ---------------------------------------------------------------- Verificações
step "Verificando"
bad=0
for f in "$OUT"/lib/*.dylib "$OUT/bin/ffmpeg"; do
    # Dependências permitidas: @rpath (as nossas) e bibliotecas/frameworks do sistema.
    refs="$(otool -L "$f" | tail -n +2 | awk '{print $1}' | grep -Ev '^(@rpath/|/usr/lib/|/System/Library/)' || true)"
    if [ -n "$refs" ]; then echo "Dependência externa em $f:"; echo "$refs"; bad=1; fi
done
[ "$bad" = 0 ] || { echo "Há bibliotecas fora do sistema ligadas dinamicamente (portabilidade comprometida)." >&2; exit 1; }
[ -f "$OUT/lib/libavcodec.63.dylib" ] || { echo "libavcodec.63.dylib ausente (o binding FFmpeg.AutoGen 9.0 espera a versão 63)." >&2; exit 1; }

encoders="$("$OUT/bin/ffmpeg" -hide_banner -encoders 2>/dev/null)"
for enc in libx264 libx265 libvpx-vp9 libsvtav1 libaom-av1 libmp3lame libopus libvorbis h264_videotoolbox; do
    grep -q " $enc " <<< "$encoders" || { echo "Codificador ausente na build: $enc" >&2; exit 1; }
done
# A gravação de tela, webcam e microfone usa o avfoundation.
devices="$("$OUT/bin/ffmpeg" -hide_banner -devices 2>/dev/null)"
grep -q avfoundation <<< "$devices" || { echo "Dispositivo ausente na build: avfoundation" >&2; exit 1; }
version="$("$OUT/bin/ffmpeg" -hide_banner -version)"
sed -n 1p <<< "$version"
echo "FFmpeg compilado em $OUT"
