#!/usr/bin/env bash
# Roda testar-deb.sh num contêiner limpo da imagem $IMAGEM (debian:12, ubuntu:22.04…), a partir da raiz do repositório.
# Uso no CI: IMAGEM=ubuntu:24.04 scripts/ci/testar-deb-conteiner.sh
set -euo pipefail

: "${IMAGEM:?defina IMAGEM com a imagem do contêiner}"
pacotes=(artifacts/linux/*.deb)
if [ "${#pacotes[@]}" -ne 1 ] || [ ! -f "${pacotes[0]}" ]; then
    echo "::error::esperado um único .deb em artifacts/linux"
    exit 1
fi
pacote="$(basename "${pacotes[0]}")"

echo "===== $IMAGEM"
docker run --rm --init -v "$PWD:/repo:ro" -v "$PWD/InstallerResults:/saida" "$IMAGEM" \
    bash /repo/scripts/ci/testar-deb.sh "/repo/artifacts/linux/$pacote" /repo/tests/instaladores/projeto.med "/saida/${IMAGEM//:/-}" \
    || { echo "::error::o .deb falhou em $IMAGEM"; exit 1; }
