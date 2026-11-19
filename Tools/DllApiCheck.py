#!/usr/bin/env python3
"""컴파일된 모드 DLL(1.6 / 1.7)의 .NET 메타데이터를 읽어, 코드가 쓰려는 림월드·유니티 API가
"이미 두 빌드 모두에서 컴파일된 적 있는 API"인지 확인한다. (림월드·dotnet 없이 파이썬만으로 돈다)

왜 필요한가: 1.7용 DLL은 유니티가 아니라 UnityStubs를 참조해 빌드된다(`Tools/build_release.sh` 참고).
그래서 유니티에는 있는 API라도 1.7 쪽에는 없을 수 있다. 새 API를 쓰기 전에 아래로 확인하고,
두 빌드에 이미 참조가 있는 API만 쓰면 어느 빌드에서도 컴파일 오류가 날 가능성이 가장 낮다.

사용법(저장소 루트에서):
  python3 Tools/DllApiCheck.py                                  # RimketFrame.cs 가 쓰는 API 목록을 검사(없으면 종료코드 1)
  python3 Tools/DllApiCheck.py UnityEngine.Input.get_mousePosition Verse.Window.windowRect
  python3 Tools/DllApiCheck.py --list UnityEngine.Input         # 그 클래스에서 모드가 이미 쓰는 멤버 전부
  python3 Tools/DllApiCheck.py --dll 경로1.dll 경로2.dll ...     # 다른 DLL로 검사

읽는 법: "있음" = 그 빌드가 컴파일될 때 그 멤버가 실제로 있었다는 증거(시그니처도 나온다).
         "없음" = 모드가 아직 그 API를 안 썼다는 뜻일 뿐, API가 없다는 증거는 아니다 → 게임 DLL(Assembly-CSharp / UnityStubs)을 직접 확인할 것.
주의: 저장소의 DLL이 현재 소스보다 오래됐을 수 있다(가장 최근에 빌드한 DLL 기준의 증거다).
"""
import struct, sys

CODED = {
 'TypeDefOrRef': (2,[0x02,0x01,0x1B]), 'HasConstant': (2,[0x04,0x08,0x17]),
 'HasCustomAttribute': (5,[0x06,0x04,0x01,0x02,0x08,0x09,0x0A,0x00,0x0E,0x17,0x14,0x11,0x1A,0x1B,0x20,0x23,0x26,0x27,0x28,0x2A,0x2C,0x2B]),
 'HasFieldMarshal': (1,[0x04,0x08]), 'HasDeclSecurity': (2,[0x02,0x06,0x20]),
 'MemberRefParent': (3,[0x02,0x01,0x1A,0x06,0x1B]), 'HasSemantics': (1,[0x14,0x17]),
 'MethodDefOrRef': (1,[0x06,0x0A]), 'MemberForwarded': (1,[0x04,0x06]),
 'Implementation': (2,[0x26,0x23,0x27]), 'CustomAttributeType': (3,[None,None,0x06,0x0A,None]),
 'ResolutionScope': (2,[0x00,0x1A,0x23,0x01]), 'TypeOrMethodDef': (1,[0x02,0x06]),
}
T = lambda n: ('tbl', n)
C = lambda n: ('coded', n)
SCHEMA = {
 0x00:['u2','str','guid','guid','guid'], 0x01:[C('ResolutionScope'),'str','str'],
 0x02:['u4','str','str',C('TypeDefOrRef'),T(0x04),T(0x06)], 0x03:[T(0x04)], 0x04:['u2','str','blob'],
 0x05:[T(0x06)], 0x06:['u4','u2','u2','str','blob',T(0x08)], 0x07:[T(0x08)], 0x08:['u2','u2','str'],
 0x09:[T(0x02),C('TypeDefOrRef')], 0x0A:[C('MemberRefParent'),'str','blob'], 0x0B:['u2',C('HasConstant'),'blob'],
 0x0C:[C('HasCustomAttribute'),C('CustomAttributeType'),'blob'], 0x0D:[C('HasFieldMarshal'),'blob'],
 0x0E:['u2',C('HasDeclSecurity'),'blob'], 0x0F:['u2','u4',T(0x02)], 0x10:['u4',T(0x04)], 0x11:['blob'],
 0x12:[T(0x02),T(0x14)], 0x13:[T(0x14)], 0x14:['u2','str',C('TypeDefOrRef')], 0x15:[T(0x02),T(0x17)],
 0x16:[T(0x17)], 0x17:['u2','str','blob'], 0x18:['u2',T(0x06),C('HasSemantics')],
 0x19:[T(0x02),C('MethodDefOrRef'),C('MethodDefOrRef')], 0x1A:['str'], 0x1B:['blob'],
 0x1C:['u2',C('MemberForwarded'),'str',T(0x1A)], 0x1D:['u4',T(0x04)], 0x1E:['u4','u4'], 0x1F:['u4'],
 0x20:['u4','u2','u2','u2','u2','u4','blob','str','str'], 0x21:['u4'], 0x22:['u4','u4','u4'],
 0x23:['u2','u2','u2','u2','u4','blob','str','str','blob'], 0x24:['u4',T(0x23)], 0x25:['u4','u4','u4',T(0x23)],
 0x26:['u4','str','blob'], 0x27:['u4','u4','str','str',C('Implementation')],
 0x28:['u4','u4','str',C('Implementation')], 0x29:[T(0x02),T(0x02)], 0x2A:['u2','u2',C('TypeOrMethodDef'),'str'],
 0x2B:[C('MethodDefOrRef'),'blob'], 0x2C:[T(0x2A),C('TypeDefOrRef')],
}

