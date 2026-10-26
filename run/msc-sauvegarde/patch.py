# Modifie des valeurs d'une sauvegarde ES2 de My Summer Car sur place (meme longueur d'octets).
# usage : patch.py fichier tag=bool:0 tag=float:0 tag=liste:int(0) tag=pos:x,y,z ...
import sys, struct
p = sys.argv[1]
d = bytearray(open(p, 'rb').read())

def entry(tag):
    t = tag.encode('latin1')
    i = d.find(b'~' + bytes([len(t)]) + t)
    if i < 0: raise SystemExit('tag introuvable : ' + tag)
    o = i + 2 + len(t)
    return o + 4, int.from_bytes(d[o:o + 4], 'little')

for a in sys.argv[2:]:
    tag, spec = a.rsplit('=', 1)
    kind, val = spec.split(':', 1)
    b, ln = entry(tag)
    if kind == 'bool': d[b + 5] = int(val)
    elif kind == 'float': d[b + 5:b + 9] = struct.pack('<f', float(val))
    elif kind == 'pos': d[b + 6:b + 18] = struct.pack('<3f', *map(float, val.split(',')))
    elif kind == 'liste':
        k = b + 11; n = int.from_bytes(d[k - 4:k], 'little')
        for _ in range(n):
            sl = d[k]; k += 1
            if sl != len(val): raise SystemExit('longueur differente pour ' + tag)
            d[k:k + sl] = val.encode('latin1'); k += sl
    print('ok', tag, kind, val)
open(p, 'wb').write(d)
