# Genere launcher/ar.inc (traduction arabe du lanceur) depuis ar_in_N.tsv (ID <TAB> anglais tel qu'ecrit dans T()) et
# ar_out_N.tsv (ID <TAB> arabe) ; garde aussi ar_launcher.tsv (anglais <TAB> arabe), comme ru_launcher.tsv.
# Le sens d'ecriture (droite a gauche) est pose a l'affichage par le lanceur (RtlText). Verifie que les %d/%s/{0}/\n concordent.  python -X utf8 -I ar_gen.py
import glob, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
spec = lambda s: sorted(re.findall(r"%[-+0-9.]*l?[dsSfxXuc%]|\{\d+[^}]*\}|\\n", s))
en_by_id, id_file = {}, {}
for p in sorted(glob.glob(os.path.join(HERE, "ar_in_*.tsv"))):
    for line in io.open(p, encoding="utf-8-sig"):
        line = line.rstrip("\r\n")
        if "\t" in line:
            i, en = line.split("\t", 1); i = i.strip()
            # (un identifiant dans deux lots : le dernier gagnait et l'arabe partait sur le mauvais texte -- 28/10, lots 24/78 et 81/82)
            if i in id_file and id_file[i] != os.path.basename(p): sys.exit("ID %s dans %s ET %s : prefixe-le par son lot (L<n>-...)" % (i, id_file[i], os.path.basename(p)))
            id_file[i] = os.path.basename(p); en_by_id[i] = en
rows, seen, errs = [], set(), 0
for p in sorted(glob.glob(os.path.join(HERE, "ar_out_*.tsv"))):
    for n, line in enumerate(io.open(p, encoding="utf-8-sig"), 1):
        line = line.rstrip("\r\n")
        if not line.strip(): continue
        if "\t" not in line: print("%s:%d sans tabulation" % (os.path.basename(p), n)); errs += 1; continue
        i, ar = line.split("\t", 1); i = i.strip()
        en = en_by_id.get(i)
        if en is None: print("%s:%d ID inconnu %s" % (os.path.basename(p), n, i)); errs += 1; continue
        if not ar.strip() or en in seen: continue
        if spec(en) != spec(ar): print("%s:%d formats differents : %s | %s" % (os.path.basename(p), n, en[:60], ar[:60])); errs += 1; continue
        if re.search(r'(?<!\\)"', ar): print("%s:%d guillemet non echappe : %s" % (os.path.basename(p), n, ar[:60])); errs += 1; continue
        seen.add(en); rows.append((en, ar))
if errs: sys.exit("%d erreur(s)" % errs)
with io.open(os.path.join(HERE, "ar_launcher.tsv"), "w", encoding="utf-8", newline="\n") as o:
    for en, ar in rows: o.write("%s\t%s\n" % (en, ar))
def wrap(ar):
    return ar   # (sens d'ecriture : pose a l'affichage, DrawStr / RtlText du lanceur)
with io.open(os.path.join(ROOT, "launcher", "ar.inc"), "w", encoding="utf-8", newline="\n") as o:
    o.write("// Genere par run/ar/ar_gen.py (traduction arabe du lanceur, 13/10/2026) : texte anglais tel qu'ecrit dans T(), arabe.\n")
    o.write("static const wchar_t *const kAr[][2] = {\n")
    for en, ar in rows: o.write('    { L"%s", L"%s" },\n' % (en, wrap(ar)))
    if not rows: o.write('    { L"", L"" },\n')
    o.write("};\n")
print("lanceur : %d traductions arabes" % len(rows))
