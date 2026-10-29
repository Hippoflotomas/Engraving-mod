# ModelConvert

Turns a Blender model into the `.hmesh` file the mod embeds and builds at runtime (no Unity asset bundle).
Same tool and format as Heraldry Mod's.

```
assimp export ../../Assets/Chisel.fbx chisel.obj
python obj_to_hmesh.py chisel.obj ../../Engraving/Art/Chisel.hmesh --scale 1
```

- Needs `assimp` (Assimp command-line tools) and Python with numpy.
- One submesh per Blender material slot; the mod puts a vanilla material on each by name:
  `wood` (the wood wall's) and `Metal` (the iron wall's).
- `--scale 1` because assimp writes the chisel in metres; Heraldry's models came out in centimetres (the default 0.01).
- The chisel is modelled blade up (+Y in Unity), origin in the middle of the handle, where the hand grips it.
