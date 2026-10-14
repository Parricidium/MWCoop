# Extrait les paires (fr, en) des textes du lanceur (T(L"..", L"..")) et du mod (Lang.T("..", "..")).
# Sortie : ru_src_launcher.tsv / ru_src_mod.tsv (en brut tel qu'ecrit dans la source, fichier, fr brut), et la liste
# des appels non litteraux.
import os, re, sys
WS = " \t\r\n"

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.dirname(os.path.abspath(__file__))

def skip(s, i):
    while i < len(s) and s[i] in WS: i += 1
    return i

def lit_seq(s, i, cpp):
    """Suite de litteraux a partir de i : adjacents en C++, relies par + en C#. Renvoie (texte brut concatene, nouvel i) ou None."""
    parts = []
    while True:
        j = skip(s, i)
        if parts and not cpp:
            if j < len(s) and s[j] == '+' and s[skip(s, j + 1):skip(s, j + 1) + 1] == '"': j = skip(s, j + 1)
            else: break
        k = j
        if cpp:
            if s.startswith('L"', k): k += 1
            else: break
        if k >= len(s) or s[k] != '"': break
        k += 1
        start = k
        while k < len(s) and s[k] != '"':
            if s[k] == '\\': k += 2
            else: k += 1
        parts.append(s[start:k])
        i = k + 1
    if not parts: return None
    return "".join(parts), i

def scan(path, cpp, pat):
    s = open(path, encoding="utf-8-sig").read()
    if os.path.basename(path) == "WaitScreen.cs": pat = r"(Lang\.T|(?<![\w.])L)\("   # (L : Lang.T)
    found, bad = [], []
    for m in re.finditer(pat, s):
        i = m.end()
        a = lit_seq(s, i, cpp)
        ok = False
        if a:
            fr, i2 = a
            i2 = skip(s, i2)
            if i2 < len(s) and s[i2] == ',':
                b = lit_seq(s, i2 + 1, cpp)
                if b:
                    en, i3 = b
                    i3 = skip(s, i3)
                    if i3 < len(s) and s[i3] == ')':
                        found.append((en, fr)); ok = True
        if not ok:
            line = s.count("\n", 0, m.start()) + 1
            bad.append("%s:%d: %s" % (os.path.basename(path), line, s[m.start():m.start() + 90].replace("\n", " ")))
    return found, bad

def run(files, cpp, pat, name):
    seen, rows, bads = {}, [], []
    for f in files:
        fo, ba = scan(f, cpp, pat)
        bads += ba
        for en, fr in fo:
            if en in seen: continue
            seen[en] = 1
            rows.append((en, os.path.basename(f), fr))
    with open(os.path.join(OUT, "ru_src_%s.tsv" % name), "w", encoding="utf-8") as o:
        for en, f, fr in rows: o.write("%s\t%s\t%s\n" % (en, f, fr))
    with open(os.path.join(OUT, "ru_bad_%s.txt" % name), "w", encoding="utf-8") as o:
        o.write("\n".join(bads))
    print(name, len(rows), "textes,", len(bads), "appels non litteraux")

L = os.path.join(ROOT, "launcher")
lf = [os.path.join(L, f) for f in sorted(os.listdir(L)) if f.endswith((".cpp", ".inc")) and not f.startswith("wikidata") and f != "ru.inc"]
run(lf, True, r"(?<![A-Za-z0-9_])T\(", "launcher")
M = os.path.join(ROOT, "src", "MWCoop")
mf = []
for d, _, fs in os.walk(M):
    for f in fs:
        if f.endswith(".cs") and f != "LangRu.cs": mf.append(os.path.join(d, f))
run(sorted(mf), False, r"Lang\.TG?\(", "mod")
