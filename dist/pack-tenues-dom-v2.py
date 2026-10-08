# Pack de Dom version 2 : sans dom_haut_77 (GIFU TRUCKS) ni dom_haut_83 (Peraburger), retires a la demande de JD (08/10).
# Les autres gardent leur nom (pas de renumerotation : les tenues deja choisies restent les memes).
import zipfile, sys
src, dst = sys.argv[1], sys.argv[2]
drop = {'dom/dom_haut_77.jpg', 'dom/dom_haut_83.jpg'}
zi = zipfile.ZipFile(src)
with zipfile.ZipFile(dst, 'w', zipfile.ZIP_STORED) as zo:
    for n in zi.namelist():
        if n in drop:
            continue
        data = zi.read(n)
        if n == 'dom/version.txt':
            data = b'2'
        elif n == 'dom/origine.txt':
            t = data.decode('utf-8')
            t = t.replace('\n\n', '\n\nVersion 2 (08/10/2026) : dom_haut_77 et dom_haut_83 retires (demande de JD).\n\n', 1)
            t = '\n'.join(l for l in t.split('\n') if not (l.startswith('dom_haut_77') or l.startswith('dom_haut_83')))
            data = t.encode('utf-8')
        zo.writestr(n, data)
print(len(zipfile.ZipFile(dst).namelist()), 'fichiers')
