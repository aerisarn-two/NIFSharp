# Test resources

Opaque fixtures. The tests assert that loading a file and saving it reproduces it
byte for byte, so **nothing here may be re-saved or edited** — a file rewritten by
any tool, this library included, stops being evidence of anything.

## The Skyrim-era files

`generate_rb.nif`, `generate_rb_box.nif`, `generate_rb_sphere.nif` and
`multi_material_cube.nif` are copied from
[ck-cmd](https://github.com/aerisarn/ck-cmd)'s `examples/blender`. All are
20.2.0.7, exported from Blender, and each carries a rigid body or a material
arrangement worth reading back.

## nifly

See `nifly/README.md`. Those come from
[nifly](https://github.com/ousnius/nifly) and are GPL-3.0, the same licence as
this library. They are the broader corpus: several stream versions, skinned
meshes, deep graphs, loose blocks, and one deliberately corrupted file that
exists to fail loading.
