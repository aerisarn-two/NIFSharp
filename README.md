# NIFSharp

Reads and writes NIF files — the Gamebryo/NetImmerse model format Bethesda's
games use — in C#.

```csharp
NifXmlDatabase schema = NifXmlDatabase.LoadEmbedded();
NifModel model = NifModel.Load("meshes/clutter/apple.nif", schema);

foreach (NifItem block in model.Blocks)
    Console.WriteLine(block.BlockType);

model.Save("out.nif");
```

## Driven by nif.xml, not by generated classes

A NIF block is not a struct. What fields it has depends on the file's version,
on the user version, and on the value of fields read earlier in the same block —
`Num Vertices` decides how long the vertex array is, `Has Normals` decides
whether the next field exists at all.

So there are no generated per-block types here. `nif.xml`, the schema NifSkope
maintains, is embedded in the assembly and parsed at load, and a block comes back
as a tree of named `NifItem`s with conditions already evaluated. Reaching a value
means naming it:

```csharp
NifItem? shape = model.FindItem(block, "Vertex Data");
uint count = model.FindItem(block, "Num Vertices")!.Value.ToUInt();
```

The cost is that nothing is checked at compile time. The gain is that every
version the schema describes can be read, including ones nobody thought about
when this was written, and that a file whose fields are in an order no generated
class expects still round trips.

## The round trip is the whole point

Load a file, save it, and you get the same bytes. Every fixture in the test suite
asserts exactly that, and it is the only check that means much for a format
reader: it fails if a field is skipped, if a condition is evaluated wrongly, if
an array length is misread, if padding is invented, or if the two stream
directions disagree anywhere at all.

150 tests run against 24 files — Skyrim LE and SE, skinned meshes, Havok
collision, deep graphs, loose blocks — and none of them needs game data, so they
run anywhere.

## What is here and what is not

This is the format layer: the schema, the item tree, both stream directions, the
block list, versions, and the ordering rules a NIF is meant to store its blocks
in.

It is deliberately not a scene graph, and it holds no opinion about what a file
*means*. Deciding what a `BSXFlags` should say, what a skin partition should look
like, or how a constraint maps onto something else, is the caller's business.
[se-cmd](https://github.com/aerisarn/se-cmd) is where that lives for the
FBX↔NIF conversion this was extracted from.

## Provenance and licence

`NifModel` is a port of the parts of NifSkope's `NifModel` that move bytes,
without the Qt model/view machinery. NifSkope is **GPL-3.0**, so this library is
too — unlike the sibling packages, which are MIT. A project that links it must be
GPL-compatible.

`nif.xml` itself is vendored verbatim from NifSkope and carries the BSD licence of
the NIF File Format Library and Tools project; see `src/NIFSharp/nifxml/README.md`.
Test fixtures from [nifly](https://github.com/ousnius/nifly) are GPL-3.0 and
redistributed as such.

## Installing

Published to GitHub Packages, so the feed has to be named in a `nuget.config`
beside your solution:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="github" value="https://nuget.pkg.github.com/aerisarn-two/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="%GITHUB_USERNAME%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github>
  </packageSourceCredentials>
</configuration>
```

Then `<PackageReference Include="NIFSharp" Version="1.0.0" />`, with
`GITHUB_USERNAME` and `GITHUB_TOKEN` (a PAT carrying `read:packages`) in the
environment.

## Releasing

Tagging is the trigger. `git tag v1.2.3 && git push origin v1.2.3` builds, tests,
packs, publishes to GitHub Packages and creates a release carrying the assembly
and the `.nupkg`. `workflow_dispatch` does everything except publish.
