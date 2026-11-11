"""
Oblivion-inspired modular foliage pack generator for Blender, styled to match Aelid's Oak Grove kit
(same leaf/stone/bark palettes, faceted leaves, flat-shaded low-poly rocks and wood).
Run:  blender -b --python build_foliage.py -- <output_dir> [<unity_kit_dir>]
      e.g. from the project root:
      blender -b --python ArtSource/OblivionFoliage/build_foliage.py -- ArtSource/OblivionFoliage Assets/_Project/Art/Environment/OblivionFoliage

Creates:
  Textures/T_Foliage_Atlas.png         painted leaf/fern/ivy/grass/flower/debris atlas (RGBA, alpha-cut)
  Textures/T_<asset>.png               baked albedo for rocks, logs, stump
  Exports/GLB/<asset>.glb              one file per asset (textures embedded)
  Exports/FBX/<asset>.fbx              one file per asset (textures embedded)
  FoliagePack.blend                    all assets, marked as Blender assets, laid out for preview
Every asset: metres, +Z up, origin on the ground contact point (hanging vine: origin at ledge top edge),
transforms applied, named SM_*.
"""
import bpy, bmesh, math, random, os, sys
import numpy as np
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else os.getcwd())
TEX = os.path.join(OUT, 'Textures')
GLB = os.path.join(OUT, 'Exports', 'GLB')
FBX = os.path.join(OUT, 'Exports', 'FBX')
for d in (TEX, GLB, FBX):
    os.makedirs(d, exist_ok=True)
for _fn in os.listdir(TEX):          # drop stale textures (e.g. normal maps from older builds)
    if _fn.endswith('.png'):
        os.remove(os.path.join(TEX, _fn))
UNITY_KIT = os.path.abspath(argv[1]) if len(argv) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
COL = bpy.data.collections.new('Foliage_Assets')
scene.collection.children.link(COL)
PREV = bpy.data.collections.new('Preview_Scene')
scene.collection.children.link(PREV)

# ----------------------------------------------------------------------------------------------
# 1. Painted foliage atlas (numpy)
# ----------------------------------------------------------------------------------------------
AW = AH = 1024
CELLS = {  # x0, y0, x1, y1 in pixels, origin top-left
    'broad':   (0, 0, 512, 512),
    'small':   (512, 0, 1024, 512),
    'fern':    (0, 512, 256, 1024),
    'ivy':     (256, 512, 512, 1024),
    'grass':   (512, 512, 768, 1024),
    'flowers': (768, 512, 1024, 768),
    'debris':  (768, 768, 1024, 1024),
}
R = random.Random(7)


def over(dst, rgb, a):
    a3 = a[..., None]
    dst[..., :3] = rgb * a3 + dst[..., :3] * (1 - a3)
    dst[..., 3] = a + dst[..., 3] * (1 - a)


def jc(c, amt=0.06):
    f = 1 + R.uniform(-amt, amt)
    return np.clip(np.array([c[0] * f * (1 + R.uniform(-.03, .03)), c[1] * f,
                             c[2] * f * (1 + R.uniform(-.05, .05))], np.float32), 0, 1)


# --- Palettes sampled from the Aelid Oak Grove kit (Blender materials, converted to sRGB) ---------
OAK_LEAF = [(0.204, 0.293, 0.209), (0.244, 0.351, 0.221), (0.298, 0.389, 0.234),
            (0.362, 0.448, 0.256), (0.418, 0.503, 0.285), (0.505, 0.571, 0.324)]   # Oak | Leaf 1-6
OAK_STEM = (0.560, 0.540, 0.380)       # sprays: pale olive-tan twigs
OAK_RIB = (0.700, 0.715, 0.545)        # pale cream midrib
OAK_BARK = [(0.349, 0.278, 0.226), (0.407, 0.324, 0.258), (0.486, 0.396, 0.310),
            (0.293, 0.241, 0.210), (0.551, 0.468, 0.349)]                          # Oak | Bark 1-5
OAK_STONE = [(0.463, 0.491, 0.404), (0.583, 0.586, 0.498), (0.396, 0.422, 0.362),
             (0.700, 0.676, 0.564)]                                                # Grove | Stone 1-4
BLUEBELL = (0.415, 0.514, 0.614)       # Grove | Bluebells
TRAIL = (0.567, 0.505, 0.381)          # Grove | Trail


def pick(pal, weights):
    return R.choices(pal, weights=weights)[0]


def leaf(cell, base, ang, L, Wd, col, shape=0.7, rib=True, lobes=0.0, facets=4, ribcol=OAK_RIB):
    """Oak-kit style leaf: wavy angular edge, flat faceted bands, pale midrib."""
    h, w = cell.shape[:2]
    bx, by = base
    dx, dy = math.cos(ang), math.sin(ang)
    tx, ty = bx + dx * L, by + dy * L
    x0 = int(max(0, min(bx, tx) - Wd - 2)); x1 = int(min(w, max(bx, tx) + Wd + 2))
    y0 = int(max(0, min(by, ty) - Wd - 2)); y1 = int(min(h, max(by, ty) + Wd + 2))
    if x1 <= x0 or y1 <= y0:
        return
    ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    rx, ry = xs - bx, ys - by
    u = rx * dx + ry * dy
    v = -rx * dy + ry * dx
    t = u / L
    tc = np.clip(t, 0, 1)
    prof = Wd * np.power(np.clip(np.sin(np.pi * np.power(tc.astype(np.float64), shape)), 0, None), 0.9)
    if lobes > 0:
        tri = np.abs(np.mod(lobes * tc + 0.25, 1.0) - 0.5) * 2.0    # angular teeth, like the oak leaves
        prof = prof * (0.82 + 0.18 * tri)
    a = np.clip((prof - np.abs(v)) * 0.9 + 0.5, 0, 1) * ((t > 0) & (t < 1))
    if a.max() <= 0:
        return
    # flat facets: bands along the leaf, each half its own tone (the kit's low-poly leaf look)
    fb = np.array([R.uniform(0.86, 1.10) for _ in range(2 * facets)], np.float32)
    fidx = np.clip((tc * facets).astype(np.int32), 0, facets - 1) + (v > 0).astype(np.int32) * facets
    shade = fb[fidx] * (0.96 + 0.06 * tc)
    rgb = np.asarray(col, np.float32)[None, None, :] * shade[..., None]
    if rib:
        rm = (np.clip(1.1 - np.abs(v), 0, 1) * (t < 0.9))[..., None]
        rgb = rgb * (1 - 0.8 * rm) + np.asarray(ribcol, np.float32)[None, None, :] * 0.8 * rm
    over(cell[y0:y1, x0:x1], np.clip(rgb, 0, 1), a)


def stem(cell, p0, p1, w0, w1, col):
    h, w = cell.shape[:2]
    r = max(w0, w1) / 2 + 2
    x0 = int(max(0, min(p0[0], p1[0]) - r)); x1 = int(min(w, max(p0[0], p1[0]) + r + 1))
    y0 = int(max(0, min(p0[1], p1[1]) - r)); y1 = int(min(h, max(p0[1], p1[1]) + r + 1))
    if x1 <= x0 or y1 <= y0:
        return
    ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    d = np.array(p1, np.float32) - np.array(p0, np.float32)
    dd = max(float(d @ d), 1e-6)
    t = np.clip(((xs - p0[0]) * d[0] + (ys - p0[1]) * d[1]) / dd, 0, 1)
    px, py = p0[0] + t * d[0], p0[1] + t * d[1]
    dist = np.sqrt((xs - px) ** 2 + (ys - py) ** 2)
    hw = (w0 + (w1 - w0) * t) * 0.5
    a = np.clip(hw - dist + 0.5, 0, 1)
    sh = 0.7 + 0.4 * np.clip(1 - dist / (hw + 1e-3), 0, 1)
    rgb = np.asarray(col, np.float32)[None, None, :] * sh[..., None]
    over(cell[y0:y1, x0:x1], np.clip(rgb, 0, 1), a)


def polyline(cell, pts, w0, w1, col):
    n = len(pts) - 1
    for i in range(n):
        stem(cell, pts[i], pts[i + 1], w0 + (w1 - w0) * i / n, w0 + (w1 - w0) * (i + 1) / n, col)


def walk(base, ang, L, segs=5, wob=0.15):
    pts = [np.array(base, float)]
    p = np.array(base, float)
    for _ in range(segs):
        ang += R.uniform(-wob, wob)
        p = p + np.array([math.cos(ang), math.sin(ang)]) * L / segs
        pts.append(p.copy())
    return pts


def along(pts, s):
    segs = len(pts) - 1
    f = s * segs
    i = min(int(f), segs - 1)
    fr = f - i
    d = pts[i + 1] - pts[i]
    return pts[i] + d * fr, math.atan2(d[1], d[0])


