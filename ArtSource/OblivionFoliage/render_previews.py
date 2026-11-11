"""Render preview images of FoliagePack.blend.
Run: blender -b FoliagePack.blend --python render_previews.py -- <output_dir> [eevee|cycles] [cat,cat]"""
import bpy, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else os.getcwd())
ENGINE = argv[1] if len(argv) > 1 else 'eevee'
ONLY = argv[2].split(',') if len(argv) > 2 else None
PV = os.path.join(OUT, 'Previews')
os.makedirs(PV, exist_ok=True)

scene = bpy.context.scene
scene.render.resolution_x, scene.render.resolution_y = 1600, 900
scene.render.resolution_percentage = 100
if ENGINE == 'eevee':
    for e in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scene.render.engine = e
            break
        except Exception:
            pass
    try:
        scene.eevee.taa_render_samples = 64
    except Exception:
        pass
else:
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.cycles.transparent_max_bounces = 32


scene.view_settings.exposure = 0.0
bpy.data.lights['Sun'].energy = 5.0
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.2
g = bpy.data.materials['M_Preview_Ground'].node_tree.nodes['Principled BSDF']
g.inputs['Base Color'].default_value = (0.09, 0.10, 0.04, 1)
cam = bpy.data.objects['PreviewCamera']
tgt = bpy.data.objects['PreviewTarget']
objs = [o for o in scene.objects if 'category' in o]

# Optional style check: OAK_BLEND=<path to Aelid-Oak-Grove.blend> puts Oak_Broadcrown next to the bushes
# and Oak_Windlean next to the logs.
oak_blend = os.environ.get('OAK_BLEND')
if oak_blend and os.path.exists(oak_blend):
    with bpy.data.libraries.load(oak_blend, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.startswith(('Oak_Broadcrown_', 'Oak_Windlean_'))]
    for cat, prefix in (('Bushes', 'Oak_Broadcrown_'), ('Logs', 'Oak_Windlean_')):
        row = [o for o in objs if o['category'] == cat]
        xmax = max((o.matrix_world @ Vector(c)).x for o in row for c in o.bound_box)
        y = row[0].location.y
        for o in dst.objects:
            if o is not None and o.name.startswith(prefix):
                scene.collection.objects.link(o)
                o.parent = None
                o.location = (xmax + 3.5, y, 0)
                o.rotation_euler = (0, 0, 0)
                o['category'] = cat
                objs.append(o)


def bbox(os_):
    pts = [o.matrix_world @ Vector(c) for o in os_ for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


cats = ['Bushes', 'Logs', 'Rocks', 'Vines', 'Plants', 'Overview']
for cat in (ONLY or cats):
    ov = cat == 'Overview'
    for o in objs:
        o.hide_render = not (ov or o['category'] == cat)
    sel = objs if ov else [o for o in objs if o['category'] == cat]
    mn, mx = bbox(sel)
    ctr = (mn + mx) / 2
    w, h = mx.x - mn.x, mx.z - mn.z
    need = max(w, h * 16 / 9) * 1.04
    if ov:
        need = max(w, (mx.y - mn.y) * 1.25) * 1.1
    el = math.radians(38 if ov else 12)
    dist = need / 2 / 0.36 + (mx.y - mn.y) / 2
    tgt.location = (ctr.x, ctr.y, mn.z + h * (0.0 if ov else 0.42))
    cam.location = (ctr.x, ctr.y - dist * math.cos(el), tgt.location.z + dist * math.sin(el))
    scene.render.filepath = os.path.join(PV, f'Preview_{cat}.png')
    bpy.ops.render.render(write_still=True)
    print('rendered', cat)
