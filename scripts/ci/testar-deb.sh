#!/usr/bin/env bash
# Testa o .deb num contêiner descartável e limpo do Debian ou do Ubuntu (roda como root dentro dele):
#   testar-deb.sh <pacote.deb> <projeto.med> <pasta de resultados>
# Confere as dependências ANTES de instalar as ferramentas do teste, que trariam bibliotecas junto.
set -uo pipefail

[ -f /.dockerenv ] || { echo "Este teste instala pacotes: só roda dentro de um contêiner descartável."; exit 2; }

DEB="$1"
SAMPLE="$2"
OUT="$3"
APP=/opt/meu-editor-de-video
FAILURES=()
export DEBIAN_FRONTEND=noninteractive LANG=C.UTF-8
mkdir -p "$OUT"

fail() { FAILURES+=("$1"); echo "FALHA: $1"; }

finish() {
    if [ "${#FAILURES[@]}" -gt 0 ]; then
        printf '::error::%s\n' "${FAILURES[@]}"
        exit 1
    fi
    echo "Pacote .deb aprovado."
    exit 0
}

# Espelhos do apt às vezes ficam lentos ou caem no CI: novas tentativas e limite por conexão, em vez de esperar sem fim.
cat > /etc/apt/apt.conf.d/80-teste-ci <<'APT'
Acquire::Retries "5";
Acquire::http::Timeout "30";
Acquire::https::Timeout "30";
APT

echo "== instalar só com as dependências obrigatórias"
for tentativa in 1 2 3; do
    apt-get update -qq && break
    [ "$tentativa" -lt 3 ] || { fail "o apt não atualizou a lista de pacotes"; finish; }
    echo "apt-get update falhou; nova tentativa em 15 s"
    sleep 15
done
if ! apt-get install -y --no-install-recommends "$DEB" > "$OUT/instalar.log" 2>&1; then
    tail -n 40 "$OUT/instalar.log"
    fail "o apt não instalou o pacote"
    finish
fi