def twig_cluster(cell, base, n, spread, tlen, lL, lW, pal, weights, tcol, spacing, shape=0.7, lobes=0.0,
                 facets=4, tw=(5, 2)):
    for _ in range(n):
        pts = walk(base, -math.pi / 2 + R.uniform(-spread, spread), R.uniform(*tlen))
        polyline(cell, pts, tw[0], tw[1], tcol)
        side, s = 1, 0.18
        while s <= 1.0:
            pos, da = along(pts, s)
            leaf(cell, pos, da + side * R.uniform(0.55, 1.0), R.uniform(*lL), R.uniform(*lW),
                 jc(pick(pal, weights)), shape, lobes=lobes, facets=facets)
            side = -side
            s += spacing * R.uniform(0.7, 1.3)
        pos, da = along(pts, 1.0)
        leaf(cell, pos, da, R.uniform(*lL), R.uniform(*lW), jc(pick(pal, weights)), shape, lobes=lobes, facets=facets)


def paint_atlas(path):
    img = np.zeros((AH, AW, 4), np.float32)
    img[..., :3] = OAK_LEAF[1]   # bleed colour under the cut-out edges

    def view(c):
        x0, y0, x1, y1 = CELLS[c]
        return img[y0:y1, x0:x1]

    W_MID = [1, 3, 4, 4, 3, 2]     # mostly the kit's middle greens
    # broad cell: oak leaves, same shape/shading as the Oak Grove sprays
    twig_cluster(view('broad'), (256, 505), 8, 0.75, (220, 330), (66, 92), (24, 31),
                 OAK_LEAF, W_MID, OAK_STEM, 0.14, shape=0.8, lobes=4.5, facets=5)
    # small cell: smaller, plainer leaves in the same palette (privet/box-like shrubs)
    twig_cluster(view('small'), (256, 505), 12, 0.85, (200, 290), (34, 48), (13, 17),
                 OAK_LEAF, [2, 4, 4, 3, 2, 1], OAK_STEM, 0.075, shape=0.75, lobes=2.0, facets=3, tw=(4, 1.5))
    # fern frond
    c = view('fern')
    curve = R.uniform(-1, 1) * 18
    pts = [np.array([128 + curve * math.sin(math.pi * s), 505 - 495 * s]) for s in np.linspace(0, 1, 12)]
    polyline(c, pts, 6, 2, OAK_STEM)
    s = 0.06
    while s < 0.985:
        pos, da = along(pts, s)
        Lp = 108 * (1 - s) ** 0.7 * min(1.0, s * 7) + 6
        Wp = 4 + 6 * (1 - s)
        for sd in (-1, 1):
            leaf(c, pos, da + sd * (1.25 - 0.45 * s), Lp, Wp, jc(pick(OAK_LEAF, [0, 1, 3, 4, 3, 1])), 0.55,
                 rib=False, lobes=3.0, facets=2)
        s += 0.028
    # ivy strip (hangs down; top of cell = attachment): darker end of the oak palette
    c = view('ivy')
    ys = np.arange(0, 512, 4)
    pts = [np.array([128 + 28 * math.sin(y / 55.0), float(y)]) for y in ys]
    polyline(c, pts, 5, 4, OAK_BARK[1])
    side = 1
    for y in range(8, 500, 24):
        sx = 128 + 28 * math.sin(y / 55.0)
        p0 = (sx, y); p1 = (sx + side * 12, y + 6)
        stem(c, p0, p1, 3, 2, OAK_STEM)
        leaf(c, p1, math.pi / 2 - side * R.uniform(0.5, 1.1), R.uniform(50, 64), R.uniform(27, 35),
             jc(pick(OAK_LEAF, [3, 4, 3, 2, 1, 0])), 0.45, lobes=1.5, facets=3)
        side = -side
    # grass blades: the kit's lighter greens plus a few dry stalks
    c = view('grass')
    for _ in range(30):
        bx = 128 + R.gauss(0, 24)
        a0 = -math.pi / 2 + R.uniform(-0.35, 0.35)
        lean = 1 if a0 > -math.pi / 2 else -1
        pts = [np.array([bx, 508.0])]
        p = pts[0].copy(); a = a0; L = R.uniform(250, 480)
        for _ in range(8):
            a += R.uniform(-0.05, 0.05) + lean * 0.035
            p = p + np.array([math.cos(a), math.sin(a)]) * L / 8
            pts.append(p.copy())
        col = pick(OAK_LEAF + [(0.58, 0.57, 0.36)], [0, 0, 2, 4, 4, 3, 2])
        polyline(c, pts, 7.5, 1.2, jc(col))
    # flowers: bluebells (the grove's flower colour) with a few cream ones
    c = view('flowers')
    for _ in range(9):
        leaf(c, (128, 128), R.uniform(0, 2 * math.pi), R.uniform(40, 62), R.uniform(14, 20),
             jc(pick(OAK_LEAF, [1, 3, 4, 3, 1, 0])), 0.6, facets=2)
    for _ in range(16):
        cx = 128 + R.gauss(0, 48); cy = 128 + R.gauss(0, 42)
        cream = R.random() < 0.25
        pc = (0.86, 0.83, 0.64) if cream else (BLUEBELL[0] * 1.05, BLUEBELL[1] * 1.0, BLUEBELL[2] * 1.15)
        rot = R.uniform(0, 2 * math.pi)
        pl, pw = R.uniform(18, 26), R.uniform(11, 14)
        for k in range(5):
            leaf(c, (cx, cy), rot + k * 2 * math.pi / 5, pl, pw, jc(pc), 0.55, rib=False, facets=1)
        stem(c, (cx, cy), (cx, cy), 7, 7, (0.80, 0.76, 0.52))
    # debris: dry twigs + fallen oak leaves in the kit's bark/trail browns
    c = view('debris')
    for _ in range(6):
        pts = walk((R.uniform(30, 226), R.uniform(30, 226)), R.uniform(0, 2 * math.pi), R.uniform(80, 170), 4, 0.3)
        polyline(c, pts, 5, 2, OAK_BARK[1])
        pos, da = along(pts, 0.5)
        polyline(c, walk(pos, da + R.choice((-1, 1)) * 0.7, 50, 3, 0.2), 3, 1.5, OAK_BARK[2])
    for _ in range(38):
        leaf(c, (R.uniform(20, 236), R.uniform(20, 236)), R.uniform(0, 2 * math.pi), R.uniform(26, 44),
             R.uniform(10, 15), jc(R.choice([TRAIL, OAK_BARK[4], OAK_BARK[2], (0.52, 0.50, 0.30), OAK_LEAF[3]])),
             0.8, lobes=3.5, facets=3)

    # per-cell finishing: darker bases on grass/fern, soft fade at cell borders
    for name, (x0, y0, x1, y1) in CELLS.items():
        c = img[y0:y1, x0:x1]
        h, w = c.shape[:2]
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        vfade = name not in ('ivy',)
        if name in ('grass', 'fern'):
            k = 0.72 if name == 'grass' else 0.85
            c[..., :3] *= (k + (1 - k) * (1 - yy / h))[..., None]
        d = np.minimum(xx, w - 1 - xx)
        if vfade:
            d = np.minimum(d, np.minimum(yy, h - 1 - yy))
        c[..., 3] *= np.clip((d - 1) / 6, 0, 1)

    img = np.nan_to_num(img)
    bimg = bpy.data.images.new('tmp_atlas', AW, AH, alpha=True)
    bimg.pixels.foreach_set(np.ascontiguousarray(img[::-1]).ravel())
    bimg.filepath_raw = path
    bimg.file_format = 'PNG'
    bimg.save()
    bpy.data.images.remove(bimg)
    im = bpy.data.images.load(path)
    im.name = 'T_Foliage_Atlas'
    return im


def cuv(cell):
    x0, y0, x1, y1 = CELLS[cell]
    return ((x0 + 1) / AW, 1 - (y1 - 1) / AH, (x1 - 1) / AW, 1 - (y0 + 1) / AH)


def aspect(cell):
    x0, y0, x1, y1 = CELLS[cell]
    return (x1 - x0) / (y1 - y0)


# ----------------------------------------------------------------------------------------------
# 2. Materials
# ----------------------------------------------------------------------------------------------
def lin(c):
    return tuple(x ** 2.2 for x in c)


def S(socks, key):
    for s in socks:
        if s.identifier == key:
            return s
    return socks[key]


def new_mat(name):
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    try:
        bsdf.inputs['Specular IOR Level'].default_value = 0.25
    except Exception:
        pass
    return m, nt, bsdf


def simple_mat(name, color=(0.5, 0.5, 0.5), image=None, rough=0.85, alpha_clip=False):
    m, nt, bsdf = new_mat(name)
    bsdf.inputs['Base Color'].default_value = (*lin(color), 1)
    bsdf.inputs['Roughness'].default_value = rough
    m.diffuse_color = (*color, 1)
    m.use_backface_culling = not alpha_clip
    if image:
        tex = nt.nodes.new('ShaderNodeTexImage')
        tex.image = image
        nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        if alpha_clip:
            mr = nt.nodes.new('ShaderNodeMath'); mr.operation = 'ROUND'
            nt.links.new(tex.outputs['Alpha'], mr.inputs[0])
            nt.links.new(mr.outputs['Value'], bsdf.inputs['Alpha'])
            m.use_backface_culling = False
            for attr, val in (('surface_render_method', 'DITHERED'), ('blend_method', 'CLIP'),
                              ('use_transparent_shadow', True)):
                try:
                    setattr(m, attr, val)
                except Exception:
                    pass
    return m


def L(nt, a, b):
    nt.links.new(a, b)


