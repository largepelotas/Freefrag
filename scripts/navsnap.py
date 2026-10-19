"""Snap Freefrag route targets onto the CS2 nav mesh.

usage: python3 -I navsnap.py <map.vpk|map.nav> <outdir> <route.txt>...  [--only N,N,...]

For every target line (file line n+3 = target n) reports whether its (x, y) lies inside a
nav area and how far its z is from that area's plane. Targets listed in --only (1-based
target numbers, per file, as file:n) or every off-mesh target when --all is given are moved
to the nearest point on the nearest area (4 units inside its edge, z from the area plane)
and rewritten into <outdir>/<route>.txt; everything else in the file is copied verbatim.
"""
import struct, sys, os, math

MAGIC = 0xFEEDFACE

class R:
    def __init__(self, d): self.d = d; self.p = 0
    def take(self, n):
        if self.p + n > len(self.d): raise EOFError(f"short read at {self.p}")
        b = self.d[self.p:self.p+n]; self.p += n; return b
    def u8(self): return self.take(1)[0]
    def u32(self): return struct.unpack('<I', self.take(4))[0]
    def i32(self): return struct.unpack('<i', self.take(4))[0]
    def i64(self): return struct.unpack('<q', self.take(8))[0]
    def f32(self): return struct.unpack('<f', self.take(4))[0]
    def vec3(self): return struct.unpack('<fff', self.take(12))
    def skip(self, n): self.take(n)
    def align8(self): self.skip((8 - self.p % 8) % 8)
    def cstring(self):
        while self.u8() != 0: pass

def skip_kv3(r):
    r.align8()
    magic = r.u32()
    ver = magic & 0xFF
    if magic & 0xFFFFFF00 != 0x4B563300 or not (2 <= ver <= 5):
        raise ValueError(f"unsupported KV3 signature {magic:#010x}")
    r.skip(16); method = r.u32(); r.skip(4); r.skip(16); r.skip(4)
    unc_total = r.i32(); cmp_total = r.i32(); n_blocks = r.i32(); n_blobs = r.i32()
    size_block_compressed = 0
    if ver >= 4:
        r.skip(4); size_block_compressed = r.i32()
    if ver >= 5:
        unc1 = r.i32(); cmp1 = r.i32(); unc2 = r.i32(); cmp2 = r.i32(); r.skip(32)
    else:
        unc1, cmp1, unc2, cmp2 = unc_total, cmp_total, 0, 0
    if n_blocks or n_blobs: raise ValueError("KV3 with blocks/blobs")
    od = lambda u, c: u if method == 0 else c
    r.skip(od(unc1, cmp1) + od(unc2, cmp2) + size_block_compressed)

def parse_nav(d):
    r = R(d)
    if r.u32() != MAGIC: raise ValueError("bad magic")
    version = r.u32(); sub = r.u32(); unk1 = r.u32()
    if version >= 36: skip_kv3(r)
    polygons = None
    if version >= 31:
        nc = r.u32(); corners = [r.vec3() for _ in range(nc)]
        npoly = r.u32(); polygons = []
        for _ in range(npoly):
            n = r.u8(); poly = [corners[r.u32()] for _ in range(n)]
            if version >= 35: r.skip(4)
            polygons.append(poly)
    if version >= 32: r.skip(4)
    if version >= 35:
        cnt = r.u32()
        for _ in range(cnt): r.cstring(); r.skip(48)
    if version >= 36: skip_kv3(r)
    n_areas = r.u32(); areas = []
    for _ in range(n_areas):
        aid = r.u32(); flags = r.i64(); hull = r.u8()
        if polygons is not None:
            cs = polygons[r.u32()]
        else:
            cs = [r.vec3() for _ in range(r.u32())]
        r.skip(4)
        for _ in range(len(cs)):
            c = r.u32()
            for _ in range(c): r.u32(); r.u32()
        r.skip(5)
        for _ in range(r.u32()): pass
        for _ in range(r.u32()): pass
        areas.append((aid, hull, cs))
    return version, sub, areas

def vpk_extract(path, want):
    d = open(path, 'rb').read()
    sig, ver, tree_size = struct.unpack_from('<III', d, 0)
    assert sig == 0x55aa1234 and ver == 2, (sig, ver)
    hdr = 28
    r = R(d); r.p = hdr
    def s():
        b = bytearray()
        while True:
            c = r.u8()
            if c == 0: return b.decode('utf-8', 'replace')
            b.append(c)
    while True:
        ext = s()
        if ext == '': break
        while True:
            folder = s()
            if folder == '': break
            while True:
                name = s()
                if name == '': break
                crc, preload, arch, off, ln, term = struct.unpack_from('<IHHIIH', d, r.p); r.p += 18
                pre = r.take(preload)
                full = f"{folder}/{name}.{ext}" if folder != ' ' else f"{name}.{ext}"
                if full == want:
                    assert arch == 0x7fff, f"data in external archive {arch}"
                    base = hdr + tree_size
                    return bytes(pre) + d[base+off: base+off+ln]
    raise KeyError(want)

