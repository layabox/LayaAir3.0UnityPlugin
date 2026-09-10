# Material render state regression tests

Run from PowerShell with an already compiled Unity project that references this plugin:

```powershell
./Tests~/run-material-state-tests.ps1 `
  -UnityProject 'D:/project/UnityProject/ParticleLib' `
  -UnityEditor 'D:/tool/Unity/Editor/2022.3.62f3/Editor/Unity.exe' `
  -ShaderGraph 'D:/project/UnityProject/ParticleLib/Assets/Piloto Studio/Shaders_Reforged/Piloto Warp.shadergraph'
```

The optional `ShaderGraph` argument checks the audited Warp fixture. The other tests
use small synthetic targets with conflicting material properties. They cover active
target references, disabled/enabled material overrides, URP/Built-in differences,
depth/cull mappings, all four blend modes, URP Lit specular preservation, alpha clip,
custom queues, and JSON state ordering/cleanup.

The runner compiles the full Editor assembly with the project's Unity compiler
response file, then executes the production parser, state resolver and JSON writer
under .NET. It requires Unity's Bee/Roslyn compiler layout (verified with Unity
2022.3) and a local .NET runtime. Output goes to ignored `artifacts/`. It does not
replace the project's assemblies, run native Unity material APIs, re-export project
assets, or test rendering. `Tests~` is ignored by Unity's package importer.

The new resolver supports Built-in and URP Lit/Unlit ShaderGraph targets. Unsupported
targets and legacy graph formats emit a warning and retain the legacy material
fallback. Ordinary ShaderLab parsing and configured uniform/texture mappings retain
their existing behavior.