def noise_fac(nt, vec, scale, detail, rough):
    n = nt.nodes.new('ShaderNodeTexNoise')
    L(nt, vec, n.inputs['Vector'])
    n.inputs['Scale'].default_value = scale
    n.inputs['Detail'].default_value = detail
    n.inputs['Roughness'].default_value = rough
    return n.outputs['Fac']


def maprange(nt, val, a, b, c, d):
    m = nt.nodes.new('ShaderNodeMapRange')
    L(nt, val, S(m.inputs, 'Value'))
    S(m.inputs, 'From Min').default_value = a
    S(m.inputs, 'From Max').default_value = b
    S(m.inputs, 'To Min').default_value = c
    S(m.inputs, 'To Max').default_value = d
    return S(m.outputs, 'Result')


def ramp(nt, fac, stops):
    r = nt.nodes.new('ShaderNodeValToRGB')
    L(nt, fac, r.inputs['Fac'])
    el = r.color_ramp.elements
    while len(el) < len(stops):
        el.new(0.5)
    for e, (p, c) in zip(el, stops):
        e.position = p
        e.color = (*lin(c), 1)
    return r.outputs['Color']


def mixc(nt, fac, a, b, blend='MIX'):
    m = nt.nodes.new('ShaderNodeMix')
    m.data_type = 'RGBA'
    m.blend_type = blend
    f = S(m.inputs, 'Factor_Float')
    if isinstance(fac, (int, float)):
        f.default_value = fac
    else:
        L(nt, fac, f)
    for key, v in (('A_Color', a), ('B_Color', b)):
        s = S(m.inputs, key)
        if isinstance(v, tuple):
            s.default_value = (*lin(v), 1)
        else:
            L(nt, v, s)
    return S(m.outputs, 'Result_Color')


def mathn(nt, op, a, b):
    m = nt.nodes.new('ShaderNodeMath'); m.operation = op
    for i, v in enumerate((a, b)):
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        else:
            L(nt, v, m.inputs[i])
    return m.outputs['Value']


def moss_mask(nt, geo, co, amount, lo=0.35):
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    L(nt, geo.outputs['Normal'], sep.inputs['Vector'])
    mz = maprange(nt, sep.outputs['Z'], lo, 0.85, 0, 1)
    nz = maprange(nt, noise_fac(nt, co, 2.2, 5, 0.6), 0.42, 0.60, 0, 1)
    return mathn(nt, 'MULTIPLY', mathn(nt, 'MULTIPLY', mz, nz), amount)


def moss_color(nt, co):
    return ramp(nt, noise_fac(nt, co, 9, 3, 0.6), [(0.3, (0.10, 0.17, 0.04)), (0.7, (0.25, 0.34, 0.07))])


def proc_rock(name, moss):
    m, nt, bsdf = new_mat(name)
    co = nt.nodes.new('ShaderNodeTexCoord').outputs['Object']
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    c = ramp(nt, noise_fac(nt, co, 0.7, 6, 0.55),
             [(0.25, (0.16, 0.15, 0.13)), (0.5, (0.29, 0.27, 0.24)), (0.75, (0.42, 0.40, 0.35))])
    c = mixc(nt, 1.0, c, maprange(nt, noise_fac(nt, co, 5, 4, 0.5), 0.3, 0.7, 0.75, 1.15), 'MULTIPLY')
    vor = nt.nodes.new('ShaderNodeTexVoronoi'); vor.feature = 'DISTANCE_TO_EDGE'
    L(nt, co, vor.inputs['Vector']); vor.inputs['Scale'].default_value = 1.3
    c = mixc(nt, 1.0, c, maprange(nt, vor.outputs['Distance'], 0.0, 0.03, 0.7, 1.0), 'MULTIPLY')
    c = mixc(nt, 1.0, c, maprange(nt, geo.outputs['Pointiness'], 0.42, 0.58, 0.65, 1.15), 'MULTIPLY')
    c = mixc(nt, moss_mask(nt, geo, co, moss), c, moss_color(nt, co))
    L(nt, c, bsdf.inputs['Base Color'])
    hgt = mathn(nt, 'ADD', noise_fac(nt, co, 4, 6, 0.6), maprange(nt, vor.outputs['Distance'], 0.0, 0.03, 0.0, 0.6))
    add_bump(nt, bsdf, hgt, 0.6, 0.05)
    return m


def add_bump(nt, bsdf, hgt, strength, dist):
    b = nt.nodes.new('ShaderNodeBump')
    b.inputs['Strength'].default_value = strength
    b.inputs['Distance'].default_value = dist
    L(nt, hgt, b.inputs['Height'])
    L(nt, b.outputs['Normal'], bsdf.inputs['Normal'])


def proc_bark(name, axis, zc, moss):
    m, nt, bsdf = new_mat(name)
    co = nt.nodes.new('ShaderNodeTexCoord').outputs['Object']
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    mp = nt.nodes.new('ShaderNodeMapping')
    L(nt, co, mp.inputs['Vector'])
    mp.inputs['Scale'].default_value = (0.3, 4, 4) if axis == 'X' else (4, 4, 0.3)
    mv = mp.outputs['Vector']
    c = ramp(nt, noise_fac(nt, mv, 2.5, 8, 0.62),
             [(0.35, (0.09, 0.06, 0.035)), (0.5, (0.22, 0.155, 0.09)), (0.68, (0.36, 0.29, 0.20))])
    vor = nt.nodes.new('ShaderNodeTexVoronoi'); vor.feature = 'DISTANCE_TO_EDGE'
    L(nt, mv, vor.inputs['Vector']); vor.inputs['Scale'].default_value = 1.5
    c = mixc(nt, 1.0, c, maprange(nt, vor.outputs['Distance'], 0.0, 0.08, 0.6, 1.0), 'MULTIPLY')
    c = mixc(nt, 1.0, c, maprange(nt, geo.outputs['Pointiness'], 0.44, 0.56, 0.75, 1.1), 'MULTIPLY')
    c = mixc(nt, moss_mask(nt, geo, co, moss, 0.25), c, moss_color(nt, co))
    # end-grain rings
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); L(nt, co, sep.inputs['Vector'])
    comb = nt.nodes.new('ShaderNodeCombineXYZ')
    if axis == 'X':
        L(nt, sep.outputs['Y'], comb.inputs['Y'])
        L(nt, mathn(nt, 'SUBTRACT', sep.outputs['Z'], zc), comb.inputs['Z'])
    else:
        L(nt, sep.outputs['X'], comb.inputs['X'])
        L(nt, sep.outputs['Y'], comb.inputs['Y'])
    wave = nt.nodes.new('ShaderNodeTexWave'); wave.wave_type = 'RINGS'
    L(nt, comb.outputs['Vector'], wave.inputs['Vector'])
    wave.inputs['Scale'].default_value = 25
    wave.inputs['Distortion'].default_value = 4
    wave.inputs['Detail'].default_value = 2
    rings = ramp(nt, wave.outputs['Fac'], [(0.0, (0.40, 0.28, 0.15)), (1.0, (0.63, 0.50, 0.32))])
    rot = ramp(nt, noise_fac(nt, co, 6, 4, 0.6), [(0.3, (0.08, 0.05, 0.03)), (0.7, (0.22, 0.14, 0.08))])
    attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'Mask'
    sc = nt.nodes.new('ShaderNodeSeparateColor'); L(nt, attr.outputs['Color'], sc.inputs['Color'])
    c = mixc(nt, sc.outputs['Red'], c, rings)
    c = mixc(nt, sc.outputs['Green'], c, rot)
    L(nt, c, bsdf.inputs['Base Color'])
    hgt = mathn(nt, 'ADD', noise_fac(nt, mv, 2.5, 8, 0.62), maprange(nt, vor.outputs['Distance'], 0.0, 0.08, 0.0, 0.8))
    add_bump(nt, bsdf, hgt, 0.8, 0.04)
    return m


# ----------------------------------------------------------------------------------------------
# 3. Mesh builder
# ----------------------------------------------------------------------------------------------
UP = Vector((0, 0, 1))


