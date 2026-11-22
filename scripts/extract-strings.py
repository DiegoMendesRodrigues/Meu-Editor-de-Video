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
# Palavras minúsculas que entram como valor de um modelo ("Adicionar máscara ({0})"): parecem identificadores.
LOWER_WORDS = ('animada', 'clipe', 'completa', 'decodificar', 'dedicada', 'desconhecida', 'desconhecido', 'efeito', 'elipse',
               'fala', 'horizontal', 'imagem', 'integrada/compartilhada', 'invertida', 'livre', 'pessoa', 'quadrado', 'rastreada', 'semitons', 'trecho',
               'trechos', 'trilha', 'vazio', 'vertical')
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
        if not re.fullmatch(r'[A-ZÀ-Ý][a-zà-ÿ]+(/[A-Za-zÀ-ÿ][a-zà-ÿ]+)?', t):
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
            j = code_end(body, i + 1)
            fmt = None
            k = i + 1
            while k < j:
                k = code_end(body, k, ':')
                if k < j and body[i + 1:j].count('?') == body[i + 1:j].count(':'):
                    break
                if k < j and '?' not in body[i + 1:k]:
                    break
                k += 1
            if k < j:
                fmt = k
            expr_fmt = body[fmt + 1:j] if fmt is not None else None
            # alinhamento/formatos: mantém o formato; "x ? a : b" não tem formato (contagem de ? e :)
            out.append('{%d%s}' % (n, (':' + expr_fmt) if expr_fmt and '"' not in expr_fmt and ' ' not in expr_fmt else ''))
            n += 1
            i = j + 1
            continue
        out.append(c)
        i += 1
    return unescape_cs(''.join(out).replace('{{', '\x01').replace('}}', '\x02'), verbatim).replace('\x01', '{{').replace('\x02', '}}')


def string_end(text: str, start: int):
    """Se começa um literal C# em start: (fim, prefixo, aspas, interpolações [(início, fim)]); senão None."""
    m = re.match(r'([$@]*)("+)', text[start:start + 16])
    if not m:
        return None
    prefix, quotes = m.group(1), len(m.group(2))
    dollars, verbatim, raw = prefix.count('$'), '@' in prefix, '@' not in prefix and quotes >= 3
    if not raw:
        quotes = 1
    i, holes = start + len(prefix) + quotes, []
    while i < len(text):
        c = text[i]
        if raw and text.startswith('"' * quotes, i):
            return i + quotes, prefix, quotes, holes
        if not raw:
            if c == '\\' and not verbatim:
                i += 2
                continue
            if c == '"':
                if verbatim and text.startswith('""', i):
                    i += 2
                    continue
                return i + 1, prefix, 1, holes
            if c == '\n' and not verbatim:
                return None
            if dollars and text[i:i + 2] in ('{{', '}}'):
                i += 2
                continue
        if dollars and text.startswith('{' * (dollars if raw else 1), i):
            first = i + (dollars if raw else 1)
            close = code_end(text, first)
            holes.append((first, close))
            i = close + (dollars if raw else 1)
            continue
        i += 1
    return None


def skip_code_token(text: str, i: int):
    """Fim do comentário, caractere ou literal que começa em i (None se não houver)."""
    if text.startswith('//', i):
        n = text.find('\n', i)
        return len(text) if n < 0 else n
    if text.startswith('/*', i):
        n = text.find('*/', i)
        return len(text) if n < 0 else n + 2
    if text[i] == "'":
        m = re.match(r"'(?:\\[^'\n]+|[^'\\\n])'", text[i:i + 14])
        if m:
            return i + m.end()
    if text[i] in '"$@':
        found = string_end(text, i)
        if found:
            return found[0]
    return None


def code_end(text: str, i: int, stops: str = '}') -> int:
    """Primeiro caractere de stops fora de parênteses, textos e comentários (ou o fechamento que sobra)."""
    depth = 0
    while i < len(text):
        n = skip_code_token(text, i)
        if n is not None:
            i = n
            continue
        c = text[i]
        if depth == 0 and c in stops:
            return i
        if c in '([{':
            depth += 1
        elif c in ')]}':
            if depth == 0:
                return i
            depth -= 1
        i += 1
    return i


def all_literals(text: str, i: int = 0, end: int = None):
    """Os literais do código, inclusive os de dentro das interpolações."""
    end = len(text) if end is None else end
    while i < end:
        if text[i] in '"$@':
            found = string_end(text, i)
            if found:
                yield (i,) + found
                for first, last in found[3]:
                    yield from all_literals(text, first, last)
                i = found[0]
                continue
        n = skip_code_token(text, i)
        i = n if n is not None else i + 1


