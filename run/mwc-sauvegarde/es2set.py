"""Modifie un fichier Easy Save 2 en place (essais de « Verifier la sauvegarde ») :
  tag=pos:x,y,z     position d'un transform (nan accepte)
  tag=f:v           flottant
  tag=i:v           entier
  tag=blt:n         liste de vis : toutes a int(n)
Usage : python -I es2set.py <fichier> tag=... ..."""
import sys, struct

def entries(b):
    i = 0
    while i < len(b):
        n = b[i + 1]; s = i
        nm = b[i + 2:i + 2 + n].decode('utf-8', 'replace'); i += 2 + n
        ln = struct.unpack_from('<I', b, i)[0]; i += 4
        yield nm, s, i, ln
        i += ln

def main():
    path = sys.argv[1]
    b = bytearray(open(path, 'rb').read())
    for arg in sys.argv[2:]:
        tag, val = arg.split('=', 1)
        kind, v = val.split(':', 1)
        hit = [(s, d, ln) for nm, s, d, ln in entries(bytes(b)) if nm == tag]
        if not hit:
            print('absent :', tag); continue
        s, d, ln = hit[0]
        if kind == 'pos':
            x, y, z = (float(c) for c in v.split(','))
            struct.pack_into('<3f', b, d + 6, x, y, z)
        elif kind == 'f':
            struct.pack_into('<f', b, d + 5, float(v))
        elif kind == 'i':
            struct.pack_into('<i', b, d + 5, int(v))
        elif kind == 'blt':
            data = bytes(b[d:d + ln])
            cnt = struct.unpack_from('<I', data, 6)[0]
            body = b''.join(bytes([len('int(%s)' % v)]) + ('int(%s)' % v).encode() for _ in range(cnt))
            new = data[:10] + body + b'{'
            b[d - 4:d + ln] = struct.pack('<I', len(new)) + new
        print('ok :', arg)
    open(path, 'wb').write(bytes(b))

main()
