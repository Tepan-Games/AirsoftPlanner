"""Extraction des textes à traduire (multilingue d'Airsoft Planner).

Remplace les textes français des écrans (.axaml) par {l:T clé} et ceux du code C# par L.T("clé") / L.F("clé", ...),
et complète src/AirsoftPlanner.Core/Localization/fr.json (langue de référence).

Usage : python tools/i18n/extract.py fichier1.axaml fichier2.cs ...
Les fichiers déjà convertis peuvent être repassés : seuls les nouveaux textes français sont traités.
"""
import json
import os
import re
import sys
import unicodedata

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FR = os.path.join(ROOT, "src", "AirsoftPlanner.Core", "Localization", "fr.json")

table = json.load(open(FR, encoding="utf-8")) if os.path.exists(FR) else {}
by_text = {v: k for k, v in table.items()}


def slug(text):
    t = unicodedata.normalize("NFKD", text)
    t = "".join(c for c in t if not unicodedata.combining(c)).lower()
    t = re.sub(r"\{\d+[^}]*\}", " x ", t)
    words = re.findall(r"[a-z0-9]+", t)
    s = "_".join(words)[:48].strip("_")
    return s or "texte"


def key_for(text):
    if text in by_text:
        return by_text[text]
    base = slug(text)
    key, n = base, 2
    while key in table:
        key, n = f"{base}_{n}", n + 1
    table[key] = text
    by_text[text] = key
    return key


LETTERS = re.compile(r"[A-Za-zÀ-ÿ]")


def is_ui_text(text):
    """Texte destiné à l'utilisateur (et non un nom technique, un format, une adresse...)."""
    t = text.strip()
    if not LETTERS.search(t):
        return False
    # Fragments d'adresse, paramètres, identifiants en capitales : techniques.
    if ("=" in t and ("&" in t or "?" in t)) or not re.search(r"[a-zà-ÿ]", t):
        return False
    if re.match(r"^(#[0-9A-Fa-f]{3,8}|https?://|/|\*\.|[a-z]+/[a-z0-9.+-]+$)", t):
        return False
    if re.fullmatch(r"[A-Za-z0-9_.:\-/\\{}]+", t):
        # Un seul « mot » sans espace : texte seulement s'il est accentué ou en français courant (Majuscule + minuscules).
        if re.search(r"[À-ÿ]", t):
            return True
        if re.fullmatch(r"[A-ZÀ-Ý][a-zà-ÿ]{2,}", t) and t not in SINGLE_WORD_TECH:
            return True
        return False
    if re.fullmatch(r"[A-Za-z]+(\.[A-Za-z]+)+", t):
        return False
    return True


SINGLE_WORD_TECH = {
    "Consolas", "Lato", "Segoe", "Default", "Normal", "Background", "Auto", "None", "Infantry", "Text", "Value",
    "Name", "Color", "Selected", "Items", "Missions", "Teams", "Zones", "Points", "Utm", "Monospace", "Bold",
    "Center", "Left", "Right", "Top", "Bottom", "True", "False", "Smartphone", "Meshtastic", "Traccar", "Webservice",
    "Fichier", "Manuel",
}

# ----- XAML -----

XAML_ATTR = re.compile(r'(?<![\w.])(Text|Content|Header|ToolTip\.Tip|PlaceholderText|Title|Watermark)="([^"]*)"')


def xml_unescape(v):
    return (v.replace("&#10;", "\n").replace("&quot;", '"').replace("&apos;", "'").replace("&lt;", "<")
            .replace("&gt;", ">").replace("&amp;", "&"))


def convert_xaml(path):
    s = open(path, encoding="utf-8-sig").read()
    count = 0

    def repl(m):
        nonlocal count
        name, value = m.group(1), m.group(2)
        if value.startswith("{") or not is_ui_text(xml_unescape(value)):
            return m.group(0)
        count += 1
        return f'{name}="{{l:T {key_for(xml_unescape(value))}}}"'

    s = XAML_ATTR.sub(repl, s)
    if count and 'xmlns:l="using:AirsoftPlanner.App.Localization"' not in s:
        s = re.sub(r'(<\w[\w.:]*\s+xmlns="https://github.com/avaloniaui")', r'\1\n        xmlns:l="using:AirsoftPlanner.App.Localization"', s, count=1)
    open(path, "w", encoding="utf-8-sig").write(s)
    return count


