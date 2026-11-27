#!/usr/bin/env python3
# Desenvolvido por Diego Mendes Rodrigues - https://www.linkedin.com/in/diegomendesrodrigues/
# Gera resources/fonts/MedIcons.ttf: fonte de ícones multiplataforma que mantém os
# MESMOS codepoints das fontes do Windows (Segoe Fluent Icons / Segoe MDL2 Assets),
# para que o XAML só precise trocar o FontFamily.
#
# Fonte de origem: Fluent UI System Icons (Microsoft, MIT), tag fixada em resources/fonts/icones.json.
# Dependências (instale num venv): fonttools==4.62.1
# Uso: python scripts/build-icon-font.py [--cache PASTA]
import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

RAIZ = Path(__file__).resolve().parent.parent
PASTA_FONTES = RAIZ / "resources" / "fonts"
SAIDA = PASTA_FONTES / "MedIcons.ttf"
FAMILIA = "Med Icons"
POSTSCRIPT = "MedIcons-Regular"


def baixar(url: str, sha256: str, destino: Path) -> bytes:
    """Baixa a URL fixada (ou usa o cache) e falha se o SHA-256 não conferir."""
    if destino.exists():
        dados = destino.read_bytes()
    else:
        print(f"Baixando {url}")
        with urllib.request.urlopen(url, timeout=120) as r:
            dados = r.read()
    obtido = hashlib.sha256(dados).hexdigest()
    if obtido.lower() != sha256.lower():
        sys.exit(f"SHA-256 inválido para {url}: esperado {sha256}, obtido {obtido}")
    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_bytes(dados)
    return dados


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", default=str(RAIZ / "obj" / "icon-font-cache"),
                    help="pasta onde guardar os downloads verificados")
    args = ap.parse_args()
    cache = Path(args.cache)

    cfg = json.loads((PASTA_FONTES / "icones.json").read_text(encoding="utf-8"))
    origem, glifos = cfg["origem"], cfg["glifos"]

    baixar(origem["url"], origem["sha256"], cache / "FluentSystemIcons-Regular.ttf")
    dados_mapa = baixar(origem["mapaUrl"], origem["mapaSha256"], cache / "FluentSystemIcons-Regular.json")
    nome_para_cp = json.loads(dados_mapa)  # nome do ícone -> codepoint (decimal)

    # codepoint de destino (chave do icones.json, igual ao Segoe) -> codepoint de origem
    destinos = {}
    for chave, nome in glifos.items():
        if nome not in nome_para_cp:
            sys.exit(f"Ícone inexistente na fonte de origem: {nome}")
        destinos[int(chave, 16)] = nome_para_cp[nome]

    # Reduz a fonte apenas aos glifos usados.
    opcoes = subset.Options()
    opcoes.layout_features = []       # sem GSUB/GPOS: são ícones simples
    opcoes.name_IDs = ["*"]
    opcoes.notdef_outline = True
    opcoes.glyph_names = True
    fonte = TTFont(cache / "FluentSystemIcons-Regular.ttf")
    sub = subset.Subsetter(opcoes)
    sub.populate(unicodes=sorted(set(destinos.values())))
    sub.subset(fonte)

    # Remapeia: cada glifo passa a responder ao codepoint do Segoe. Vários destinos
    # podem apontar para o mesmo glifo (ex.: E7C3 e E8A5, ambos "documento").
    cmap_origem = fonte.getBestCmap()
    novo = {destino: cmap_origem[orig] for destino, orig in destinos.items()}
    for t in fonte["cmap"].tables:
        if t.isUnicode():
            t.cmap = dict(novo)
    fonte["cmap"].tables = [t for t in fonte["cmap"].tables if t.isUnicode()]
    fonte["OS/2"].recalcUnicodeRanges(fonte)
    fonte["OS/2"].usFirstCharIndex = min(novo)
    fonte["OS/2"].usLastCharIndex = max(c for c in novo if c <= 0xFFFF)

    # Nomes: família "Med Icons", estilo Regular, PostScript MedIcons-Regular.
    nomes = {1: FAMILIA, 2: "Regular", 3: f"{POSTSCRIPT};{origem['tag']}", 4: f"{FAMILIA} Regular",
             6: POSTSCRIPT, 16: FAMILIA, 17: "Regular"}
    tabela = fonte["name"]
    tabela.names = [n for n in tabela.names if n.nameID not in nomes]
    for nid, texto in nomes.items():
        tabela.setName(texto, nid, 3, 1, 0x409)
        tabela.setName(texto, nid, 1, 0, 0)

    SAIDA.parent.mkdir(parents=True, exist_ok=True)
    fonte.save(SAIDA)
    print(f"{SAIDA}: {SAIDA.stat().st_size} bytes, {len(novo)} codepoints, {len(set(novo.values()))} glifos")


if __name__ == "__main__":
    main()
