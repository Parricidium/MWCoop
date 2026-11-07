# Compare deux dossiers de sauvegarde Easy Save 2 (My Winter Car ou My Summer Car) etiquette par etiquette :
# memes fichiers, memes etiquettes, memes octets pour chaque valeur. L'ordre des etiquettes dans un fichier ne compte pas
# (la sauvegarde groupee les ecrit dans son ordre) ; il est seulement signale.
#   python -I run/es2/es2cmp.py <dossier A> <dossier B> [--tout]
# Sortie : par fichier, etiquettes de A, de B, seulement dans A, seulement dans B, valeurs differentes (decodees si possible).
# Code de retour 0 si tout est identique, 1 sinon.
# Format ES2 (voir run/msc-sauvegarde/es2dump.py) : entree = '~', longueur de l'etiquette (7 bits), etiquette, longueur du
# corps (int32), corps (0xFF + empreinte du type + valeur, ou marqueur de collection).
import os, struct, sys

TYPES = {'9c7c4dad': 'bool', '6bd73e6e': 'float', '76fa7a09': 'transform', '5608a8e2': 'int', '314bcf32': 'color',
         'eef1e9fd': 'string', '46dc66ec': 'vector3'}


def rd7(b, o):
    n = s = 0
    while True:
        c = b[o]; o += 1; n |= (c & 0x7f) << s; s += 7
        if c < 128:
            return n, o


def parse(path):
    """Liste (etiquette, corps) dans l'ordre du fichier ; None si ce n'est pas un fichier ES2 lisible."""
    d = open(path, 'rb').read()
    out, i = [], 0
    while i < len(d):
        if d[i] != 0x7E:
            return None
        L, o = rd7(d, i + 1)
        tag = d[o:o + L].decode('utf-8', 'replace'); o += L
        if o + 4 > len(d):
            return None
        ln = struct.unpack('<i', d[o:o + 4])[0]; o += 4
        if ln < 0 or o + ln > len(d):
            return None
        out.append((tag, d[o:o + ln]))
        i = o + ln
    return out


def show(body):
    try:
        if body[:1] == b'\xff':
            t = TYPES.get(body[1:5].hex(), body[1:5].hex()); v = body[5:]
            if t == 'bool': return 'bool %s' % bool(v[0])
            if t == 'float': return 'float %r' % struct.unpack('<f', v[:4])[0]
            if t == 'int': return 'int %d' % struct.unpack('<i', v[:4])[0]
            if t == 'vector3': return 'vector3 %r' % (struct.unpack('<3f', v[:12]),)
            if t == 'color': return 'color %r' % (struct.unpack('<4f', v[:16]),)
            if t == 'string':
                n, o = rd7(v, 0); return 'string %r' % v[o:o + n].decode('utf-8', 'replace')
            if t == 'transform':
                f = struct.unpack('<10f', v[1:41]); return 'transform pos %r rot %r' % (f[0:3], f[3:7])
            return '%s %s' % (t, v[:24].hex())
        return 'collection %s... (%d octets)' % (body[:16].hex(), len(body))
    except Exception:
        return body[:24].hex()


def main():
    a, b = sys.argv[1], sys.argv[2]
    tout = '--tout' in sys.argv
    fa = {f for f in os.listdir(a) if os.path.isfile(os.path.join(a, f))}
    fb = {f for f in os.listdir(b) if os.path.isfile(os.path.join(b, f))}
    bad = 0
    for f in sorted(fa - fb): print('%s : seulement dans A' % f); bad += 1
    for f in sorted(fb - fa): print('%s : seulement dans B' % f); bad += 1
    for f in sorted(fa & fb):
        pa, pb = os.path.join(a, f), os.path.join(b, f)
        ra, rb = open(pa, 'rb').read(), open(pb, 'rb').read()
        ea, eb = parse(pa), parse(pb)
        if ea is None or eb is None:
            same = ra == rb
            print('%s : pas lu comme ES2, octets %s (%d / %d)' % (f, 'IDENTIQUES' if same else 'DIFFERENTS', len(ra), len(rb)))
            bad += 0 if same else 1
            continue
        da, db = dict(ea), dict(eb)
        dup = (len(da) != len(ea)) or (len(db) != len(eb))
        only_a = [t for t, _ in ea if t not in db]
        only_b = [t for t, _ in eb if t not in da]
        diff = [t for t, _ in ea if t in db and da[t] != db[t]]
        order = [t for t, _ in ea if t in db] == [t for t, _ in eb if t in da]
        ok = not only_a and not only_b and not diff and not dup
        print('%s : %d / %d etiquettes, %s%s%s, octets du fichier %s (%d / %d)' % (
            f, len(ea), len(eb), 'IDENTIQUES' if ok else 'ECARTS : %d seulement dans A, %d seulement dans B, %d valeurs differentes' % (len(only_a), len(only_b), len(diff)),
            '' if order else ', ordre different', ', ETIQUETTES EN DOUBLE' if dup else '',
            'identiques' if ra == rb else 'differents', len(ra), len(rb)))
        if not ok: bad += 1
        lim = None if tout else 25
        for t in only_a[:lim]: print('   seulement dans A : %s = %s' % (t, show(da[t])))
        for t in only_b[:lim]: print('   seulement dans B : %s = %s' % (t, show(db[t])))
        for t in diff[:lim]: print('   %s : A %s | B %s' % (t, show(da[t]), show(db[t])))
    print('TOUT IDENTIQUE' if bad == 0 else '%d fichier(s) avec des ecarts' % bad)
    return 0 if bad == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