echo "== bibliotecas nativas"
LIBS="$(find "$APP" -name '*.so*' -printf '%h\n' | sort -u | paste -sd:)"
# As libggml têm SONAME "….so.0" e arquivo "….so": o Whisper.net as carrega antes da libwhisper (WhisperNativeTests).
# Aqui bastam atalhos com o SONAME, para o ldd conferir as demais dependências delas.
SONAMES="$(mktemp -d)"
for f in "$APP"/runtimes/linux-x64/libggml*.so; do ln -s "$f" "$SONAMES/$(basename "$f").0"; done
LIBS="$SONAMES:$LIBS"
while IFS= read -r f; do
    case "$f" in
        */libcoreclrtraceptprovider.so) continue ;; # rastreamento opcional do .NET
        */linux-arm/*|*/linux-arm64/*) continue ;;
    esac
    missing="$(LD_LIBRARY_PATH="$LIBS" ldd "$f" 2>/dev/null | grep 'not found' || true)"
    [ -z "$missing" ] || fail "$f: $(echo $missing)"
done < <(find "$APP" -type f \( -name '*.so*' -o -perm -u+x \))

echo "== FFmpeg instalado"
FF="$APP/ffmpeg/bin/ffmpeg"
"$FF" -hide_banner -loglevel error -y -f lavfi -i testsrc2=size=320x180:rate=25 -f lavfi -i sine=frequency=440 -t 1 \
    -c:v libx264 -pix_fmt yuv420p -c:a aac /tmp/teste.mp4 || fail "o FFmpeg instalado não codifica"
"$FF" -hide_banner -loglevel error -init_hw_device vaapi -f lavfi -i nullsrc -frames:v 1 -f null - > "$OUT/vaapi.log" 2>&1
code=$?
# Sem GPU ele recusa com erro; não pode ser encerrado por sinal (134 = abort).
[ "$code" -ne 134 ] && [ "$code" -ne 139 ] || fail "o FFmpeg caiu ao usar VA-API (código $code)"

echo "== ferramentas do teste"
apt-get install -y --no-install-recommends xvfb xauth imagemagick procps iproute2 libglib2.0-bin shared-mime-info \
    desktop-file-utils > "$OUT/ferramentas.log" 2>&1 || { fail "ferramentas do teste não instaladas"; finish; }
update-mime-database /usr/share/mime
update-desktop-database /usr/share/applications
desktop-file-validate /usr/share/applications/meu-editor-de-video.desktop || fail "arquivo .desktop inválido"

Xvfb :99 -screen 0 1600x900x24 -nolisten tcp > "$OUT/xvfb.log" 2>&1 &
sleep 3

# Usuário novo, sem ~/.config nem ~/.local/share.
useradd -m -s /bin/bash testador
H=/home/testador
DATA="$H/.local/share/MeuEditorDeVideo"
CONFIG="$H/.config/MeuEditorDeVideo"
install -d -o testador -g testador "$H/trabalho"
cp "$SAMPLE" "$H/Projeto de instalação.med"
chown testador:testador "$H/Projeto de instalação.med"

as_user() { (cd "$H/trabalho" && runuser -u testador -- env HOME="$H" DISPLAY=:99 LANG=C.UTF-8 "$@"); }
stop_app() { pkill -KILL -x MeuEditorDeVide 2> /dev/null; sleep 2; } # o nome do processo tem no máximo 15 letras
app_log() { cat "$DATA"/logs/*.log 2> /dev/null; }
has_log() { grep -qsF "$1" "$DATA"/logs/*.log; }
wait_log() {
    for _ in $(seq 1 120); do
        has_log "$1" && return 0
        sleep 1
    done
    return 1
}
screen() { DISPLAY=:99 import -window root "$OUT/$1.png" || echo "captura indisponível"; }

echo "== primeira abertura"
as_user meu-editor-de-video > "$OUT/primeira-abertura.log" 2>&1 &
wait_log 'iniciado (Avalonia)' || fail "o programa não iniciou"
sleep 8
screen boas-vindas
pgrep -x MeuEditorDeVide > /dev/null || { fail "o programa fechou na primeira abertura"; cat "$OUT/primeira-abertura.log"; }
has_log 'Novo projeto sem título criado' && fail "a janela de boas-vindas não apareceu"
[ ! -e "$H/trabalho/MeuEditorDeVideo" ] || fail "os dados caíram na pasta atual"
stop_app

echo "== .med pela associação de arquivos"
rm -rf "$DATA" "$CONFIG"
install -d -o testador -g testador "$H/.config" "$CONFIG"
printf '{"Name":"Teste","Language":"pt-BR","ThemeColor":"Dark","CheckForNewVersions":false,"LastCheck":"%s"}' "$(date -Iseconds)" \
    > "$CONFIG/preferences.json"
chown testador:testador "$CONFIG/preferences.json"
as_user gio open "$H/Projeto de instalação.med" > "$OUT/associacao.log" 2>&1 &
if ! wait_log 'Projeto aberto: formato'; then
    fail "o .med não abriu pela associação"
    cat "$OUT/associacao.log"
fi
for _ in $(seq 1 20); do
    sockets="$(ss -H -tunap 2> /dev/null | grep 'MeuEditorDeVide' | awk '$6 !~ /^(127\.|\[::1\]|0\.0\.0\.0:|\*:|\[::\]:)/' || true)"
    [ -z "$sockets" ] || { fail "o programa usou a rede: $sockets"; break; }
    sleep 1
done
screen editor
pgrep -x MeuEditorDeVide > /dev/null || fail "o programa fechou depois de abrir o projeto"
app_log | tee "$OUT/programa.log"
grep -qE '\[(ERR|FTL)\]' "$OUT/programa.log" && fail "há erros no log do programa"
stop_app

echo "== desinstalar"
apt-get remove -y meu-editor-de-video > "$OUT/desinstalar.log" 2>&1 || fail "o apt não removeu o pacote"
[ ! -e "$APP/MeuEditorDeVideo" ] || fail "o executável continuou instalado"
[ ! -e /usr/share/applications/meu-editor-de-video.desktop ] || fail "o atalho continuou instalado"

chmod -R a+rwX "$OUT"
finish