def literal_text(text: str, start: int, end: int, prefix: str, quotes: int) -> str:
    body = text[start + len(prefix) + quotes:end - quotes]
    if quotes >= 3:
        lines = body.split('\n')
        if len(lines) > 1:
            indent = len(lines[-1])
            body = '\n'.join(line.rstrip('\r')[indent:] for line in lines[1:-1])
        return interp_template(body, True) if '$' in prefix else body
    return interp_template(body, '@' in prefix) if '$' in prefix else unescape_cs(body, '@' in prefix)


def cs_strings(text: str):
    result = []
    for start, end, prefix, quotes, _ in all_literals(text):
        before = text[max(0, start - 120):start].rstrip()
        bracket = text.rfind('[', 0, start)
        if (bracket >= 0 and ']' not in text[bracket:start] and before.endswith(('(', ',', '='))
                and re.match(r'\[(\w+:\s*)?[A-Z][\w.]*\(', text[bracket:start])
                # atributo [Xxx("...")] vem no início de uma declaração; "= [ Num(...)" e "f([X(...)" são coleções.
                and re.search(r'(^|[;{}\]])$|//[^\n]*$', text[max(0, bracket - 300):bracket].rstrip())):
            continue
        # chamadas de log
        call = re.search(r'(\w[\w.]*)\s*\(\s*(?:\w+\s*,\s*)?$', before)
        if call and re.search(r'(Log(Information|Warning|Error|Debug|Trace|Critical)|^Debug\.|^Trace\.|Console\.)', call.group(1)):
            continue
        result.append((start, end, literal_text(text, start, end, prefix, quotes), '$' in prefix))
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


def chain_templates(text: str, strings):
    """$"a {x}" + "b" + y → "a {0}b{1}": os pedaços de uma mensagem quebrada em linhas viram um modelo só."""
    by_start, used, out = {s[0]: s for s in strings}, set(), []
    for start, end, value, _ in strings:
        if start in used:
            continue
        chain, pos, count = [value], end, 1
        while True:
            m = re.match(r'(?:\s|//[^\n]*\n)*\+(?![+=])(?:\s|//[^\n]*\n)*', text[pos:pos + 400])
            if not m:
                break
            first = pos + m.end()
            if first in by_start:
                used.add(first)
                chain.append(by_start[first][2])
                pos = by_start[first][1]
            else:
                stop = code_end(text, first, '+;,?:')
                while text.startswith('?.', stop) or text.startswith('??', stop):
                    stop = code_end(text, stop + 2, '+;,?:')
                if stop == first:
                    break
                chain.append(None)
                pos = first + len(text[first:stop].rstrip())
            count += 1
        if count < 2:
            continue
        numbers, template = 0, ''
        for part in chain:
            if part is None:
                template += '{%d}' % numbers
                numbers += 1
            else:
                found = re.findall(r'(?<!\{)\{\d+', part)
                base = numbers
                template += re.sub(r'(?<!\{)\{(\d+)', lambda g: '{%d' % (base + int(g.group(1))), part)
                numbers += len(found)
        out.append(template)
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
                for c in concat_templates(text, strings) + chain_templates(text, strings):
                    keys.add(c)
            elif f.endswith(('.xaml', '.axaml')):
                xaml = open(p, encoding='utf-8-sig').read()
                for s in xaml_strings(xaml):
                    keys.add(s)
                keys.update(re.findall(r"StringFormat='([^']*[A-Za-zÀ-ÿ]{3}[^']*)'", xaml))
    for f in os.listdir(os.path.join(ROOT, 'resources', 'templates', 'export')):
        if f.endswith('.json'):
            data = json.load(open(os.path.join(ROOT, 'resources', 'templates', 'export', f), encoding='utf-8-sig'))
            for k in ('name', 'nome', 'description', 'descricao'):
                if isinstance(data.get(k), str):
                    keys.add(data[k])
    # Catálogo de modelos de IA: nome, origem, finalidade e recursos aparecem nas janelas de modelos.
    catalog = json.load(open(os.path.join(ROOT, 'resources', 'ai-models', 'modelos.json'), encoding='utf-8-sig'))
    for m in (catalog if isinstance(catalog, list) else next(v for v in catalog.values() if isinstance(v, list))):
        keys.update([m['name'], m['source'], m['purpose'], *m['features']])
    # O copyright vem do atributo do assembly.
    keys.update(re.findall(r'<Copyright>([^<]+)</Copyright>', open(os.path.join(ROOT, 'Directory.Build.props'), encoding='utf-8-sig').read()))
    # Textos curtos ou montados em código que a extração não reconhece sozinha.
    keys.update(['Auto', 'Automática', 'IA', 'Limite', 'Projetos', *LOWER_WORDS])
    texts = sorted(k for k in keys if (looks_like_text(k) or k == 'IA' or k in LOWER_WORDS) and k not in IGNORED)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(texts, open(OUT, 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=1)
    print(len(texts), 'textos em', OUT)


if __name__ == '__main__':
    sys.exit(main())