class MB:
    def __init__(s):
        s.v, s.n, s.c, s.f, s.uv, s.m = [], [], [], [], [], []

    def vert(s, co, n=None, c=(0, 0, 0, 1)):
        s.v.append(Vector(co)); s.n.append(n); s.c.append(c)
        return len(s.v) - 1

    def face(s, idx, uvs, mat=0):
        s.f.append(list(idx)); s.uv.append(list(uvs)); s.m.append(mat)

    def card(s, base, right, up, h, cell, mat=0, nfn=None, flip=False, center=False):
        w = h * aspect(cell)
        if center:
            base = base - up * h / 2
        cs = [base - right * w / 2, base + right * w / 2, base + right * w / 2 + up * h, base - right * w / 2 + up * h]
        u0, v0, u1, v1 = cuv(cell)
        if flip:
            u0, u1 = u1, u0
        fn = right.cross(up).normalized()
        ids = [s.vert(p, nfn(p) if nfn else fn) for p in cs]
        s.face(ids, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], mat)

    def strip(s, pts, sides, width, cell, mat=0, nfn=None, top_first=False, flip=False):
        u0, v0, u1, v1 = cuv(cell)
        if flip:
            u0, u1 = u1, u0
        n = len(pts)
        rows = []
        for k, p in enumerate(pts):
            sd = sides[k] if isinstance(sides, list) else sides
            f = k / (n - 1)
            v = v1 - (v1 - v0) * f if top_first else v0 + (v1 - v0) * f
            tg = pts[min(k + 1, n - 1)] - pts[max(k - 1, 0)]
            nrm = nfn(p) if nfn else sd.cross(tg).normalized()
            rows.append((s.vert(p - sd * width / 2, nrm), s.vert(p + sd * width / 2, nrm), v))
        for k in range(n - 1):
            a0, b0, va = rows[k]; a1, b1, vb = rows[k + 1]
            s.face([a0, b0, b1, a1], [(u0, va), (u1, va), (u1, vb), (u0, vb)], mat)

    def tube(s, path, radii, sides, mat=0, cap0=False, cap1=False, rfn=None, afn=None,
             mask=(0, 0, 0, 1), cap_mask=(1, 0, 0, 1), inward=False):
        path = [Vector(p) for p in path]
        n = len(path)
        T = [(path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized() for i in range(n)]
        ref = Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
        N = (ref - T[0] * ref.dot(T[0])).normalized()
        rings, frames, vl = [], [], 0.0
        for i in range(n):
            if i:
                N = (N - T[i] * N.dot(T[i])).normalized()
                vl += (path[i] - path[i - 1]).length
            B = T[i].cross(N)
            ring = []
            for j in range(sides):
                an = 2 * math.pi * j / sides
                d = N * math.cos(an) + B * math.sin(an)
                r = radii[i] * (rfn(i, j, path[i], d) if rfn else 1.0)
                p = path[i] + d * r
                if afn:
                    p = p + T[i] * afn(i, j)
                ring.append(s.vert(p, -d if inward else d, mask))
            rings.append((ring, vl)); frames.append(T[i])
        for i in range(n - 1):
            ra, va = rings[i]; rb, vb = rings[i + 1]
            for j in range(sides):
                j2 = (j + 1) % sides
                q = [ra[j], ra[j2], rb[j2], rb[j]]
                uv = [(j / sides, va), ((j + 1) / sides, va), ((j + 1) / sides, vb), (j / sides, vb)]
                if inward:
                    q.reverse(); uv.reverse()
                s.face(q, uv, mat)
        caps = {}
        for i, end, on in ((0, False, cap0), (n - 1, True, cap1)):
            if not on:
                continue
            ring = rings[i][0]
            nn = frames[i] if end else -frames[i]
            cpos = sum((s.v[k] for k in ring), Vector()) / sides
            cv = s.vert(cpos, nn, cap_mask)
            rv = [s.vert(s.v[k].copy(), nn, cap_mask) for k in ring]
            for j in range(sides):
                j2 = (j + 1) % sides
                a0, a1 = 2 * math.pi * j / sides, 2 * math.pi * (j + 1) / sides
                uc = (0.5, 0.5)
                ua = (0.5 + 0.5 * math.cos(a0), 0.5 + 0.5 * math.sin(a0))
                ub = (0.5 + 0.5 * math.cos(a1), 0.5 + 0.5 * math.sin(a1))
                if end:
                    s.face([cv, rv[j], rv[j2]], [uc, ua, ub], mat)
                else:
                    s.face([cv, rv[j2], rv[j]], [uc, ub, ua], mat)
            caps[i] = cv
        return [r[0] for r in rings], frames, caps

    def lathe(s, prof, sides, mats, M):
        rings = []
        for r, z in prof:
            if r < 1e-5:
                rings.append([s.vert(M @ Vector((0, 0, z)))])
            else:
                rings.append([s.vert(M @ Vector((r * math.cos(2 * math.pi * j / sides),
                                                 r * math.sin(2 * math.pi * j / sides), z))) for j in range(sides)])
        for k in range(len(prof) - 1):
            A, Bv, mat = rings[k], rings[k + 1], mats[k]
            for j in range(sides):
                j2 = (j + 1) % sides
                u0, u1, va, vb = j / sides, (j + 1) / sides, k / len(prof), (k + 1) / len(prof)
                if len(A) == 1:
                    s.face([A[0], Bv[j2], Bv[j]], [(u0, va), (u1, vb), (u0, vb)], mat)
                elif len(Bv) == 1:
                    s.face([A[j], A[j2], Bv[0]], [(u0, va), (u1, va), (u0, vb)], mat)
                else:
                    s.face([A[j], A[j2], Bv[j2], Bv[j]], [(u0, va), (u1, va), (u1, vb), (u0, vb)], mat)

    def build(s, name, mats, custom_normals=False, mask=False):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in s.v], [], s.f)
        uvl = me.uv_layers.new(name='UVMap')
        uvl.data.foreach_set('uv', [c for f in s.uv for uv in f for c in uv])
        me.polygons.foreach_set('material_index', s.m)
        for m in mats:
            me.materials.append(m)
        try:
            me.shade_smooth()
        except Exception:
            me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
        if mask:
            at = me.color_attributes.new('Mask', 'FLOAT_COLOR', 'POINT')
            at.data.foreach_set('color', [x for c in s.c for x in c])
        me.update()
        if custom_normals:
            try:
                me.normals_split_custom_set_from_vertices(
                    [tuple((n if n is not None else UP).normalized()) for n in s.n])
            except Exception as e:
                print('custom normals failed', name, e)
        obj = bpy.data.objects.new(name, me)
        COL.objects.link(obj)
        return obj


def rand_unit(rng):
    z = rng.uniform(-1, 1); a = rng.uniform(0, 2 * math.pi); r = math.sqrt(1 - z * z)
    return Vector((r * math.cos(a), r * math.sin(a), z))


# ----------------------------------------------------------------------------------------------
# 4. Assets
# ----------------------------------------------------------------------------------------------
ATLAS = paint_atlas(os.path.join(TEX, 'T_Foliage_Atlas.png'))
M_FOL = simple_mat('M_Foliage', (0.3, 0.45, 0.12), ATLAS, 0.75, alpha_clip=True)
M_TWIG = simple_mat('M_Twig', OAK_BARK[1], rough=0.9)
M_CAP = simple_mat('M_Mushroom_Cap', (0.62, 0.33, 0.18), rough=0.6)
M_GILL = simple_mat('M_Mushroom_Stem', (0.80, 0.76, 0.62), rough=0.85)


def shade_flat(me):
    try:
        me.shade_flat()
    except Exception:
        me.polygons.foreach_set('use_smooth', [False] * len(me.polygons))


def flat_face_mat(name):
    """Bake source: each face's colour comes from the face-domain attribute 'FaceCol' (Oak Grove style)."""
    m, nt, bsdf = new_mat(name)
    at = nt.nodes.new('ShaderNodeAttribute')
    at.attribute_type = 'GEOMETRY'
    at.attribute_name = 'FaceCol'
    L(nt, at.outputs['Color'], bsdf.inputs['Base Color'])
    return m


def set_face_colors(me, fn):
    cols = []
    for p in me.polygons:
        cols += (*lin(fn(p)), 1.0)
    at = me.color_attributes.new('FaceCol', 'FLOAT_COLOR', 'FACE')
    at.data.foreach_set('color', cols)
M_STONE = simple_mat('M_Preview_Stone', (0.36, 0.34, 0.31), rough=0.9)
M_GROUND = simple_mat('M_Preview_Ground', (0.17, 0.19, 0.09), rough=1.0)

ASSETS = []  # (obj, category)


def register(obj, cat, desc):
    obj['category'] = cat
    try:
        obj.asset_mark()
        obj.asset_data.tags.new(cat)
        obj.asset_data.description = desc
    except Exception as e:
        print('asset mark failed', e)
    ASSETS.append((obj, cat))
    print('built', obj.name, len(obj.data.polygons), 'faces')


def bush(name, seed, rx, h, n, cells, size, stems=5, flowers=0, desc=''):
    rng = random.Random(seed); mb = MB()
    c = Vector((0, 0, h * 0.5)); ax = Vector((rx, rx, h * 0.5))

    def nrm(p):
        d = Vector((p.x / ax.x, p.y / ax.y, (p.z - c.z) / ax.z))
        if d.length < 1e-4:
            d = UP.copy()
        return (d.normalized() + Vector((0, 0, 0.35))).normalized()

    for _ in range(stems):
        a = rng.uniform(0, 2 * math.pi); f = rng.uniform(0.3, 0.9)
        tip = Vector((math.cos(a) * rx * 0.5 * f, math.sin(a) * rx * 0.5 * f, h * rng.uniform(0.3, 0.55)))
        mid = tip * 0.5 + Vector((rng.uniform(-.05, .05), rng.uniform(-.05, .05), 0))
        mb.tube([Vector((0, 0, -0.03)), mid, tip], [0.035, 0.024, 0.01], 5, 1)
    for _ in range(n):
        d = rand_unit(rng); d.z = abs(d.z) * 1.2 - 0.45
        d.normalize()
        f = math.sqrt(rng.uniform(0.3, 1.0))
        p = c + Vector((d.x * ax.x * f, d.y * ax.y * f, d.z * ax.z * f))
        p.z = max(p.z, 0.08 * h)
        cell = rng.choice(cells)
        sz = rng.uniform(*size)
        up = (d * 0.7 + Vector((0, 0, 0.8)) + rand_unit(rng) * 0.4).normalized()
        right = up.cross(rand_unit(rng))
        if right.length < 1e-3:
            right = up.cross(Vector((1, 0, 0)))
        right.normalize()
        base = p - up * sz * 0.35
        for r in (right, up.cross(right).normalized()):
            mb.card(base, r, up, sz, cell, 0, nrm, rng.random() < 0.5)
    for _ in range(flowers):
        d = rand_unit(rng); d.z = abs(d.z); d.normalize()
        f = rng.uniform(0.92, 1.05)
        p = c + Vector((d.x * ax.x * f, d.y * ax.y * f, d.z * ax.z * f))
        right = UP.cross(d)
        if right.length < 1e-3:
            right = Vector((1, 0, 0))
        right.normalize()
        up = d.cross(right).normalized()
        mb.card(p, right, up, rng.uniform(0.42, 0.58), 'flowers', 0, nrm, center=True)
    register(mb.build(name, [M_FOL, M_TWIG], custom_normals=True), 'Bushes', desc)