class Dll:
    def __init__(self, path):
        d = self.d = open(path,'rb').read()
        pe = struct.unpack_from('<I', d, 0x3c)[0]
        assert d[pe:pe+4] == b'PE\0\0'
        nsec, = struct.unpack_from('<H', d, pe+6); optsize, = struct.unpack_from('<H', d, pe+20)
        opt = pe+24; magic, = struct.unpack_from('<H', d, opt)
        dd = opt + (96 if magic == 0x10b else 112)
        cli_rva, _ = struct.unpack_from('<II', d, dd+14*8)
        secs=[]; so = opt+optsize
        for i in range(nsec):
            _, vs, va, rs, rp = struct.unpack_from('<8sIIII', d, so+i*40); secs.append((va, max(vs,rs), rp))
        def r2o(rva):
            for va, sz, rp in secs:
                if va <= rva < va+sz: return rva-va+rp
            raise ValueError(rva)
        cli = r2o(cli_rva); md_rva, _ = struct.unpack_from('<II', d, cli+8); md = r2o(md_rva)
        assert struct.unpack_from('<I', d, md)[0] == 0x424A5342
        vlen, = struct.unpack_from('<I', d, md+12); p = md+16+vlen
        _, ns = struct.unpack_from('<HH', d, p); p += 4; st = {}
        for _ in range(ns):
            off, size = struct.unpack_from('<II', d, p); p += 8; j = p
            while d[j] != 0: j += 1
            name = d[p:j].decode(); p = (j+1+3) & ~3; st[name] = (md+off, size)
        self.strings = st['#Strings'][0]; self.blobs = st['#Blob'][0]
        tb = st.get('#~', st.get('#-'))[0]
        heap = d[tb+6]; valid, = struct.unpack_from('<Q', d, tb+8); p = tb+24
        self.rows = {}
        for t in range(64):
            if valid >> t & 1: self.rows[t], = struct.unpack_from('<I', d, p); p += 4
        self.sstr = 4 if heap & 1 else 2; self.sguid = 4 if heap & 2 else 2; self.sblob = 4 if heap & 4 else 2
        def tsz(n): return 4 if self.rows.get(n, 0) >= 65536 else 2
        def csz(name):
            bits, tabs = CODED[name]; mx = max([self.rows.get(t, 0) for t in tabs if t is not None] or [0])
            return 4 if mx >= (1 << (16-bits)) else 2
        def colsz(c):
            if c == 'u2': return 2
            if c == 'u4': return 4
            if c == 'str': return self.sstr
            if c == 'guid': return self.sguid
            if c == 'blob': return self.sblob
            if c[0] == 'tbl': return tsz(c[1])
            return csz(c[1])
        self.tab = {}
        for t in sorted(self.rows):
            sch = SCHEMA[t]; sizes = [colsz(c) for c in sch]; rs = sum(sizes)
            self.tab[t] = (p, rs, sch, sizes); p += rs*self.rows[t]
    def S(self, i):
        o = self.strings+i; j = o
        while self.d[j] != 0: j += 1
        return self.d[o:j].decode('utf8', 'replace')
    def Blob(self, i):
        o = self.blobs+i; b = self.d[o]
        if b & 0x80 == 0: n, o = b, o+1
        elif b & 0xC0 == 0x80: n, o = ((b & 0x3F) << 8) | self.d[o+1], o+2
        else: n, o = ((b & 0x1F) << 24) | (self.d[o+1] << 16) | (self.d[o+2] << 8) | self.d[o+3], o+4
        return self.d[o:o+n]
    def row(self, t, i):  # i is 1-based
        p, rs, sch, sizes = self.tab[t]; o = p+(i-1)*rs; out = []
        for c, s in zip(sch, sizes):
            v = int.from_bytes(self.d[o:o+s], 'little'); o += s; out.append(v)
        return out
    def decode_coded(self, name, v):
        bits, tabs = CODED[name]; return tabs[v & ((1 << bits)-1)], v >> bits
    # ---- names
    def typename(self, kind, idx):
        if kind == 0x01:
            r = self.row(0x01, idx); ns, nm = self.S(r[2]), self.S(r[1]); return (ns+'.' if ns else '')+nm
        if kind == 0x02:
            r = self.row(0x02, idx); ns, nm = self.S(r[2]), self.S(r[1]); return (ns+'.' if ns else '')+nm
        if kind == 0x1B: return '<typespec>'
        return '?'
    def tdr(self, v):
        k, i = self.decode_coded('TypeDefOrRef', v); return self.typename(k, i)
    # ---- signature decoding
    def sig(self, blob):
        pos = [0]
        def u():
            b = blob[pos[0]]
            if b & 0x80 == 0: pos[0] += 1; return b
            if b & 0xC0 == 0x80: v = ((b & 0x3F) << 8) | blob[pos[0]+1]; pos[0] += 2; return v
            v = ((b & 0x1F) << 24) | (blob[pos[0]+1] << 16) | (blob[pos[0]+2] << 8) | blob[pos[0]+3]; pos[0] += 4; return v
        P = {1:'void',2:'bool',3:'char',4:'sbyte',5:'byte',6:'short',7:'ushort',8:'int',9:'uint',10:'long',11:'ulong',12:'float',13:'double',14:'string',0x18:'IntPtr',0x19:'UIntPtr',0x1c:'object'}
        def ty():
            b = blob[pos[0]]; pos[0] += 1
            if b in P: return P[b]
            if b in (0x11, 0x12): return ('struct ' if b == 0x11 else '')+self.tdr(u())
            if b == 0x1d: return ty()+'[]'
            if b == 0x10: return 'ref '+ty()
            if b == 0x0f: return ty()+'*'
            if b == 0x15:
                ty_kind = blob[pos[0]]; pos[0] += 1; base = self.tdr(u()); n = u(); args = [ty() for _ in range(n)]
                return base+'<'+', '.join(args)+'>'
            if b == 0x13: return '!'+str(u())
            if b == 0x1e: return '!!'+str(u())
            if b == 0x14:
                et = ty(); rank = u(); ns = u(); [u() for _ in range(ns)]; nl = u(); [u() for _ in range(nl)]; return et+'['+','*(rank-1)+']'
            if b in (0x1f, 0x20): u(); return ty()
            if b == 0x41: return ty()
            return '?%02x' % b
        cc = blob[pos[0]]; pos[0] += 1
        if cc == 0x06: return 'field '+ty()
        if cc & 0x10: u()
        n = u(); ret = ty(); ps = [ty() for _ in range(n)]
        return ('instance ' if cc & 0x20 else 'static ')+ret+'('+', '.join(ps)+')'
    # ---- convenience
    def assembly_refs(self):
        out = []
        for i in range(1, self.rows.get(0x23, 0)+1):
            r = self.row(0x23, i); out.append((self.S(r[6]), '%d.%d.%d.%d' % tuple(r[:4])))
        return out
    def memberrefs(self):
        out = []
        for i in range(1, self.rows.get(0x0A, 0)+1):
            r = self.row(0x0A, i); k, ix = self.decode_coded('MemberRefParent', r[0])
            cls = self.typename(k, ix) if k in (0x01, 0x02) else '<'+str(k)+'>'
            out.append((cls, self.S(r[1]), self.sig(self.Blob(r[2]))))
        return out
    def methoddefs(self):
        """yield (typename, base typename, methodname, flags, sig)"""
        nt = self.rows[0x02]; nm = self.rows[0x06]
        for t in range(1, nt+1):
            tr = self.row(0x02, t); start = tr[5]; end = self.row(0x02, t+1)[5] if t < nt else nm+1
            ext = tr[3]; base = self.tdr(ext) if ext else None
            tn = self.typename(0x02, t)
            for m in range(start, end):
                mr = self.row(0x06, m)
                yield tn, base, self.S(mr[3]), mr[2], self.sig(self.Blob(mr[4])), mr[1]


