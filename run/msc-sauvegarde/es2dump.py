import sys, struct
H={'9c7c4dad':'bool','6bd73e6e':'float','76fa7a09':'transform','5608a8e2':'int','314bcf32':'color','eef1e9fd':'string','46dc66ec':'vector3'}
def rd7(b,o):
    n=0;s=0
    while True:
        c=b[o];o+=1;n|=(c&0x7f)<<s;s+=7
        if c<128:return n,o
def parse(p):
    d=open(p,'rb').read(); out=[]; i=0
    while True:
        j=d.find(b'~',i)
        if j<0 or j+1>=len(d): break
        L=d[j+1]; tag=d[j+2:j+2+L].decode('latin1'); o=j+2+L
        ln=int.from_bytes(d[o:o+4],'little'); body=d[o+4:o+4+ln]; i=o+4+ln
        if body[:1]==b'\xff':
            t=H.get(body[1:5].hex(),body[1:5].hex()); v=body[5:]
            if t=='bool': val=bool(v[0])
            elif t=='float': val=struct.unpack('<f',v[:4])[0]
            elif t=='int': val=struct.unpack('<i',v[:4])[0]
            elif t=='transform':
                f=struct.unpack('<10f',v[1:41]); tl,oo=rd7(v,41); val=(f[0:3],f[3:7],f[7:10],v[oo:oo+tl].decode('latin1'))
            elif t=='vector3': val=struct.unpack('<3f',v[:12])
            elif t=='string': sl,oo=rd7(v,0); val=v[oo:oo+sl].decode('latin1')
            else: val=v[:-1].hex()
        else:
            t='coll:'+chr(body[0]); val=body[:-1]
            # list of strings
            try:
                k=1
                assert body[k]==0xff; k+=5; k+=1  # hash + settings byte?
                n=int.from_bytes(body[k:k+4],'little'); k+=4; items=[]
                for _ in range(n):
                    sl,k=rd7(body,k); items.append(body[k:k+sl].decode('latin1')); k+=sl
                val=items
            except Exception as e: val='?'+body[:12].hex()
        out.append((tag,t,val))
    return out
if __name__=='__main__':
    for tag,t,v in parse(sys.argv[1]):
        print(t,'|',tag,'|',v)