# ----- C# -----

SKIP_BEFORE = re.compile(r"(nameof\s*\(|PropertyName\s*(==|is|!=)|GetCultureInfo\s*\(|FromFamilyName\s*\(|FontFamily\s*\(|"
                         r"HasConversion|JsonPropertyName|GetManifestResourceStream|Environment\.|ContentType|\bcase\s+$|"
                         r"Header\s*==|\[\"|\.Parse\(|TryParse\(|Query\[|form\[|Path\.Combine\(|Regex|GetField\(|GetMethod\(|"
                         r"\bconst\s+string\b[^=]*=\s*$|Resource\.|Intent\.|ActionView|LocalizationJson|"
                         r"\.Replace\([^)]*|\.StartsWith\(|\.EndsWith\(|\.Contains\(|\.IndexOf\(|\.Split\(|\.Trim(Start|End)?\(|"
                         r"\.ToString\(|\.Equals\(|Headers\.|UserAgent|ProductInfoHeaderValue\(|GetString\(|GetProperty\(|"
                         r"TryGetProperty\(|ProcessStartInfo\(|Process\.Start\(|SetItemProperty)\s*$")


def csharp_unescape(body):
    out, i = [], 0
    while i < len(body):
        c = body[i]
        if c == "\\" and i + 1 < len(body):
            n = body[i + 1]
            out.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\", "'": "'", "r": "\r", "0": "\0"}.get(n, "\\" + n))
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def split_interpolation(body):
    """Découpe le contenu d'une chaîne interpolée en texte et expressions {expr[,align][:format]}."""
    parts, text, i = [], [], 0
    while i < len(body):
        c = body[i]
        if c == "{" and body[i + 1:i + 2] == "{":
            text.append("{{")
            i += 2
            continue
        if c == "}" and body[i + 1:i + 2] == "}":
            text.append("}}")
            i += 2
            continue
        if c == "{":
            depth, j, in_str, quote = 1, i + 1, False, ""
            while j < len(body) and depth:
                d = body[j]
                if in_str:
                    if d == "\\":
                        j += 2
                        continue
                    if d == quote:
                        in_str = False
                elif d in "\"'":
                    in_str, quote = True, d
                elif d in "({[":
                    depth += 1
                elif d in ")}]":
                    depth -= 1
                j += 1
            expr = body[i + 1:j - 1]
            # Format et alignement : premier « : » ou « , » au niveau 0, hors chaînes et parenthèses.
            level, in_str, quote, cut, fmt = 0, False, "", len(expr), ""
            for k, d in enumerate(expr):
                if in_str:
                    if d == quote and expr[k - 1] != "\\":
                        in_str = False
                    continue
                if d in "\"'":
                    in_str, quote = True, d
                elif d in "([{":
                    level += 1
                elif d in ")]}":
                    level -= 1
                elif d in ":," and level == 0:
                    cut, fmt = k, expr[k:]
                    break
            parts.append(("".join(text), expr[:cut], fmt))
            text = []
            i = j
            continue
        text.append(c)
        i += 1
    return parts, "".join(text)


STRING = re.compile(r'(\$@|@\$|\$|@)?"')