# ---------------------------------------------------------------------------- CLI
import os, sys

DEFAULT_LIST = [
    "UnityEngine.Mathf.Min", "UnityEngine.Mathf.Max", "UnityEngine.Mathf.Clamp", "UnityEngine.Mathf.Round", "UnityEngine.Mathf.Abs",
    "UnityEngine.Rect..ctor", "UnityEngine.Rect.get_x", "UnityEngine.Rect.get_y", "UnityEngine.Rect.get_width", "UnityEngine.Rect.get_height", "UnityEngine.Rect.Contains",
    "UnityEngine.Vector2..ctor", "UnityEngine.Vector2.x", "UnityEngine.Vector2.y",
    "UnityEngine.Input.GetMouseButton", "UnityEngine.Time.get_frameCount",
    "UnityEngine.Event.get_current", "UnityEngine.Event.get_mousePosition", "UnityEngine.Event.get_type", "UnityEngine.Event.get_button", "UnityEngine.Event.Use",
    "UnityEngine.GUI.set_color", "UnityEngine.GUI.DrawTexture", "UnityEngine.Color..ctor", "UnityEngine.Color.get_white",
    "Verse.UI.screenWidth", "Verse.UI.screenHeight", "Verse.BaseContent.WhiteTex",
    "Verse.Text.get_Font", "Verse.Text.set_Font", "Verse.Text.get_Anchor", "Verse.Text.set_Anchor",
    "Verse.Widgets.Label", "Verse.TooltipHandler.TipRegion", "Verse.TipSignal.op_Implicit",
    "Verse.Window.windowRect", "Verse.Window.WindowUpdate", "Verse.ModSettings.Write",
    "Verse.Find.get_WindowStack", "Verse.WindowStack.get_Windows",
]