# ---- geometry ----
def plane_z(cs, x, y):
    # least-squares plane through the corners; falls back to the mean z for degenerate polygons
    n = len(cs)
    mx = sum(c[0] for c in cs)/n; my = sum(c[1] for c in cs)/n; mz = sum(c[2] for c in cs)/n
    sxx = sum((c[0]-mx)**2 for c in cs); syy = sum((c[1]-my)**2 for c in cs); sxy = sum((c[0]-mx)*(c[1]-my) for c in cs)
    sxz = sum((c[0]-mx)*(c[2]-mz) for c in cs); syz = sum((c[1]-my)*(c[2]-mz) for c in cs)
    det = sxx*syy - sxy*sxy
    if abs(det) < 1e-6: return mz
    a = (sxz*syy - syz*sxy)/det; b = (syz*sxx - sxz*sxy)/det
    return mz + a*(x-mx) + b*(y-my)

def contains_xy(cs, x, y):
    inside = False; n = len(cs); j = n-1
    for i in range(n):
        xi, yi = cs[i][0], cs[i][1]; xj, yj = cs[j][0], cs[j][1]
        if (yi > y) != (yj > y) and x < (xj-xi)*(y-yi)/(yj-yi) + xi: inside = not inside
        j = i
    return inside

def nearest_on_edges(cs, x, y):
    best = None
    n = len(cs)
    for i in range(n):
        ax, ay = cs[i][0], cs[i][1]; bx, by = cs[(i+1)%n][0], cs[(i+1)%n][1]
        dx, dy = bx-ax, by-ay; l2 = dx*dx+dy*dy
        t = 0 if l2 == 0 else max(0, min(1, ((x-ax)*dx + (y-ay)*dy)/l2))
        px, py = ax+t*dx, ay+t*dy
        dist = math.hypot(px-x, py-y)
        if best is None or dist < best[0]: best = (dist, px, py)
    return best

def centroid(cs):
    n = len(cs); return (sum(c[0] for c in cs)/n, sum(c[1] for c in cs)/n)

def classify(areas, x, y, z):
    """Returns (status, area, dz, snapped) where snapped = (x, y, z) on the mesh."""
    containing = [(abs(plane_z(cs, x, y) - z), aid, cs) for aid, hull, cs in areas if contains_xy(cs, x, y)]
    if containing:
        dz, aid, cs = min(containing, key=lambda t: t[0])
        return ('inside', aid, plane_z(cs, x, y) - z, (x, y, plane_z(cs, x, y)))
    best = None
    for aid, hull, cs in areas:
        dist, px, py = nearest_on_edges(cs, x, y)
        pz = plane_z(cs, px, py)
        score = math.hypot(dist, (pz - z) * 0.5)      # vertical mismatch counts half, so a ledge below is not preferred over the floor beside it
        if best is None or score < best[0]: best = (score, dist, aid, cs, px, py, pz)
    score, dist, aid, cs, px, py, pz = best
    cx, cy = centroid(cs); vx, vy = cx-px, cy-py; vl = math.hypot(vx, vy)
    if vl > 0: px += vx/vl*4; py += vy/vl*4          # 4 units inside the edge
    pz = plane_z(cs, px, py)
    return (f'outside by {dist:.1f}', aid, pz - z, (px, py, pz))

def fmt(v): return f"{v:.6f}"

def main():
    args = sys.argv[1:]
    only = set(); do_all = False
    if '--all' in args: do_all = True; args.remove('--all')
    if '--only' in args:
        i = args.index('--only'); only = set(args[i+1].split(',')); del args[i:i+2]
    src, outdir, routes = args[0], args[1], args[2:]
    data = open(src, 'rb').read() if src.endswith('.nav') else vpk_extract(src, 'maps/' + os.path.basename(src).replace('.vpk', '.nav'))
    version, sub, areas = parse_nav(data)
    hulls = {}
    for aid, hull, cs in areas: hulls[hull] = hulls.get(hull, 0) + 1
    print(f"nav version {version}.{sub}, {len(areas)} areas, hulls {hulls}")
    areas = [a for a in areas if a[1] == 0] or areas
    os.makedirs(outdir, exist_ok=True)
    for route in routes:
        name = os.path.basename(route)
        lines = open(route, encoding='utf-8').read().split('\n')
        out = list(lines); changed = 0
        print(f"\n== {name}")
        for li in range(3, len(lines)):
            t = lines[li].split()
            if len(t) < 7 or t[6] not in ('True', 'False'): continue
            tn = li - 2
            x, y, z = float(t[0]), float(t[1]), float(t[2])
            status, aid, dz, (nx, ny, nz) = classify(areas, x, y, z)
            key = f"{name}:{tn}"
            move = key in only or (do_all and ((status != 'inside' and float(status.split()[-1]) > 10) or dz < -20))
            flag = 'FIX' if move else '   '
            print(f"{flag} target {tn:2d} line {li+1:2d}  ({x:8.1f} {y:8.1f} {z:7.1f})  {status:16s} area {aid:5d}  dz {dz:+7.1f}" + (f"  -> ({nx:.1f} {ny:.1f} {nz:.1f})" if move else ''))
            if move:
                t[0], t[1], t[2] = fmt(nx), fmt(ny), fmt(nz)
                out[li] = ' '.join(t); changed += 1
        if changed:
            open(os.path.join(outdir, name), 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
            print(f"wrote {outdir}/{name} ({changed} lines changed)")

main()
