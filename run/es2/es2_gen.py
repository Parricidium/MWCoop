# Genere launcher/es2-pieces.inc depuis les tables des pieces (run/msc-sauvegarde/table-pieces-msc.tsv de [MSC] ; MWC plus tard) :
# pour « Verifier la sauvegarde », les pieces a vis (piece, liste des vis, serrage). Lance : python -X utf8 -I es2_gen.py
import os
here = os.path.dirname(os.path.abspath(__file__))
root = os.path.dirname(os.path.dirname(here))
def rows(path):
    out = []
    with open(path, encoding='utf-8') as f:
        next(f)
        for line in f:
            c = line.rstrip('\r\n').split('\t')
            if len(c) >= 5 and c[2] and c[3] and c[4]: out.append((c[2], c[3], c[4]))
    return out
def q(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
msc = rows(os.path.join(root, 'run', 'msc-sauvegarde', 'table-pieces-msc.tsv'))
with open(os.path.join(root, 'launcher', 'es2-pieces.inc'), 'w', encoding='utf-8', newline='\r\n') as o:
    o.write('// (genere par run/es2/es2_gen.py depuis run/msc-sauvegarde/table-pieces-msc.tsv : ne pas modifier a la main)\n')
    o.write('// Pieces a vis : <piece>Installed / <piece>Bolted, liste des vis (« int(n) »), serrage (somme des n).\n')
    o.write('static const Es2Part kEs2PartsMsc[] = {\n')
    for p, b, t in msc: o.write('    { %s, %s, %s },\n' % (q(p), q(b), q(t)))
    o.write('};\n')
print('es2-pieces.inc : %d pieces MSC' % len(msc))