def convert_cs_code(s, depth=0):
    out, i, count = [], 0, 0
    while i < len(s):
        c = s[i]
        # Commentaires et caractères
        if s.startswith("//", i):
            j = s.find("\n", i)
            j = len(s) if j < 0 else j
            out.append(s[i:j])
            i = j
            continue
        if s.startswith("/*", i):
            j = s.find("*/", i) + 2
            out.append(s[i:j])
            i = j
            continue
        if c == "'" and (i == 0 or not s[i - 1].isalnum()):
            j = i + 1
            while j < len(s) and s[j] != "'":
                j += 2 if s[j] == "\\" else 1
            out.append(s[i:j + 1])
            i = j + 1
            continue
        if s.startswith('"""', i) or s.startswith('$"""', i) or s.startswith('$$"""', i):
            start = s.find('"""', i)
            j = s.find('"""', start + 3) + 3
            out.append(s[i:j])
            i = j
            continue
        m = STRING.match(s, i)
        if m and (i == 0 or not (s[i - 1].isalnum() or s[i - 1] == "_")):
            prefix = m.group(1) or ""
            verbatim, interp = "@" in prefix, "$" in prefix
            j = m.end()
            while j < len(s):
                if verbatim and s[j] == '"' and s[j + 1:j + 2] == '"':
                    j += 2
                    continue
                if not verbatim and s[j] == "\\":
                    j += 2
                    continue
                if interp and s[j] == "{" and s[j + 1:j + 2] != "{":
                    # sauter l'expression (peut contenir des chaînes)
                    level = 1
                    j += 1
                    while j < len(s) and level:
                        if s[j] == '"':
                            j += 1
                            while j < len(s) and s[j] != '"':
                                j += 2 if s[j] == "\\" else 1
                        elif s[j] == "{":
                            level += 1
                        elif s[j] == "}":
                            level -= 1
                        j += 1
                    continue
                if interp and s[j] == "{":
                    j += 2
                    continue
                if s[j] == '"':
                    break
                j += 1
            literal = s[i:j + 1]
            body = s[m.end():j]
            line_start = s.rfind("\n", 0, i) + 1
            before = s[line_start:i]
            replacement = literal
            if not verbatim and not before.lstrip().startswith("[") and not SKIP_BEFORE.search(before):
                if interp:
                    parts, tail = split_interpolation(body)
                    template, args = [], []
                    for text, expr, fmt in parts:
                        template.append(text)
                        template.append("{%d%s}" % (len(args), fmt))
                        converted, n = convert_cs_code(expr, depth + 1)
                        count += n
                        args.append(converted.strip())
                    template.append(tail)
                    text = csharp_unescape("".join(template))
                    visible = re.sub(r"\{\d+[^}]*\}", "", text)
                    if is_ui_text(visible) and re.search(r"[a-zà-ÿ]{2,}", visible):
                        replacement = 'L.F("%s", %s)' % (key_for(text), ", ".join(args)) if args else 'L.T("%s")' % key_for(text.replace("{{", "{").replace("}}", "}"))
                        count += 1
                    elif args:
                        # Chaîne sans texte à traduire : on garde la chaîne, avec ses expressions converties.
                        rebuilt, k = [], 0
                        for text_part, expr, fmt in parts:
                            rebuilt.append(text_part + "{" + args[k] + fmt + "}")
                            k += 1
                        replacement = "$\"" + "".join(rebuilt) + tail + "\""
                else:
                    text = csharp_unescape(body)
                    if is_ui_text(text):
                        replacement = 'L.T("%s")' % key_for(text)
                        count += 1
            out.append(replacement)
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out), count


def convert_cs(path):
    s = open(path, encoding="utf-8-sig").read()
    converted, count = convert_cs_code(s)
    if count and "using AirsoftPlanner.Core.Localization;" not in converted:
        lines = converted.split("\n")
        idx = next((k for k, l in enumerate(lines) if l.startswith("namespace ")), 0)
        last_using = max((k for k, l in enumerate(lines[:idx]) if l.startswith("using ")), default=-1)
        lines.insert(last_using + 1, "using AirsoftPlanner.Core.Localization;")
        converted = "\n".join(lines)
    open(path, "w", encoding="utf-8-sig").write(converted)
    return count


def prune():
    """Retire de fr.json les clés qui ne sont plus utilisées dans le code ni les écrans."""
    used = set()
    for folder in ("src",):
        for base, _, files in os.walk(os.path.join(ROOT, folder)):
            if os.sep + "obj" in base or os.sep + "bin" in base:
                continue
            for f in files:
                if f.endswith((".cs", ".axaml")):
                    text = open(os.path.join(base, f), encoding="utf-8-sig").read()
                    used.update(re.findall(r'L\.[TF]\("([a-z0-9_]+)"', text))
                    used.update(re.findall(r"\{l:T ([a-z0-9_]+)\}", text))
                    used.update(re.findall(r"FallbackValue=\{l:T ([a-z0-9_]+)\}", text))
    for key in [k for k in table if k not in used]:
        del table[key]


def main():
    total = 0
    if sys.argv[1:] == ["--prune"]:
        prune()
        json.dump(dict(sorted(table.items())), open(FR, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"{len(table)} clés dans fr.json")
        return
    for path in sys.argv[1:]:
        n = convert_xaml(path) if path.endswith(".axaml") else convert_cs(path)
        print(f"{n:4d}  {os.path.relpath(path, ROOT)}")
        total += n
    json.dump(dict(sorted(table.items())), open(FR, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"{total} textes, {len(table)} clés dans fr.json")


if __name__ == "__main__":
    main()
