# Textes des tables (pas dans un T()) : paires de litteraux adjacents (fr, en), tableaux *En[] et affectations .en/.En/.dEn.
# Ajoute a ru_src_<x>.tsv les textes anglais pas encore listes (colonne fichier suffixee de « (table) »).
import os, re

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.dirname(os.path.abspath(__file__))

def lits(s, cpp):
    p = r'L"((?:[^"\\]|\\.)*)"' if cpp else r'(?<![@\w])"((?:[^"\\]|\\.)*)"'
    return [(m.start(), m.end(), m.group(1)) for m in re.finditer(p, s)]

def cands(path, cpp):
    s = open(path, encoding="utf-8-sig").read()
    out = []
    L = lits(s, cpp)
    for a, b in zip(L, L[1:]):
        gap = s[a[1]:b[0]]
        if re.fullmatch(r"\s*,\s*", gap): out.append((b[2], a[2]))
    pre = "L" if cpp else ""
    for m in re.finditer(r"\b(\w*En|en|ze|le)\s*(\[\d*\])?\s*=\s*(new\s*\[\]\s*)?\{([^}]*)\}", s):
        for x in re.finditer(r'%s"((?:[^"\\]|\\.)*)"' % pre, m.group(4)): out.append((x.group(1), ""))
    for m in re.finditer(r"(en|En|dEn|NameEn)\s*=\s*%s\"((?:[^\"\\]|\\.)*)\"" % pre, s): out.append((m.group(2), ""))
    return out

def keep(en):
    if not re.search(r"[A-Za-z]{2}", en): return False
    if re.fullmatch(r"[\w.\\/-]+\.(png|jpg|ogg|txt|ini|dll|exe|json|zip|mp4|wav|cs|inc)", en, re.I): return False
    return True

def run(files, cpp, name):
    tsv = os.path.join(OUT, "ru_src_%s.tsv" % name)
    rows = [l.rstrip("\n").split("\t") for l in open(tsv, encoding="utf-8")]
    seen = set(r[0] for r in rows if r[1].find("(table)") < 0)
    rows = [r for r in rows if r[1].find("(table)") < 0]
    add = []
    for f in files:
        for en, fr in cands(f, cpp):
            if en in seen or not keep(en) or en == fr and not re.search(r"\s", en) and len(en) < 4: continue
            seen.add(en); add.append((en, os.path.basename(f) + " (table)", fr))
    with open(tsv, "w", encoding="utf-8") as o:
        for r in rows + add: o.write("\t".join(r) + "\n")
    print(name, len(rows), "+", len(add), "des tables")

L = os.path.join(ROOT, "launcher")
run([os.path.join(L, f) for f in sorted(os.listdir(L)) if f.endswith((".cpp", ".inc")) and not f.startswith("wiki") and f != "ru.inc"], True, "launcher")
M = os.path.join(ROOT, "src", "MWCoop")
run([os.path.join(M, p) for p in ("Gfx.cs", "Sync/Cheats.cs", "UI/Menu.cs", "Sync/Respawn.cs")], False, "mod")