def bramble(name, seed, rx, h, ncards, nstems, desc):
    rng = random.Random(seed); mb = MB()
    paths = []
    for _ in range(nstems):
        a = rng.uniform(0, 2 * math.pi); ro = rx * rng.uniform(0.6, 1.15); hb = h * rng.uniform(0.6, 1.1)
        pts = []
        for i in range(6):
            t = i / 5
            pts.append(Vector((math.cos(a) * ro * t, math.sin(a) * ro * t, 0.02 + hb * math.sin(math.pi * t * 0.8))))
            a += rng.uniform(-0.12, 0.12)
        paths.append(pts)
        mb.tube(pts, [0.03, 0.022, 0.016, 0.012, 0.008, 0.004], 5, 1)
    def nrm(p):
        return (Vector((p.x, p.y, 0)) * (1 / max(rx, 0.01)) + Vector((0, 0, 0.8))).normalized()
    for _ in range(ncards):
        pts = rng.choice(paths)
        t = rng.uniform(0.3, 1.0) * 5
        i = min(int(t), 4)
        p = pts[i].lerp(pts[i + 1], t - i)
        cell = rng.choice(['debris', 'debris', 'small'])
        sz = rng.uniform(0.4, 0.65)
        up = (Vector((p.x, p.y, 0)).normalized() * 0.5 + Vector((0, 0, 0.9)) + rand_unit(rng) * 0.4).normalized()
        right = up.cross(rand_unit(rng)).normalized()
        for r in (right, up.cross(right).normalized()):
            mb.card(p - up * sz * 0.4, r, up, sz, cell, 0, nrm, rng.random() < 0.5)
    register(mb.build(name, [M_FOL, M_TWIG], custom_normals=True), 'Bushes', desc)


def fern(name, seed, n, Lr, desc):
    rng = random.Random(seed); mb = MB()
    for k in range(n):
        a = 2 * math.pi * k / n + rng.uniform(-0.25, 0.25)
        hor = Vector((math.cos(a), math.sin(a), 0)); sd = Vector((-math.sin(a), math.cos(a), 0))
        Lk = rng.uniform(*Lr); el = rng.uniform(0.9, 1.4); tw = rng.uniform(-0.25, 0.25)
        side = (sd * math.cos(tw) + UP * math.sin(tw)).normalized()
        pts = [hor * (Lk * t * 0.85) + UP * (0.02 + Lk * 0.6 * (el * t - 0.8 * t * t)) for t in np.linspace(0, 1, 6)]
        mb.strip(pts, side, Lk * 0.5, 'fern', 0, lambda p, h=hor: (h * 0.4 + UP).normalized(), flip=rng.random() < 0.5)
    register(mb.build(name, [M_FOL], custom_normals=True), 'Plants', desc)


def crossed(mb, rng, pos, cell, h, count=3, tilt=0.12):
    a0 = rng.uniform(0, math.pi)
    for k in range(count):
        a = a0 + k * math.pi / count
        right = Vector((math.cos(a), math.sin(a), 0))
        out = Vector((-math.sin(a), math.cos(a), 0)) * rng.uniform(-tilt, tilt)
        up = (UP + out).normalized()
        mb.card(pos + Vector((0, 0, -0.02)), right, up, h, cell, 0,
                lambda p, c=pos: (Vector((p.x - c.x, p.y - c.y, 0)) * 0.8 + UP).normalized(), rng.random() < 0.5)


