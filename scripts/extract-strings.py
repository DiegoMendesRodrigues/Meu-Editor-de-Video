"""
Extrai os textos da interface (pt-BR, a língua de origem) para resources/i18n/origem.json.

Fontes: atributos de texto nos .xaml, recursos sys:String, literais e strings interpoladas do C# (as interpolações
viram {0}, {1}…, e concatenações "a" + x + "b" viram "a{0}b") e os nomes dos modelos de exportação (.json).
Ignora chamadas de log, atributos, identificadores (sem espaço nem acento) e caminhos.

Uso: python scripts/extract-strings.py
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'src')
OUT = os.path.join(ROOT, 'resources', 'i18n', 'origem.json')

# Projetos com textos para o usuário (motores incluídos: mensagens de erro e nomes de efeitos).
SKIP_DIRS = {'bin', 'obj'}
# Instruções do modelo de linguagem do assistente virtual: já escritas em cada idioma, não são textos da interface.
SKIP_FILES = {'HelpAssistantService.cs'}
# Valores técnicos que parecem texto (códigos de tecla, colunas de grade, fontes, nomes de propriedades).
IGNORED = {'Vc', 'Vc{0}', 'Auto,*', 'Command', 'Contents', 'Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace'}
XAML_ATTRS = ('Text', 'Content', 'Header', 'ToolTip', 'Title', 'AutomationProperties.Name', 'AutomationProperties.HelpText',
              'Tag', 'Description', 'Label', 'PlaceholderText', 'ToolTip.Tip', 'Watermark')
LOG_CALL = re.compile(r'\b(Log(Information|Warning|Error|Debug|Trace|Critical)|_logger\.\w+|logger\.\w+|Debug\.\w+|Trace\.\w+)\s*\($')


def looks_like_text(s: str) -> bool:
    t = s.strip()
    if len(t) < 2:
        return False
    if not re.search(r'[A-Za-zÀ-ÿ]', t):
        return False
    if re.fullmatch(r'[\w.\-/\\:*?<>|]+', t) and not re.search(r'[À-ÿ]', t) and ' ' not in t:
        # identificador, caminho, extensão, chave… (palavras simples com maiúscula ainda contam: "Gravar")
        if not re.fullmatch(r'[A-ZÀ-Ý][a-zà-ÿ]+', t):
            return False
    if t.startswith(('http://', 'https://', 'pack://', '#')):
        return False
    if re.fullmatch(r'\{[^}]*\}', t):
        return False
    return True


def unescape_cs(body: str, verbatim: bool) -> str:
    if verbatim:
        return body.replace('""', '"')
    out, i = [], 0
    while i < len(body):
        c = body[i]
        if c == '\\' and i + 1 < len(body):
            n = body[i + 1]
            out.append({'n': '\n', 't': '\t', 'r': '\r', '"': '"', "'": "'", '\\': '\\', '0': '\0'}.get(n, n))
            if n == 'u':
                out[-1] = chr(int(body[i + 2:i + 6], 16))
                i += 6
                continue
            i += 2
            continue
        out.append(c)
        i += 1
    return ''.join(out)


def interp_template(body: str, verbatim: bool) -> str:
    """$"a {x:0.0} b" → "a {0:0.0} b" (chaves duplas viram literais)."""
    out, i, n = [], 0, 0
    while i < len(body):
        c = body[i]
        if c == '{' and body[i:i + 2] == '{{':
            out.append('{{'); i += 2; continue
        if c == '}' and body[i:i + 2] == '}}':
            out.append('}}'); i += 2; continue
        if c == '{':
            depth, j, fmt = 1, i + 1, None
            in_str = False
            while j < len(body) and depth:
                ch = body[j]
                if ch == '"' and not verbatim:
                    in_str = not in_str
                elif not in_str:
                    if ch in '({[':
                        depth += 1 if ch == '{' else 0
                    if ch == '}':
                        depth -= 1
                        if depth == 0:
                            break
                    if ch == ':' and depth == 1 and fmt is None and body[i + 1:j].count('?') == body[i + 1:j].count(':'):
                        fmt = j
                j += 1
            expr_fmt = body[fmt + 1:j] if fmt is not None else None
            # alinhamento/formatos: mantém o formato; "x ? a : b" não tem formato (contagem de ? e :)
            out.append('{%d%s}' % (n, (':' + expr_fmt) if expr_fmt and '"' not in expr_fmt and ' ' not in expr_fmt else ''))
            n += 1
            i = j + 1
            continue
        out.append(c)
        i += 1
    return unescape_cs(''.join(out).replace('{{', '\x01').replace('}}', '\x02'), verbatim).replace('\x01', '{{').replace('\x02', '}}')


TOKEN = re.compile(r'''(?P<interp>\$@"(?:[^"]|"")*"|@\$"(?:[^"]|"")*"|\$"(?:[^"\\]|\\.)*")|(?P<verb>@"(?:[^"]|"")*")|(?P<str>"(?:[^"\\\n]|\\.)*")''')


def cs_strings(text: str):
    # remove comentários (preservando strings)
    result = []
    for m in TOKEN.finditer(text):
        start = m.start()
        line_start = text.rfind('\n', 0, start) + 1
        prefix = text[line_start:start]
        if '//' in prefix and prefix.count('"') % 2 == 0:
            continue
        before = text[max(0, start - 120):start].rstrip()
        attribute = re.search(r'\[\s*(\w+)(\(|\s*\()[^\]]*$', text[max(0, start - 200):start])
        # "[ new(...)" é uma expressão de coleção, não um atributo.
        if attribute and attribute.group(1) != 'new' and before.endswith(('(', ',', '=')):
            # atributo [Xxx("...")]
            last = text[text.rfind('[', 0, start):start]
            if ']' not in last:
                continue
        # chamadas de log
        call = re.search(r'(\w[\w.]*)\s*\(\s*(?:\w+\s*,\s*)?$', before)
        if call and re.search(r'(Log(Information|Warning|Error|Debug|Trace|Critical)|^Debug\.|^Trace\.|Console\.)', call.group(1)):
            continue
        tok = m.group(0)
        if m.group('interp'):
            verbatim = '@' in tok[:2]
            body = tok[3:-1] if verbatim else tok[2:-1]
            s = interp_template(body, verbatim)
        elif m.group('verb'):
            s = unescape_cs(tok[2:-1], True)
        else:
            s = unescape_cs(tok[1:-1], False)
        result.append((m.start(), m.end(), s, bool(m.group('interp'))))
    return result


def concat_templates(text: str, strings):
    """ "a" + expr + "b" (na mesma expressão) → "a{0}b". """
    out = []
    by_start = {s[0]: s for s in strings}
    for st, en, s, interp in strings:
        parts, n, pos, cur_end = [s if not interp else None], 0, en, en
        if interp:
            continue
        chain = s
        k = 0
        while True:
            m = re.match(r'\s*\+\s*', text[cur_end:])
            if not m:
                break
            nxt = cur_end + m.end()
            if nxt in by_start and not by_start[nxt][3]:
                chain += by_start[nxt][2]
                cur_end = by_start[nxt][1]
                continue
            # expressão até o próximo + "literal" ou fim
            m2 = re.match(r'([^;"\n+]+?)\s*(\+\s*(?="))', text[nxt:])
            if m2:
                chain += '{%d}' % k
                k += 1
                cur_end = nxt + m2.end(1)
                continue
            m3 = re.match(r'([\w.()\[\]?!]+)', text[nxt:])
            if m3:
                chain += '{%d}' % k
                k += 1
                cur_end = nxt + m3.end(1)
                continue
            break
        if chain != s:
            out.append(chain)
    return out


def xaml_strings(text: str):
    found = []
    for attr in XAML_ATTRS:
        for m in re.finditer(r'\s' + re.escape(attr) + r'="([^"{][^"]*)"', text):
            found.append(m.group(1))
    for m in re.finditer(r'<sys:String x:Key="[^"]+">([^<]*)</sys:String>', text):
        found.append(m.group(1))
        if m.group(1).count('_') == 1:  # tecla de acesso: o Translator procura o texto sem o sublinhado
            found.append(m.group(1).replace('_', ''))
    for m in re.finditer(r'<(?:Run|TextBlock|Label|Button|MenuItem|CheckBox|RadioButton)[^>/]*>([^<]+)</', text):
        found.append(m.group(1).strip())
    import html
    return [html.unescape(f).replace('&#x0a;', '\n') for f in found]


def main():
    keys = set()
    for dirpath, dirnames, filenames in os.walk(SRC):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for f in filenames:
            p = os.path.join(dirpath, f)
            if f in SKIP_FILES:
                continue
            if f.endswith('.cs') and not f.endswith(('.g.cs', '.g.i.cs', 'AssemblyInfo.cs')):
                text = open(p, encoding='utf-8-sig').read()
                strings = cs_strings(text)
                for _, _, s, _ in strings:
                    keys.add(s)
                for c in concat_templates(text, strings):
                    keys.add(c)
            elif f.endswith(('.xaml', '.axaml')):
                for s in xaml_strings(open(p, encoding='utf-8-sig').read()):
                    keys.add(s)
    for f in os.listdir(os.path.join(ROOT, 'resources', 'templates', 'export')):
        if f.endswith('.json'):
            data = json.load(open(os.path.join(ROOT, 'resources', 'templates', 'export', f), encoding='utf-8-sig'))
            for k in ('name', 'nome', 'description', 'descricao'):
                if isinstance(data.get(k), str):
                    keys.add(data[k])
    # Textos curtos ou montados em código que a extração não reconhece sozinha.
    keys.update(['Auto', 'Automática', 'IA'])
    texts = sorted(k for k in keys if (looks_like_text(k) or k == 'IA') and k not in IGNORED)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(texts, open(OUT, 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
    print(len(texts), 'textos em', OUT)


if __name__ == '__main__':
    sys.exit(main())