def main(argv):
    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    dlls = {}
    args = list(argv)
    if "--dll" in args:
        i = args.index("--dll"); paths = args[i+1:]; args = args[:i]
        for p in paths: dlls[os.path.basename(os.path.dirname(os.path.dirname(p))) or p] = p
    else:
        for v in ("1.6", "1.7"):
            p = os.path.join(root, "Rimketmon", v, "Assemblies", "Rimketmon.dll")
            if os.path.exists(p): dlls[v] = p
    if not dlls: print("DLL을 찾지 못했다(--dll 로 지정)."); return 2
    refs = {v: Dll(p).memberrefs() for v, p in dlls.items()}
    if args and args[0] == "--list":
        cls = args[1]
        for v in dlls:
            print("[%s] %s" % (v, cls))
            for n, s in sorted(set((n, s) for c, n, s in refs[v] if c == cls)): print("    %-28s %s" % (n, s))
        return 0
    wanted = args or DEFAULT_LIST
    missing = 0
    for w in wanted:
        cls, _, mem = w.rpartition(".")
        if mem == "" and cls.endswith("."): pass
        if w.endswith("..ctor"): cls, mem = w[:-6], ".ctor"
        row = []
        for v in dlls:
            sigs = sorted(set(s for c, n, s in refs[v] if c == cls and n == mem))
            row.append((v, sigs))
        ok = all(sigs for v, sigs in row)
        missing += 0 if ok else 1
        print(("OK  " if ok else "??  ") + w)
        for v, sigs in row:
            print("      %s: %s" % (v, ("있음  " + " | ".join(sigs)[:150]) if sigs else "없음(모드가 아직 안 쓴 API일 수 있음)"))
    print("\n확인 안 된 항목: %d / %d" % (missing, len(wanted)))
    return 1 if missing else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
