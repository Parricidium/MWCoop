# Genere launcher/ru.inc et src/MWCoop/UI/LangRu.cs depuis ru_launcher.tsv / ru_mod.tsv (anglais brut <TAB> russe).
# Les cles sont recopiees telles qu'ecrites dans la source (memes echappements) ; verifie que les %d/%s/{0}/\n concordent.
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

def load(name):
    p = os.path.join(HERE, "ru_%s.tsv" % name)
    rows, seen, errs = [], set(), 0
    if not os.path.exists(p): return rows
    for n, line in enumerate(open(p, encoding="utf-8-sig"), 1):
        line = line.rstrip("\r\n")
        if not line.strip(): continue
        if "\t" not in line: print("%s:%d sans tabulation" % (name, n)); errs += 1; continue
        en, ru = line.split("\t", 1)
        if not ru.strip() or en in seen: continue
        spec = lambda s: sorted(re.findall(r"%[-+0-9.]*l?[dsSfxXuc%]|\{\d+[^}]*\}|\\n", s))
        if spec(en) != spec(ru):
            print("%s:%d formats differents : %s | %s" % (name, n, en[:60], ru[:60])); errs += 1; continue
        if re.search(r'(?<!\\)"', ru): print("%s:%d guillemet non echappe : %s" % (name, n, ru[:60])); errs += 1; continue
        seen.add(en); rows.append((en, ru))
    if errs: sys.exit("%d erreur(s) dans ru_%s.tsv" % (errs, name))
    return rows

lr = load("launcher")
lrk = set(en for en, ru in lr)
lr += [r for r in load("wiki") if r[0] not in lrk]   # (GUIDE du lanceur : ru_wiki.tsv, refonte du 14/10)
with open(os.path.join(ROOT, "launcher", "ru.inc"), "w", encoding="utf-8", newline="\n") as o:
    o.write("// Genere par ru_gen.py (traduction russe du lanceur, 13/10/2026) : texte anglais tel qu'ecrit dans T(), russe.\n")
    o.write("static const wchar_t *const kRu[][2] = {\n")
    for en, ru in lr: o.write('    { L"%s", L"%s" },\n' % (en, ru))
    if not lr: o.write('    { L"", L"" },\n')
    o.write("};\n")
mr = load("mod")
with open(os.path.join(ROOT, "src", "MWCoop", "UI", "LangRu.cs"), "w", encoding="utf-8-sig", newline="\n") as o:
    o.write("using System.Collections.Generic;\n\nnamespace MWCoop\n{\n")
    o.write("    // Genere par ru_gen.py (traduction russe des menus du mod, 13/10/2026) : texte anglais tel qu'ecrit dans Lang.T, russe.\n")
    o.write("    public static class LangRu\n    {\n        public static readonly Dictionary<string, string> Table = new Dictionary<string, string>\n        {\n")
    for en, ru in mr: o.write('            { "%s", "%s" },\n' % (en, ru))
    o.write("        };\n    }\n}\n")
print("lanceur", len(lr), "traductions ; mod", len(mr))