def grass(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    # sized to read at gameplay zoom (8 px ASCII cells are ~12 cm on the ground)
    for pos, h in ((Vector((0, 0, 0)), 1.25), (Vector((0.32, 0.16, 0)), 0.95), (Vector((-0.22, 0.28, 0)), 0.85),
                   (Vector((0.1, -0.3, 0)), 0.8), (Vector((-0.3, -0.12, 0)), 0.75)):
        crossed(mb, rng, pos, 'grass', h * rng.uniform(0.9, 1.1))
    register(mb.build(name, [M_FOL], custom_normals=True), 'Plants', desc)


def flower_patch(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    for _ in range(9):
        a = rng.uniform(0, 2 * math.pi); r = rng.uniform(0, 0.65)
        pos = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        crossed(mb, rng, pos, 'flowers', rng.uniform(0.42, 0.56), 2)
        crossed(mb, rng, pos + Vector((0.07, 0, 0)), 'grass', rng.uniform(0.5, 0.7), 2)
    register(mb.build(name, [M_FOL], custom_normals=True), 'Plants', desc)


def mushrooms(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    for k in range(6):
        big = k == 0
        a = rng.uniform(0, 2 * math.pi); r = 0 if big else rng.uniform(0.13, 0.42)
        h = rng.uniform(0.26, 0.32) if big else rng.uniform(0.09, 0.2)
        rs = h * rng.uniform(0.15, 0.2); rc = h * rng.uniform(0.5, 0.75); ch = rc * rng.uniform(0.45, 0.7)
        prof = [(0, -0.01), (rs, -0.01), (rs * 0.95, h * 0.4), (rs * 0.85, h * 0.9), (rc * 0.92, h * 0.95),
                (rc, h + ch * 0.15)]
        for deg in (25, 50, 70, 90):
            t = math.radians(deg)
            prof.append((rc * math.cos(t), h + ch * (0.15 + 0.85 * math.sin(t))))
        mats = [1, 1, 1, 1, 0, 0, 0, 0, 0]
        M = (Matrix.Translation((math.cos(a) * r, math.sin(a) * r, 0)) @
             Matrix.Rotation(rng.uniform(0, 0.3), 4, Vector((math.cos(a + 1.5), math.sin(a + 1.5), 0))))
        mb.lathe(prof, 8, mats, M)
    obj = mb.build(name, [M_CAP, M_GILL])
    shade_flat(obj.data)
    register(obj, 'Plants', desc)


def debris(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    a = rng.uniform(0, math.pi)
    right = Vector((math.cos(a), math.sin(a), 0)); up = Vector((-math.sin(a), math.cos(a), 0))
    mb.card(Vector((0, 0, 0.012)), right, up, 1.7, 'debris', 0, lambda p: UP, center=True)
    for _ in range(3):
        b = rng.uniform(0, 2 * math.pi); d = Vector((math.cos(b), math.sin(b), 0)); c = rand_unit(rng) * 0.35
        c.z = 0.03
        mb.tube([c - d * 0.45, c, c + d * 0.45], [0.028, 0.022, 0.014], 5, 1, cap0=True, cap1=True)
    register(mb.build(name, [M_FOL, M_TWIG], custom_normals=True), 'Plants', desc)


def resample(pts, step):
    out = [pts[0].copy()]
    carry = 0.0
    for a, b in zip(pts, pts[1:]):
        seg = (b - a).length
        d = step - carry
        while d <= seg:
            out.append(a.lerp(b, d / seg))
            d += step
        carry = seg - (d - step)
    if (out[-1] - pts[-1]).length > step * 0.3:
        out.append(pts[-1].copy())
    return out


def tiled_strip(mb, pts, side_fn, width, nfn, top_first=False, flip=False, cell='ivy'):
    """Lay the ivy texture along a long path, repeating it every natural texture length."""
    natural = width / aspect(cell)
    pts = resample(pts, natural / 4)
    for k in range(0, len(pts) - 1, 4):
        ch = pts[k:k + 5]
        if len(ch) < 2:
            break
        sides = []
        for i in range(len(ch)):
            tg = ch[min(i + 1, len(ch) - 1)] - ch[max(i - 1, 0)]
            sides.append(side_fn(ch[i], tg.normalized()))
        mb.strip(ch, sides, width, cell, 0, nfn, top_first=top_first, flip=flip)


def vine_hanging(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    nfn = lambda p: Vector((0, -1, 0.3)).normalized()
    for k in range(9):
        x = -0.85 + 1.7 * k / 8 + rng.uniform(-0.06, 0.06)
        Lh = rng.uniform(0.9, 2.4)
        pts = [Vector((x, 0.32, 0.03)), Vector((x, 0.14, 0.035)), Vector((x, -0.03, -0.04))]
        for i in range(1, 7):
            t = i / 6
            pts.append(Vector((x + rng.uniform(-0.04, 0.04), -0.04 - 0.12 * math.sin(math.pi * t) * rng.uniform(0.5, 1),
                               -0.04 - Lh * t)))
        tiled_strip(mb, pts, lambda p, tg: Vector((1, 0, 0)), 0.4, nfn, top_first=True, flip=rng.random() < 0.5)
        sh = rng.uniform(0.15, 0.3)
        p2 = [p + Vector((0.09, -0.035, -sh)) for p in pts[2:]]
        tiled_strip(mb, p2, lambda p, tg: Vector((1, 0, 0)), 0.36, nfn, top_first=True, flip=True)
    register(mb.build(name, [M_FOL], custom_normals=True), 'Vines', desc)


def vine_wall(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    nfn = lambda p: Vector((0, -1, 0.15)).normalized()
    wall_side = lambda p, tg: Vector((0, -1, 0)).cross(tg).normalized()
    stems = []
    for _ in range(10):
        x = rng.uniform(-1.0, 1.0); hg = rng.uniform(1.0, 2.6); pts = []
        for i in range(8):
            x += rng.uniform(-0.08, 0.08)
            pts.append(Vector((x, -0.03 - 0.04 * rng.random(), hg * i / 7)))
        stems.append(pts)
        tiled_strip(mb, pts, wall_side, 0.42, nfn, flip=rng.random() < 0.5)
        tiled_strip(mb, [p + Vector((0.08, -0.03, 0.2)) for p in pts[:-1]], wall_side, 0.36, nfn, flip=True)
    for _ in range(8):  # side branches
        src = rng.choice(stems)
        p0 = src[rng.randint(1, len(src) - 2)].copy(); p0.y -= 0.02
        a = math.radians(rng.choice((-1, 1)) * rng.uniform(25, 60))
        d = Vector((math.sin(a), 0, math.cos(a)))
        ln = rng.uniform(0.5, 0.9)
        tiled_strip(mb, [p0, p0 + d * ln * 0.5 + Vector((0, -0.02, 0)), p0 + d * ln], wall_side, 0.36, nfn)
    register(mb.build(name, [M_FOL], custom_normals=True), 'Vines', desc)


def vine_ground(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    for _ in range(6):
        hd = rng.uniform(0, 2 * math.pi); p = Vector((0, 0, 0.03)); pts = [p.copy()]
        for _ in range(6):
            hd += rng.uniform(-0.4, 0.4)
            p = p + Vector((math.cos(hd), math.sin(hd), 0)) * 0.25
            p.z = 0.02 + 0.05 * rng.random()
            pts.append(p.copy())
        tiled_strip(mb, pts, lambda q, tg: Vector((-tg.y, tg.x, 0)).normalized(), 0.4, lambda q: UP)
    register(mb.build(name, [M_FOL], custom_normals=True), 'Vines', desc)


def smart_uv(obj):
    for o in scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name='UVMap')
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake(obj, size, normal=False):
    smart_uv(obj)
    img = bpy.data.images.new('bake_' + obj.name, size, size, alpha=False)
    pm = obj.data.materials[0]
    nt = pm.node_tree
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
    for nd in nt.nodes:
        nd.select = False
    tn.select = True
    nt.nodes.active = tn
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 4
    scene.cycles.device = 'CPU'
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8, use_clear=True)
    path = os.path.join(TEX, 'T_' + obj.name[3:] + '.png')
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)
    if not normal:
        # Oak Grove style: flat-shaded facets carry the form, so no normal map.
        m = simple_mat('M_' + obj.name[3:], image=bpy.data.images.load(path), rough=0.9)
        fc = obj.data.color_attributes.get('FaceCol')
        if fc is not None:
            obj.data.color_attributes.remove(fc)   # baked into the texture; keep it out of the FBX
        obj.data.materials.clear()
        obj.data.materials.append(m)
        bpy.data.materials.remove(pm)
        return
    # tangent-space normal map (OpenGL / Y+ convention, same as Unity)
    nimg = bpy.data.images.new('bakeN_' + obj.name, size, size, alpha=False)
    nimg.colorspace_settings.name = 'Non-Color'
    tn.image = nimg
    bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=8, use_clear=True)
    npath = os.path.join(TEX, 'T_' + obj.name[3:] + '_Normal.png')
    nimg.filepath_raw = npath; nimg.file_format = 'PNG'; nimg.save()
    bpy.data.images.remove(nimg)
    im = bpy.data.images.load(path)
    nim = bpy.data.images.load(npath)
    nim.colorspace_settings.name = 'Non-Color'
    m = simple_mat('M_' + obj.name[3:], image=im, rough=0.92)
    mnt = m.node_tree
    bsdf = next(n for n in mnt.nodes if n.type == 'BSDF_PRINCIPLED')
    ntex = mnt.nodes.new('ShaderNodeTexImage'); ntex.image = nim
    nmap = mnt.nodes.new('ShaderNodeNormalMap')
    mnt.links.new(ntex.outputs['Color'], nmap.inputs['Color'])
    mnt.links.new(nmap.outputs['Normal'], bsdf.inputs['Normal'])
    obj.data.materials.clear()
    obj.data.materials.append(m)
    bpy.data.materials.remove(pm)


def log_asset(name, seed, Lg, Rd, hollow=False, broken=True, stubs=3, moss=0.6, tex=1024, desc=''):
    rng = random.Random(seed); mb = MB()
    off = Vector((rng.uniform(0, 99), rng.uniform(0, 99), rng.uniform(0, 99)))
    n, sides, zc = 9, 10, Rd * 0.82   # low-poly, like the oak trunks
    ts = [i / (n - 1) for i in range(n)]
    path = [Vector((-Lg / 2 + Lg * t, 0.04 * Lg * math.sin(math.pi * t) * 0.3, zc)) for t in ts]
    radii = [Rd * (1 - 0.15 * t) * (1 + 0.3 * max(0.0, 0.12 - t) / 0.12) for t in ts]
    jag = [rng.uniform(-0.03, 0.16) for _ in range(sides)]

    def rfn(i, j, c, d, amp=1.0):
        p = c + d * Rd
        return 1 + amp * (0.07 * noise.noise(p * 1.2 + off) +
                          0.035 * noise.noise(Vector((p.x * 0.4, d.y * 5, d.z * 5)) + off))

    def afn(i, j):
        return jag[j] if (broken and i == n - 1) else 0.0

    rings, fr, caps = mb.tube(path, radii, sides, 0, cap0=not hollow, cap1=not hollow, rfn=rfn, afn=afn)
    if broken and not hollow:
        mb.v[caps[n - 1]] -= fr[-1] * 0.06
    if hollow:
        ir = [r * 0.7 for r in radii]
        irings, _, _ = mb.tube(path, ir, sides, 0, rfn=lambda i, j, c, d: rfn(i, j, c, d, 0.5), afn=afn,
                               mask=(0, 1, 0, 1), inward=True)
        for i, end in ((0, False), (n - 1, True)):
            T = fr[i] if end else -fr[i]
            O = [mb.vert(mb.v[k].copy(), T, (1, 0, 0, 1)) for k in rings[i]]
            I = [mb.vert(mb.v[k].copy(), T, (1, 0, 0, 1)) for k in irings[i]]
            for j in range(sides):
                j2 = (j + 1) % sides
                q = [O[j], O[j2], I[j2], I[j]]
                if not end:
                    q.reverse()
                mb.face(q, [(0, 0), (1, 0), (1, 1), (0, 1)], 0)
    for _ in range(stubs):
        t = rng.uniform(0.2, 0.85); i = int(t * (n - 1)); b = rng.uniform(0.4, 2.7)
        d = Vector((0, math.cos(b), math.sin(b)))
        st = path[i] + d * radii[i] * 0.85
        dv = (d + Vector((rng.uniform(-0.3, 0.3), 0, 0))).normalized(); lb = rng.uniform(0.15, 0.4)
        mb.tube([st, st + dv * lb * 0.5, st + dv * lb], [0.075, 0.06, 0.045], 5, 0, cap1=True, cap_mask=(0, 0, 0, 1))
    obj = mb.build(name, [flat_face_mat('proc_' + name)], mask=True)
    wood_faces(obj, seed, moss)
    bake(obj, tex)
    register(obj, 'Logs', desc)


def wood_faces(obj, seed, moss, axis='X'):
    """Oak-trunk style: Oak | Bark tones in long bands around the wood (like the oak trunks' facets),
    two-tone faceted cut ends, darkest bark inside hollows."""
    me = obj.data
    shade_flat(me)
    rng = random.Random(seed)
    mask = me.color_attributes['Mask'].data
    bands = 12
    band_col = [rng.choices(OAK_BARK, weights=[2, 4, 4, 1, 2])[0] for _ in range(bands)]

    def fn(p):
        m = mask[p.vertices[0]].color
        f = rng.uniform(0.97, 1.03)
        if m[0] > 0.5:
            c = (0.68, 0.56, 0.40) if p.index % 2 else (0.61, 0.49, 0.34)
        elif m[1] > 0.5:
            c = OAK_BARK[3]
        else:
            n = p.normal
            ang = math.atan2(n.z, n.y) if axis == 'X' else math.atan2(n.y, n.x)
            c = band_col[int((ang + math.pi) / (2 * math.pi) * bands) % bands]
        return tuple(x * f for x in c)

    set_face_colors(me, fn)


def stump(name, seed, desc):
    rng = random.Random(seed); mb = MB()
    zs = [-0.12, 0.05, 0.18, 0.32, 0.45, 0.55]
    radii = [0.5, 0.4, 0.35, 0.33, 0.32, 0.31]
    sides = 9; th0 = rng.uniform(0, 2 * math.pi)
    off = Vector((rng.uniform(0, 99), 0, 0))

    def rfn(i, j, c, d):
        p = c + d * 0.3
        return 1 + 0.06 * noise.noise(p * 2 + off) + 0.04 * noise.noise(Vector((d.x * 6, d.y * 6, p.z * 0.5)) + off)

    def afn(i, j):
        if i != len(zs) - 1:
            return 0.0
        a = 2 * math.pi * j / sides
        return 0.03 * noise.noise(Vector((math.cos(a) * 3, math.sin(a) * 3, 0)) + off) + \
            0.2 * max(0.0, math.cos(a - th0)) ** 6

    _, fr, caps = mb.tube([Vector((0, 0, z)) for z in zs], radii, sides, 0, cap1=True, rfn=rfn, afn=afn)
    mb.v[caps[len(zs) - 1]] -= Vector((0, 0, 0.03))
    for k in range(6):
        a = 2 * math.pi * k / 6 + rng.uniform(-0.3, 0.3); ln = rng.uniform(0.7, 1.0)
        dv = Vector((math.cos(a), math.sin(a), 0))
        mb.tube([dv * 0.15 + Vector((0, 0, 0.28)), dv * 0.42 * ln + Vector((0, 0, 0.06)), dv * 0.8 * ln + Vector((0, 0, -0.07))],
                [0.15, 0.09, 0.03], 5, 0, cap1=True, cap_mask=(0, 0, 0, 1))
    obj = mb.build(name, [flat_face_mat('proc_' + name)], mask=True)
    wood_faces(obj, seed, 0.5, axis='Z')
    bake(obj, 512)
    register(obj, 'Logs', desc)


def rock_asset(name, seed, pieces, tex, moss=0.7, desc=''):
    rng = random.Random(seed)
    bm = bmesh.new()
    piece_layer = bm.faces.layers.int.new('piece')
    for pi, (off, sc, sd, npl) in enumerate(pieces):
        before = set(bm.faces)
        res = bmesh.ops.create_icosphere(bm, subdivisions=sd, radius=1.0)
        for f in bm.faces:
            if f not in before:
                f[piece_layer] = pi
        planes = []
        for _ in range(npl):
            nv = rand_unit(rng); nv.z = abs(nv.z) * 1.2 - 0.2; nv.normalize()
            planes.append((nv, rng.uniform(0.55, 0.85)))
        so = Vector((rng.uniform(0, 50), rng.uniform(0, 50), rng.uniform(0, 50)))
        for v in res['verts']:
            p = v.co.copy()
            for nv, dd in planes:
                x = p.dot(nv)
                if x > dd:
                    p -= nv * (x - dd)
            p *= 1 + 0.13 * noise.fractal(p * 1.4 + so, 1.0, 2.0, 4)
            p = Vector((p.x * sc[0], p.y * sc[1], p.z * sc[2]))
            if p.z < 0:
                p.z *= 0.35
            v.co = p + Vector(off)
    zmin = min(v.co.z for v in bm.verts); zmax = max(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= zmin + 0.06 * (zmax - zmin)
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    shade_flat(me)
    # Oak Grove stones: one Grove | Stone colour per rock, small per-face variation, mossier tops.
    piece_col = [rng.choices(OAK_STONE, weights=[2, 4, 2, 4])[0] for _ in pieces]
    pidx = me.attributes['piece'].data

    def fn(p):
        c = piece_col[pidx[p.index].value]
        if p.normal.z > 0.5 and rng.random() < 0.3 * moss:
            c = tuple((a + b) * 0.5 for a, b in zip(c, OAK_STONE[0]))
        f = rng.uniform(0.93, 1.07)
        return tuple(x * f for x in c)

    set_face_colors(me, fn)
    me.materials.append(flat_face_mat('proc_' + name))
    obj = bpy.data.objects.new(name, me)
    COL.objects.link(obj)
    bake(obj, tex)
    register(obj, 'Rocks', desc)


# --- build everything -------------------------------------------------------------------------
bush('SM_Bush_Broadleaf_Round', 11, 0.85, 1.2, 46, ['broad'], (0.55, 0.8), 5, desc='Round broad-leaf shrub, ~1.7m wide')
bush('SM_Bush_SmallLeaf_Tall', 12, 0.55, 1.9, 48, ['small'], (0.5, 0.75), 4, desc='Tall dense small-leaf shrub, ~1.9m')
bush('SM_Bush_Flowering', 13, 0.7, 1.0, 36, ['small', 'small', 'broad'], (0.45, 0.65), 4, flowers=30,
     desc='Lavender flowering shrub')
bramble('SM_Bush_Bramble_Dry', 14, 1.0, 0.8, 26, 9, 'Dry, sparse bramble / dead shrub')

log_asset('SM_Log_Fallen_Long', 21, 4.0, 0.32, broken=True, stubs=3, moss=0.6, desc='4m fallen log, broken end')
log_asset('SM_Log_Hollow', 22, 2.6, 0.42, hollow=True, broken=True, stubs=1, moss=0.8, desc='Hollow mossy log')
log_asset('SM_Log_Short', 23, 1.3, 0.22, broken=False, stubs=1, moss=0.8, tex=512, desc='Short cut log section')
stump('SM_Stump', 24, 'Tree stump with roots')

rock_asset('SM_Rock_Boulder_Large', 31, [((0, 0, 0), (1.4, 1.1, 1.0), 3, 10)], 512, 0.7, 'Large boulder ~2.8m')
rock_asset('SM_Rock_Medium', 32, [((0, 0, 0), (0.7, 0.55, 0.5), 3, 9)], 256, 0.7, 'Medium rock ~1.4m')
rock_asset('SM_Rock_Small', 33, [((0, 0, 0), (0.25, 0.2, 0.18), 2, 7)], 128, 0.5, 'Small rock ~0.5m')
rock_asset('SM_Rock_Slab', 34, [((0, 0, 0), (1.2, 0.8, 0.22), 3, 6)], 256, 0.8, 'Flat rock slab')
rock_asset('SM_Rock_Cluster', 35, [((0, 0, 0), (0.6, 0.5, 0.55), 3, 9), ((0.75, 0.2, 0), (0.38, 0.32, 0.3), 2, 7),
                                   ((-0.55, 0.45, 0), (0.3, 0.28, 0.25), 2, 7)], 512, 0.7, 'Cluster of 3 rocks')
_rng = random.Random(36)
rock_asset('SM_Rock_Pebbles', 36, [((math.cos(a) * r, math.sin(a) * r, 0), (s, s * 0.85, s * 0.6), 1, 3)
                                   for a, r, s in [(_rng.uniform(0, 6.28), _rng.uniform(0, 0.6), _rng.uniform(0.06, 0.15))
                                                   for _ in range(10)]], 256, 0.2, 'Scatter of pebbles')

vine_hanging('SM_Vine_Hanging', 41, 'Ivy draping over a ledge; origin at ledge top-front edge, hangs toward -Y')
vine_wall('SM_Vine_Ivy_Wall', 42, 'Ivy patch for walls; origin bottom-centre, wall plane is XZ, faces -Y')
vine_ground('SM_Vine_Ground_Creeper', 43, 'Ground-creeping ivy')

fern('SM_Fern', 51, 9, (0.6, 0.95), 'Large fern')
grass('SM_Grass_Clump', 52, 'Tall grass clump')
flower_patch('SM_Flower_Patch', 53, 'Lavender / yellow wildflower patch')
mushrooms('SM_Mushroom_Cluster', 54, 'Mushroom cluster')
debris('SM_Ground_Debris', 55, 'Leaf litter / twig decal with sticks')


# ----------------------------------------------------------------------------------------------
# 4b. Wind weights -> vertex color 'Wind'
#     R = bend weight (0 at the anchor, 1 at the tips)   G = per-branch phase (random per island)
#     B = leaf flutter amount (1 on leaf cards, 0 on stems)
# ----------------------------------------------------------------------------------------------
def bake_wind(obj):
    me = obj.data
    name = obj.name
    rng = random.Random(sum(map(ord, name)) + 7)
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    island = [0.0] * len(bm.verts)
    leafv = [0.0] * len(bm.verts)
    seen = set()
    for v in bm.verts:
        if v.index in seen:
            continue
        g = rng.random()
        stack = [v]; seen.add(v.index)
        while stack:
            w = stack.pop(); island[w.index] = g
            for e in w.link_edges:
                o = e.other_vert(w)
                if o.index not in seen:
                    seen.add(o.index); stack.append(o)
    for f in bm.faces:
        if f.material_index == 0:
            for v in f.verts:
                leafv[v.index] = 1.0
    bm.free()
    dims = obj.dimensions
    size = max(dims.x, dims.y, dims.z, 0.1)
    cols = []
    for i, v in enumerate(me.vertices):
        p = v.co
        if name == 'SM_Vine_Hanging':
            r = min(max(-p.z / 2.4, 0.0), 1.0) ** 1.2
        elif name == 'SM_Vine_Ivy_Wall':
            r = 0.12 * min(max(p.z / 2.6, 0.0), 1.0)
        elif name == 'SM_Vine_Ground_Creeper':
            r = 0.04
        elif name == 'SM_Ground_Debris':
            r = 0.0
        else:
            d = math.sqrt(p.x * p.x + p.y * p.y + p.z * p.z) / (0.7 * size)
            r = min(max(d, 0.0), 1.0) ** 1.5
        b = leafv[i] * (0.0 if name == 'SM_Ground_Debris' else 0.5 if 'Ivy_Wall' in name or 'Creeper' in name else 1.0)
        cols += (r, island[i], b, 1.0)
    at = me.color_attributes.new('Wind', 'FLOAT_COLOR', 'POINT')
    at.data.foreach_set('color', cols)
    try:
        me.color_attributes.active_color = at
    except Exception:
        pass


for _obj, _cat in ASSETS:
    if _cat in ('Bushes', 'Vines') or _obj.name in ('SM_Fern', 'SM_Grass_Clump', 'SM_Flower_Patch', 'SM_Ground_Debris'):
        bake_wind(_obj)

# ----------------------------------------------------------------------------------------------
# 5. Export each asset at the origin
# ----------------------------------------------------------------------------------------------
for obj, cat in ASSETS:
    for o in scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    try:
        bpy.ops.export_scene.gltf(filepath=os.path.join(GLB, obj.name + '.glb'), export_format='GLB',
                                  use_selection=True)
    except Exception as e:
        print('GLB export failed', obj.name, e)
    try:
        bpy.ops.export_scene.fbx(filepath=os.path.join(FBX, obj.name + '.fbx'), use_selection=True,
                                 object_types={'MESH'}, path_mode='COPY', embed_textures=True,
                                 apply_scale_options='FBX_SCALE_ALL')
    except Exception as e:
        print('FBX export failed', obj.name, e)

# ----------------------------------------------------------------------------------------------
# 5b. Unity package: LOD0/LOD1/Collider per asset, Unity axis FBX, loose textures
# ----------------------------------------------------------------------------------------------
import shutil
UNI = UNITY_KIT or os.path.join(OUT, 'Unity', 'OblivionFoliage')
if os.name == 'nt' and len(UNI) > 180:
    UNI = '\\\\?\\' + UNI  # Windows long-path prefix
UMOD = os.path.join(UNI, 'Models'); UTEX = os.path.join(UNI, 'Textures')
for d in (UMOD, UTEX):
    os.makedirs(d, exist_ok=True)
for fn in os.listdir(TEX):
    shutil.copy2(os.path.join(TEX, fn), os.path.join(UTEX, fn))

NO_LOD = {'SM_Grass_Clump', 'SM_Flower_Patch', 'SM_Ground_Debris'}
COLLIDE = {'Rocks', 'Logs'}


def new_obj_from_bm(name, bm, src_mats=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    if src_mats:
        for m in src_mats:
            me.materials.append(m)
    o = bpy.data.objects.new(name, me)
    COL.objects.link(o)
    return o


def make_lod1(obj, cat):
    if obj.name in NO_LOD:
        return None
    rng = random.Random(sum(map(ord, obj.name)))
    if cat in ('Rocks', 'Logs') or obj.name == 'SM_Mushroom_Cluster':
        bm = bmesh.new(); bm.from_mesh(obj.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        tmp = new_obj_from_bm(obj.name + '_tmp', bm, obj.data.materials)
        mod = tmp.modifiers.new('dec', 'DECIMATE')
        mod.ratio = 0.4 if cat == 'Rocks' else 0.45
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
        bpy.data.objects.remove(tmp)
        me.name = obj.name + '_LOD1'
        shade_flat(me)
        o = bpy.data.objects.new(obj.name + '_LOD1', me)
        COL.objects.link(o)
        return o
    # foliage: drop ~45% of leaf-card islands, keep stems
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    seen, kill = set(), []
    for f in bm.faces:
        if f.index in seen or f.material_index != 0:
            continue
        isl, stack = [], [f]
        seen.add(f.index)
        while stack:
            g = stack.pop(); isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen and h.material_index == 0:
                        seen.add(h.index); stack.append(h)
        if rng.random() < 0.45:
            kill.extend(isl)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    me = obj.data.copy()
    me.name = obj.name + '_LOD1'
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(obj.name + '_LOD1', me)
    COL.objects.link(o)
    return o


def make_collider(src, name):
    bm = bmesh.new()
    for v in src.data.vertices:
        bm.verts.new(v.co)
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    dead = list({g for g in res['geom_interior'] + res['geom_unused'] if isinstance(g, bmesh.types.BMVert)})
    bmesh.ops.delete(bm, geom=dead, context='VERTS')
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(4), verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    return new_obj_from_bm(name, bm)


for obj, cat in ASSETS:
    base = obj.name
    extra = []
    lod1 = make_lod1(obj, cat)
    if lod1:
        extra.append(lod1)
    if cat in COLLIDE and base != 'SM_Rock_Pebbles':
        extra.append(make_collider(lod1 or obj, base + '_Collider'))
    obj.name = base + '_LOD0'
    for o in scene.objects:
        o.select_set(False)
    for o in [obj] + extra:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obj
    try:
        bpy.ops.export_scene.fbx(filepath=os.path.join(UMOD, base + '.fbx'), use_selection=True,
                                 object_types={'MESH'}, path_mode='STRIP', embed_textures=False,
                                 apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
                                 axis_forward='-Z', axis_up='Y', mesh_smooth_type='OFF',
                                 colors_type='LINEAR')
    except Exception as e:
        print('Unity FBX export failed', base, e)
    print('unity', base, [o.name for o in extra], [len(o.data.polygons) for o in extra])
    obj.name = base
    for o in extra:
        me = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(me)

# ----------------------------------------------------------------------------------------------
# 6. Preview layout, helpers, camera, light; save .blend
# ----------------------------------------------------------------------------------------------
def box(name, mn, mx, cat):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((mn[0] + (v.co.x + 0.5) * (mx[0] - mn[0]), mn[1] + (v.co.y + 0.5) * (mx[1] - mn[1]),
                       mn[2] + (v.co.z + 0.5) * (mx[2] - mn[2])))
    bm.to_mesh(me); bm.free()
    me.materials.append(M_STONE)
    o = bpy.data.objects.new(name, me)
    PREV.objects.link(o)
    o['category'] = cat
    o['preview_helper'] = True
    return o


rows = {'Plants': 0.0, 'Rocks': 7.0, 'Logs': 14.0, 'Bushes': 21.0, 'Vines': 28.0}
for cat, y in rows.items():
    objs = [o for o, c in ASSETS if c == cat]
    widths = []
    for o in objs:
        xs = [v[0] for v in o.bound_box]
        widths.append((min(xs), max(xs)))
    gap = 1.0
    total = sum(b - a for a, b in widths) + gap * (len(objs) - 1)
    x = -total / 2
    for o, (a, b) in zip(objs, widths):
        o.location = (x - a, y, 0)
        x += (b - a) + gap
        if o.name == 'SM_Vine_Hanging':
            o.location.z = 2.6
            box('Preview_Ledge', (o.location.x - 1.1, y, 0), (o.location.x + 1.1, y + 1.0, 2.6), cat)
        if o.name == 'SM_Vine_Ivy_Wall':
            box('Preview_Wall', (o.location.x - 1.4, y, 0), (o.location.x + 1.4, y + 0.3, 3.0), cat)

gme = bpy.data.meshes.new('Preview_Ground')
gme.from_pydata([(-200, -200, 0), (200, -200, 0), (200, 200, 0), (-200, 200, 0)], [], [(0, 1, 2, 3)])
gme.materials.append(M_GROUND)
g = bpy.data.objects.new('Preview_Ground', gme)
PREV.objects.link(g)

sun_d = bpy.data.lights.new('Sun', 'SUN')
sun_d.energy = 4.0
sun_d.color = (1.0, 0.95, 0.85)
sun_d.angle = math.radians(3)
sun = bpy.data.objects.new('Sun', sun_d)
sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
PREV.objects.link(sun)

world = bpy.data.worlds.new('World')
scene.world = world
try:
    world.use_nodes = True
except Exception:
    pass
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs['Color'].default_value = (0.45, 0.58, 0.80, 1)
bg.inputs['Strength'].default_value = 0.8

tgt = bpy.data.objects.new('PreviewTarget', None)
PREV.objects.link(tgt)
cam_d = bpy.data.cameras.new('PreviewCamera')
cam_d.lens = 50
cam_d.clip_end = 500
cam = bpy.data.objects.new('PreviewCamera', cam_d)
PREV.objects.link(cam)
con = cam.constraints.new('TRACK_TO')
con.target = tgt
con.track_axis = 'TRACK_NEGATIVE_Z'
con.up_axis = 'UP_Y'
scene.camera = cam

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'FoliagePack.blend'), relative_remap=True)
print('DONE', len(ASSETS), 'assets')
