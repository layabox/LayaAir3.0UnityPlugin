# C# 转换器移植方案（蓝图转换器 + VFX 转换器）

> 状态：**✅✅✅ 移植完成 = 100% byte-exact**。蓝图 45/45 + VFX 132/132 文件产物与 JS 逐字节一致（仅忽略 version 时间戳）、全部零崩溃。
> 创建：2026-07-01。目标插件：`F:\Unity\projects\Universal3D\Assets\LayaAir3.0UnityPlugin`

## ✅ 收官（100% byte-exact）
- **蓝图转换器**：45/45 `.shadergraph`→`.bps` 逐字节一致。
- **VFX 转换器**：132/132 `.vfx`→`.laya.vfx` 逐字节一致（total diff = 0）。ran=132/crashed=0。
- 最后攻克的关键 gap：rid 大整数用 double 解析（复刻 JS parseInt 碰撞→composed-particle topology）、ResolveResourceRef 空 layaUuid 返回 null（非 "res://"）、getProperty/builtin/LogicalNot 三个 link pass、setSpawnEventAttribute reroute、setPositionShape reroute、mesh-size-fix、materialize overlay、angle 16z、initialEventName 校验、.lm 解析、vortex 子图内联、IDE workaround 等全部 post-fix。
- 测试期辅助数据（正式插件改接 `ResoureMap`）：scratchpad 的 guidmap/vfx-defs/sgmap/bpsmap/lmmap/submap.json + 双 vfx-asset-mapping + vfx-texture-variants（gen 脚本 genmap/gendefs/genmaps2/genlm/gensub.js）。验证 harness：TestVfx.cs/batch_vfx.js/vfxdiff.js/catdiff.js。
- ⚠正式集成 TODO：把上述测试期 json 映射改接插件 `ResoureMap.GetTextureFile/GetMeshFile`（导出资源分配 UUID）+ 扫 .shadergraph.meta/.bps.meta/.lm.meta/.vfxblock.meta（现由 gen 脚本代劳）；菜单入口（对齐蓝图的 ShaderGraphConvertMenu）。

---

---

## ✅ 阶段1（蓝图转换器）已完成 — 2026-07-01

**结果：全部 45 个 `.shadergraph` C# 产物与 JS 产物逐字节一致（仅 version 时间戳不同）。0 差异 / 0 报错。**

### 已创建文件（`Editor/Export/shadergraph/`）
| 文件 | 职责 |
|---|---|
| `Jval.cs` | 自研 JSON DOM（有序对象/数组，可读写改）+ 自研解析器 `Jval.Parse` + 忠实模仿 `JSON.stringify(x,null,2)` 的写出器（正确转义、不变文化、**最短 round-trip 数字** `ShortestRoundTrip`）。**不用插件 JSONObject**：其 `type`/`list` 字段 private 无法转换，且 Print 不转义字符串（会破坏含换行的 GLSL `code`）。 |
| `SgIndex.cs` | 多 JSON 深度切分 `ParseShadergraph` + 建索引 `Build`（byId/graphData/nodes/slots/edges/properties/targets），对应 JS parseShadergraph+buildIndex。 |
| `SgNodeMapping.cs` | 映射表 `NODE_MAPPING`（含所有 customGlsl 委托）+ PBR/UNLIT/VERTEX slot 常量表。 |
| `ShaderGraphConverter.cs` | 核心转换器（移植整个 JS `Converter` 类，方法一一对应）。含 m_Id 通道修复、Normal 解码、alpha-test、supportVFX、materialProps/sgInstanced 三模式。 |
| `ShaderGraphConvertMenu.cs` | Editor 菜单入口 `LayaAir3D/转换器/选中的 .shadergraph → .bps`（+ support-vfx / material-properties 变体）。唯一依赖 UnityEditor 的文件。 |

**关键设计**：核心 4 文件（除菜单）**零 Unity 依赖** → 可脱离 Unity 独立编译/测试（见下）。命名空间 `LayaAir3.Converter`。

### ⭐ 可复用验证 harness（阶段2/3 照用）
核心文件零 Unity 依赖，故可用 **Unity 自带 Mono Roslyn** 独立编译并端到端对拍 JS：
- 编译器：`F:\Unity\Editor\6000.3.12f1\Editor\Data\MonoBleedingEdge\lib\mono\4.5\csc.exe`
- 运行时：`...\MonoBleedingEdge\bin\mono.exe`
- 测试入口：`<scratchpad>\TestMain.cs`（读 .shadergraph→ 转 → 写 .bps；支持 --support-vfx/--material-properties/--sg-instanced）
- 编译：`mono.exe csc.exe -out:TestConv.exe -langversion:6 Jval.cs SgIndex.cs SgNodeMapping.cs ShaderGraphConverter.cs TestMain.cs`
- 对拍脚本：`<scratchpad>\batch_strict.js`（全量跑 C# vs `node unity-shader-to-laya.js`，严格逐值 diff，仅忽略 version）
- Unity 版本：**6000.3.12f1**（Unity 6，非 17.3！包锁 17.3 指 SG/VFX package 版本）。

### 待接入（后续）
- 菜单目前把 `.bps` 写在 `.shadergraph` 同目录（供 diff）；正式集成时应走 `JsonFile`/`FileData` + `ResoureMap`（纹理 GUID→UUID，取代 JS 的 vfx-asset-mapping.json）。
- `--material-properties` 的 texture 属性 UUID 目前依赖 `Options.AssetMapping`（空则输出 `""`）；接 ResoureMap 后自动填。

---

## 🚧 阶段2（VFX 转换器）进行中 — 2026-07-01

VFX 主转换器 `unity-vfx-to-laya.js` 实际 **~6000 行**（比记录的 5528 大），含 2000+ 行的 `BUILTIN_SLOT_MAP`(L2583-4733)、`convertSpawnBlock`(450行)、subgraph 扫描、asset mapping、大量 block 转换器。分子阶段推进，每步 Mono-csc 对拍。

### ✅ Stage 2.0：UnityYamlParser 完成（`Editor/Export/vfx/UnityYamlParser.cs`）
移植 JS 的 YAML 解析层：`ParseEntries`（按 `--- !u!<t> &<id>` 切块）+ `ParseRefIds`（SerializeReference RefIds）+ 全部 getXField（GetStringField/GetIntField/GetFloatField/GetNestedSerializableNumber/GetRefField/GetNestedRid/GetRefArrayField/GetOutputFlowSlot/GetUIPos）+ slot 提取（GetSlotInlineValue→Jval / GetSlotType / GetSlotPropertyName / GetSlotTypeName / GetSlotMaster / GetLinkedSlots / GetSlotOwner / GetExposedName / ResolveExposedNameForInputSlot）+ handedness（ConvertUnityToLayaHandedness/FlipTransform/FlipFlatTransform）。这些直接在原始文本 body 上正则提取（不建通用 YAML DOM）。

**验证**：全部 **132 个 UNI .vfx** 解析指纹（fileID|typeNum|classType|UIPos|inlineValue|linked|owner|master|typeName|exposed）与 JS **数值精确一致（132/132）**。harness：scratchpad `TestYaml.cs` + `yaml_ref.js` + `batch_yaml.js`。

⚠**数字表示边缘情况**：内嵌 JSON 里 >2^53 的 64 位 fileID、偶发曲线末位 float，两边同一个 double 但最短十进制串写法不同（JS Ryu vs 我的 `Jval.ShortestRoundTrip` 的 G1-17 循环）。**同值不同写法**，IDE 重解析成同值；仅出现在曲线数据(装饰性)/obj-ref(靠 guid 解析,数字不用)。真 Unity6 运行时 `double.ToString()`=Ryu 与 JS 一致；仅 Mono4.5 dev-harness 上现差异。若后续曲线要 byte-exact 再实现规范最短往返。

### ✅ Stage 2.1：映射表完成（`Editor/Export/vfx/VfxMaps.cs`）
移植全部 20 张表 + 分类 helper：CONTEXT_CLASSES / SPAWN_BLOCK_CLASSES / IsSlot / IsOperatorNode / SHADER_UNIFORM_RENAME / CTX_MAP / BLOCK_MAP / POSITION_SHAPE_FROM_CLASS / COMPOSITION_MAP / SOURCE_MAP / RANDOM_MAP / ATTR_TO_TYPE + AttrType / OP_MAP / OP_PROPNAME_ALIAS / NormalizeAttrName / BUILTIN_SLOT_MAP / CUSTOM_ATTR_TYPE_MAP / BUILTIN_ATTR_TYPE_HINT / CM_UNIT_MESH_NAMES / SPECIAL_MESH_SCALE_UUIDS。约定：Dict 值 null=已知跳过，key 缺失=未知（保留 JS 语义）。
**验证**：C# dump 与从 JS 源码 eval 出的同名表 **逐字节一致（20/20 表）**。harness：scratchpad `TestMaps.cs` + `maps_ref.js`。
> 📌 更正：先前担心的 `BUILTIN_SLOT_MAP`(L2583-4733) 实为 **7 条小表**——L2583→L4733 的间隔是转换*逻辑*代码（builtin 展开 / LogicalNot / block 转换器等），非 2000 行数据表。所有映射表都小。

### ✅ Stage 2.2（curve/gradient 纯转换层）完成（`Editor/Export/vfx/VfxCurveGradient.cs`）
移植 `ConvertUnityCurveToLaya`（AnimationCurve→frameData 每帧7 float）/ `UnityGradientToLayaStops`（colorKeys+alphaKeys 合并采样→stops）/ `MaxNormalizeStripGradient`（strip HDR 逐键归一，原地改 stops+_rgbElements）/ `StopsToIdeGradient`（stops→IDE GradientField 格式）/ `ResolveUpstreamInlineCurveGradient`（沿 link 回追 VFXInlineOperator 曲线值，用 Func 查找）。全部纯函数，无转换器状态依赖。
**验证**：全部 132 个 UNI .vfx 抽出的 **1104 曲线 + 450 渐变**转换与 JS **数值精确一致（132/132，0 真实差异，113 同值浮点表示差异）**。harness：scratchpad `TestCurve.cs` / `curve_ref.js` / `batch_curve.js`。
> 📌 Stage 2.2 剩余的**常量折叠/slot-scalar 解析** helper（evalConstOperatorFromInputSlot / resolveSetAttrScalar / resolveAttrComponentRange / readSpawnNumberSlot）依赖转换器全局状态（byID/算子图），归入 Stage 2.3/2.4 与主转换器类一起建。

### ✅ Stage 2.3a（VfxConverter 状态地基 + 常量折叠层）完成（`Editor/Export/vfx/VfxConverter.cs`，partial 类）
建 `VfxConverter` 类持有全局状态（本阶段：`ById` = fileID→VfxEntry）+ 移植常量折叠层：`EvalConstOperatorFromInputSlot`（沿 link 递归折叠 Multiply/Add/Subtract/Divide/Max/Min/Remap/VFXInlineOperator/VFXParameter 常量链）/ `ResolveSetAttrScalar` / `ResolveAttrComponentRange`（含 Random 算子 + constant:1 的 sin 确定性哈希）/ `ReadSpawnNumberSlot`。类声明为 `partial`，后续 stage 往同类加方法。
**验证**：全部 132 个 UNI .vfx 对每个 entry 跑 4 个折叠函数，**10164 个非空结果与 JS 数值精确一致（132/132，0 真实差异，783 同值浮点表示差异）**。⭐关键：`Math.Sin` 确定性哈希（const-random）在 Mono 与 Node 上结果一致。需真实 guid→class 映射才能触发 operator 折叠分支——用 `genmap.js` 扫 Unity VFX/URP 包（1174 类）生成 `guidmap.json` 两边共用。harness：scratchpad `TestConst.cs` / `const_ref.js` / `batch_const.js` / `genmap.js`。

### ✅ Stage 2.3b（3 个 block 转换器）完成（`Editor/Export/vfx/VfxConverter.Blocks.cs`，partial 类）
移植 `ConvertSetAttribute`（float/color/vec3；op-driven Random Per Component 检测；channels VariadicOptions→位掩码；velocity z 翻转 + angle rx/ry 取反手性）/ `ConvertAttributeFromCurve`（curve/gradient 分支 + vec3 逐通道曲线 + IDENTITY_CURVE 去重 + channels 掩码）/ `ConvertOrient`（Advanced 静态/AlongVelocity/FixedAxis 等全模式 + axes 映射）。
**验证**：全部 132 个 UNI .vfx 的 **2791 个 block（SetAttribute+AttributeFromCurve+Orient）转换与 JS 数值精确一致（132/132，0 真实差异，54 同值浮点差异）**。harness：scratchpad `TestBlocks.cs` / `blocks_ref.js` / `batch_blocks.js`。

### ✅ Stage 2.3c（convertSpawnBlock）完成（`Editor/Export/vfx/VfxConverter.Spawn.cs`，partial 类）
移植 `ConvertSpawnBlock`（ConstantRate / Burst[Old]（Single/Periodic + count←Modulo(spawnState.loopIndex,N) 检测）/ PeriodicBurst / VariableRate）。依赖已就位的 `ReadSpawnNumberSlot`。
**验证**：全部 132 个 UNI .vfx 的 **418 个 spawn block 与 JS 逐字节一致（132/132，0 真实差异，0 表示差异）**。harness：scratchpad `TestSpawn.cs` / `spawn_ref.js` / `batch_spawn.js`。
> 📌 注：`convertSpawnBlock` 仅 ~85 行（L2097-2181），先前记的 450 行含了 L2183+ 的 operator 主循环——那属于 2.4 主驱动。

### ✅ Stage 2.3 全部 block 转换器完成（setAttribute / attributeFromCurve / orient / spawnBlock）

### ✅ Stage 2.4a（IDE def 数据依赖 LayaDefs）完成（`Editor/Export/vfx/LayaDefs.cs` + `vfx-defs.json`）
JS 版 `laya-defs-loader.js` 直接解析 IDE 的 `VfxContextDefs/VfxBlockDefs/VfxOperatorDefs.ts`（在 `E:/LayaIdea/.../data/`）；C# 版改为**加载预导出的 `vfx-defs.json`**（用 `scratchpad/gendefs.js` 从同源 .ts 生成，已复制进插件 `Editor/Export/vfx/vfx-defs.json`，35KB，静态随 IDE 版本固定）。`LayaDefs.FromJson` 提供 `GetInputs`/`GetOutputs`/`GetAffinity`/`FillDefaults`。
**验证**：278 个 typeId 的 FillDefaults(空)/GetInputs/GetOutputs/GetAffinity 与 JS loader **逐字节一致（1113 行，0 差异）**。harness：scratchpad `TestDefs.cs` / `defs_ref.js` / `gendefs.js` / `diff_defs.js`。
> ⚠ 若 IDE 的 def .ts 更新，需重跑 `gendefs.js` 重新生成 `vfx-defs.json`。

### ✅ Stage 2.4 纯 helper 已就位（`Editor/Export/vfx/VfxHelpers.cs`）
主驱动用到的纯字符串/枚举 helper：`UnityBlendModeToLaya` / `UnityTypeToGlslType` / `UnityTypeToLayaPropType` / `SanitizePropName`。**验证**：固定输入集（覆盖全部分支+边缘：unicode 名/数字前缀/blend 越界）与 JS 逐字节一致。harness：scratchpad `TestHelpers.cs` / `helpers_ref.js`。

### ID 分配顺序（2.4 主驱动关键，已调研）
`newId()` 全局计数器从 1 开始，按顺序：**① 所有 context（CONTEXT_CLASSES 过滤）② 所有 top-level operator（isOperatorNode 且不在任何 context 的 m_Children 里、m_Parent 不指向 context）③ 所有 block（context 的 m_Children）**。之后 operator 主循环用预分配的 `fileIDToLayaId.get(op.fileID)`（不再 newId）；builtin/LogicalNot 展开在主循环后才 newId。

### ✅✅ Stage 2.4 端到端跑通！C# VFX 转换器完整产出 .laya.vfx（`VfxConverter.Driver[.2/.3/.4].cs`）
**全部 132 个 UNI .vfx：C# 转换器 ran=132 / crashed=0，与 JS 产物结构级一致**：
- context-count 132/132 匹配（1567 total 两边一致）
- operator-count 132/132 匹配（3409 total 两边一致）+ operator typeId 直方图完全一致
- property-count 132/132 匹配，property 名一致

`Convert(yamlText)` 入口串起：BuildClassificationAndIds → ConvertOperators → ExpandBuiltinLogicalNotParameters → BuildCustomAttributes → ConvertContexts(含 walkSlotTree shader 绑定 + buildExpression 表达式图 + processChildBlock block 分派 + context props) → LinkOperators(op→op) → BuildProperties → BuildOutput 组装。harness：scratchpad `TestVfx.cs` / `batch_vfx.js` / `vfxdiff.js`（用 guidmap.json + vfx-defs.json + vfx-asset-mapping.json）。

#### 🔽 gap 收敛进度（持续翻译剩余 JS 段中）
已补齐并验证（每补一项 diff 下降）：uiData 平移 post-fix、flowLinks、fillDefaults、全部 block 详细字段（Drag/Vortex/ConformToSphere/Turbulence/Force/TriggerEvent/PositionShape/FlipbookPlay/PositionSequential/PositionMesh）、**op→block link**（recordBlockSlotTree + blockLinkSlotId）、**shaderGraph 名解析**（ShaderGraphByGuid/BlueprintShaderByName，测试用 genmaps2.js 生成 sgmap/bpsmap.json）、**双 asset-mapping 合并**、**per-system getCtxSysInfo**（AgeOverLifetime continuous 判定）、planar output props（primitive/cameraSort/frustumCull/softParticleFade/cropFactor）、mainTexture/mesh/SG blendMode 解析。
追加补齐：outputTrail props、GPU/CPU event chain、mainTexture/mesh/SG blendMode、.lm 解析、mesh-size-fix 注入、IDE workaround（gradient→IDE 格式+VEC_ATTRS 删除+Custom sampleGradient）、initialize bounds shaderBindingLinks、**SkinnedMesh setAttribute(position)**（FindLinkedOp/ExtractHalfHM）、**vortex 子图内联**（SubgraphNameByGuid + BuildVortexProps）、**texture Repeat-variant 交换**（MainTextureRepeatVariant）、**angle 16z 双重手性修正**（ApplyAngleHandedness）、mesh-size-fix id 逐次重算。
又追加：**builtin/LogicalNot/getProperty 三个 link pass**（LinkBuiltinLogicalNotParameters + BlockLinkSlotId，⭐getProperty 连线是关键——补上后 avg 从 7→1）、**initialEventName 校验**、**setPositionShape/Mesh reroute（output→init）**。
- **全量 132：ran=132/crashed=0，context+operator 计数 132/132 一致，avg diff 49→0.8/文件，总 diff 6522→107，⭐118/132 文件完全 byte-exact**（+Phase1 蓝图 45/45 byte-exact）。
- 剩余 14 文件小 diff（worst wormhole 21/Patterned Hex 20）：VFXComposedParticleOutput 的 topology 检测边缘（2 文件）、mesh-size-fix id 边缘、少量 property default、通用 subgraph 内联（非 vortex）、_Color alpha=0 注入、setSpawnEventAttribute reroute、materialize overlay 等末梢 post-fix。
- 测试辅助 json：guidmap/vfx-defs/sgmap/bpsmap/lmmap/submap + 双 vfx-asset-mapping + vfx-texture-variants（gen 脚本 genmap/gendefs/genmaps2/genlm/gensub.js）。

#### 仍待翻译的 JS 段（长尾 post-fix，按剩余 diff 量排序）
1. **mesh-size-fix 注入**（L6014-6082 + L6082-6108）：给 mesh 输出 ctx 头部注入 `setAttribute(scale ×0.01/×0.2)` block（CM_UNIT_MESH_NAMES/SPECIAL_MESH_SCALE_UUIDS 已在 VfxMaps）——wormhole 缺 15 blocks 主因；含 `_meshSizeFix` 标记 + block 顺序。
2. **findLayaExportedLm（.lm 扫描）**：mesh `guid@lm0` → `res://<lm-uuid>`（JS 扫 assets/resources 下 *.lm.meta；C# 接 ResoureMap 或测试期生成 lm-map.json）。mesh uuid diff 源。
3. **gradient → IDE 格式 post-fix**：setAttributeCurve/outputTrail 的 `{colorKeys,alphaKeys}` → `stopsToIdeGradient` 的 `{_mode,_rgbElements,_alphaElements,stops}`。
4. **SkinnedMesh setAttribute(position)**（L3434-3538）：transformPosition 块 / Random-Y 提取。
5. **shaderBindingLinks 细节**（某些 output ctx 的 getProperty link 未命中）+ **initialize bounds shaderBindingLinks**（L4302-4321）。
6. **_Color alpha=0 注入 setAttribute**（L4640-4676）。
7. **其余 post-fix**（conformToSphere / AttributeFromCurve helper 删除 / multiply-add slot 重分配 / setSpawnEventAttribute·position reroute / materialize overlay / trigger-event 继承，L5193-6244）。
8. **subgraph 内联 inlineSubgraphOperators**（L1113-1250）。

> 收敛方式固定：读对应 JS 段 → 翻译成 partial 方法 → `batch_vfx.js` 全量对拍看 diff 下降。harness：scratchpad `TestVfx.cs`/`batch_vfx.js`/`vfxdiff.js`/`genmaps2.js`（+guidmap/vfx-defs/sgmap/bpsmap/双 asset-mapping）。

#### （历史）剩余细节 gap（早期记录）
1. **events=0/3**：GPU/CPU event chain（layaEvents）未移植（JS L5502-5578）。
2. **uiData.x/y 偏移**：所有 ctx uiData 差一个常量——「uiData 平移到画布原点」post-fix（JS L5420）未移植（trivial，收益大：~48 diff/文件）。
3. **flowLinks 全缺**：context 间 flow 连接（spawn→init→update→output）未移植（在 context 循环里，我跳过了 flowLinks 段）。
4. **spawn/setAttribute 部分字段**：如 delayRange；setAttribute 的 op-driven「Per Component」需 SkinnedMesh Random-Y 提取(TODO)或 op→block link。
5. **shaderBindingLinks 部分 / op→block link 全缺**：op→block link 需 block input slot 注册（{blockId,ctxId,slotPropName}，JS 在 block dispatch 里做）——本版只做了 op→op link。
6. **15 个 post-fix + conformToSphere + subgraph 内联 + shaderName 解析(ShaderGraphByGuid/BlueprintShaderByName 需接 ResoureMap/扫 .meta)** 未移植。

> 结论：**驱动骨架 + 全部零件端到端跑通，结构级 132/132 对齐**。剩余是把上述 6 类细节逐项补齐（多为独立 pass / 字段提取），每补一项 diff 数下降，直至语义 byte-align。这已是可交付用户验证的可运行转换器。

### （历史）🚧 Stage 2.4b backbone 首版（`Editor/Export/vfx/VfxConverter.Driver.cs`）
已移植 + **Mono-csc 编译通过**（尚未做输出对拍，待 context 循环补齐后统一验证）：
- 全局状态（NewId/FileIDToLayaId/BlockFileIDToLayaId/LayaOperators/SlotToOpId/LayaContexts/各展开列表 + LayaDefs/AssetMapping 注入）
- `ResolveResourceRef` + `SwapMainRepeatVariant`（`FindLayaExportedLm` 作可注入委托，默认 null=.lm swap 待接 ResoureMap）
- `BuildClassificationAndIds`（context/operator/block 分类 + 三段 ID 分配，对应 JS L1264-1305）
- `ConvertOperators`（operator 主循环，对应 JS L2187-2543：OP_MAP 分流 / VFXInlineOperator 按 m_Type / RecordSlotTree / inline/getAttribute/`_inputs` 提取 / op-specific 字段 / 多态 `_type`）
- `ExpandBuiltinLogicalNotParameters`（builtin/LogicalNot/getProperty 展开，对应 JS L2592-2729）

#### ⏭️ 精确 resume 点（接续从这里开始）
1. **context 主循环**（JS L2734-4697）——写 `VfxConverter.Driver2.cs`。含子函数 `buildExpression`(L2833-3044,~210 行,expression 图 builder：Constant/VFXParameter/VFXTime/Sample*/Add-Sub-Mul-Div-Mod/Sin-Cos-Frac-Abs-Neg-Saturate/Lerp/Random/AgeOverLifetime/GlobalTimeRatio→graphNodes)、`walkSlotTree`(L3046-3250,shader 属性绑定：bindings/shaderPropertyDefaults/shaderPropertyExpressions/shaderBindingLinks + 子分量 Combine + inline 常量捕获)、`processChildBlock`(L3277+,~800 行,递归含 VFXSubgraphBlock 展开[需 loadSubgraph]/disabled 检测[m_Disabled/m_ActivationSlot]/PositionShape·CollisionShape unified 类型/30+ block typeId 分派，调用已就位的 ConvertSetAttribute/SpawnBlock/Orient/AttributeFromCurve)、context 级 props(topology/blend/sort/capacity/flowLinks)。还需 `findInitConstantAttributeValue`(L1382,依赖 Contexts)、`getCtxSysInfo`(L823)、`fileHasContinuousSpawn`(L786)、`initContextLifetime`(L809)。
2. **link pass**（L4759-5095）+ `blockLinkSlotId`(L4931)。
3. **properties[]**（L5095-5193）+ conformToSphere post。
4. **post-fix 群**（L5233-5724, 5786-6244）+ 输出组装（L5724-5786）。
5. 外部依赖：`inlineSubgraphOperators`+`scanSubgraphFiles`+`loadSubgraph`（子图）、asset mapping 载入（测试用 vfx-asset-mapping*.json）。
6. **统一验证**：Convert() 全通后跑 132 个 .vfx，与 `node unity-vfx-to-laya.js` 产物 numeric-tolerant JSON diff（忽略 version）。

#### 完整驱动结构图（已 grep 逐段定位，供接续直接照做）
主驱动在 JS 里是模块顶层顺序执行，端到端产出 `layaVfx` 对象后 `JSON.stringify` 写 .laya.vfx。移植成 `VfxConverter` 的一个 `Convert()` 方法 + 若干 partial 方法，按此顺序：

1. **前置扫描/内联**（L701-855, 1113-1250）：`scanGuidMap`（→已有 guidmap.json/ResoureMap）、`scanSubgraphFiles`/`loadSubgraph`、`scanShaderGraphMap`、`scanBlueprintShaderMap`、`inlineSubgraphOperators`（把 VFXSubgraphOperator 的子图算子内联进主图 entries，改 entry 集）。
2. **分类 + ID 分配**（L1264-1305）：`contexts`=CONTEXT_CLASSES 过滤；`fileIDToLayaId`：先 contexts 各 newId()，再 operatorEntries 各 newId()；`blockFileIDToLayaId`：各 context 的 m_Children newId()。（顺序即上面记的 ID 分配顺序）
3. **operator 主循环**（L2187-2543, ~356 行）：每个 operatorEntry → layaOp{id,typeId,uiData,output,props}；OP_MAP 分流；VFXInlineOperator 按 m_Type→inlineFloat/.../资源holder/curve-gradient-suppress；`recordSlotTree`（递归子 slot，slot名→def input id 匹配，用 GetInputs/GetOutputs=LayaDefs 已就位）；inline/getAttribute/通用 `_inputs` 提取（含 evalConst 折叠 linked 常量、resolveResourceRef 资源、curve/gradient bake）；op-specific 字段（noise/compare/swizzle/sequential mode）；多态 `_type` 推断（unityTypeToGlslType 已就位）。产出 `layaOperators` + `slotToOpId`。
4. **展开 pass**（L2592-2730）：builtin（VFXDynamicBuiltInParameter 7 slot→独立 builtin op，BUILTIN_SLOT_MAP 已就位）→ `builtinSlotToOp`；LogicalNot→subtract(1,x)+inlineFloat→`logicalNotInfo`；VFXParameter outputSlot→getProperty op→`parameterToOp`。
5. **context 主循环**（L2734-4697, ~1963 行，最大）：每 context→layaCtx{id,typeId,uiData,blocks,props}；17.3 topology/shading rid 解耦（outputShaderGraphMesh/Quad）+ 老式 inline shaderGraph；⭐**ShaderGraph 属性绑定 + 表达式图 builder**（L2812-3340, ~530 行：buildExpression 递归建 shaderPropertyExpressions/Bindings/Defaults/shaderBindingLinks，节点 kind=Constant/VFXParameter/VFXTime/SampleGradient/Add.../Random/GlobalTimeRatio/AgeOverLifetime；findInitConstantAttributeValue 依赖 contexts）；block 收集（L3340-4190，调用已移植的 ConvertSetAttribute/SpawnBlock/Orient/AttributeFromCurve + 各 block typeId 分流 + handedness）；context 级 props（blend/topology/sort/capacity/flowLinks）。
6. **resource holder / custom attr**（L4697-4759）：resourceInlineHolders 写下游 op.props；layaCustomAttributes。
7. **link pass**（L4759-5095, ~336 行）：operator output slot 的 m_LinkedSlots → `output.<slot>.infoArr=[{nodeId,slotId}]`（`blockLinkSlotId` 算 target slot id）；builtin/LogicalNot/getProperty(parameterToOp) 各自 link。
8. **properties[]**（L5095-5193）：VFXParameter→layaProperties（unityTypeToLayaPropType/sanitizePropName 已就位）；conformToSphere/AABox 填 center/radius。
9. **post-fix 群**（L5233-5724, 5786-6244, ~15 个 pass）：删 AttributeFromCurve helper op；IDE 编译器 bug 规避；uiData 平移；空 update 注入；`fillDefaults` 补全所有 props（LayaDefs 已就位）；GPU/CPU event chain→layaEvents；angle 双重手性；multiply/add slot 重分配；setSpawnEventAttribute/setPositionShape reroute；mesh size×0.01（CM_UNIT/SPECIAL_MESH_SCALE 已就位）；materialize overlay；trigger-event 继承。
10. **输出组装**（L5724-5786）：layaVfx = {contexts, operators, properties, events, customAttributes, version, ...}；写文件。

#### 已就位可直接调用的零件（占驱动依赖的大部分）
UnityYamlParser（全部字段/slot 提取）、VfxMaps（全部表）、VfxCurveGradient、VfxConverter 常量折叠、ConvertSetAttribute/AttributeFromCurve/Orient/SpawnBlock、LayaDefs（GetInputs/Outputs/Affinity/FillDefaults）、VfxHelpers（4 helper）。**剩余纯属驱动编排 + 表达式图 builder + 各 post-fix，无新增算法层。**

#### 待接外部依赖
- `resolveResourceRef`（GUID→res://uuid）：JS 用 vfx-asset-mapping.json + .lm 扫描；C# 接插件 `ResoureMap.GetTextureFile/GetMeshFile`。测试期可用 guidmap 式 json。
- `inlineSubgraphOperators` + subgraph 扫描：JS 扫 .vfxoperator/.vfxblock；C# 需移植（UNI VFX 语料里用到的话）。
- shaderGraph/bps 名映射（shaderGraphByGuid/blueprintShaderByName）：接 ResoureMap 或测试期 json。

#### 验证方式（统一验收）
移植完 Convert() 后，用 Mono-csc 编译整套，对全部 132 个 UNI .vfx 跑 C# 产出 .laya.vfx，与 `node unity-vfx-to-laya.js` 产物做语义 JSON diff（复用 batch_*.js 的 numeric-tolerant 比较，忽略 version）。逐文件报告对齐率 + 差异定位。
- 主驱动：`newId` / `fileIDToLayaId` / `blockFileIDToLayaId` / `slotToOpId` 建立；`inlineSubgraphOperators`；operator 主循环（L2183+，OP_MAP 分流 / VFXInlineOperator 按 m_Type / recordSlotTree / slot 名→IDE def input id 匹配）；context 遍历（CTX_MAP，blocks 收集，调用上面各 block 转换器）；**link pass**（operator/block output slot 的 m_LinkedSlots → Laya `output.<slot>.infoArr` / block input）；builtin/LogicalNot 展开；`findInitConstantAttributeValue`（依赖 contexts）；shader 属性/uniform；输出组装（layaContexts + layaOperators → .laya.vfx JSON）。
- 资源解析：JS 靠文件系统扫描（scanGuidMap / scanSubgraphFiles / scanShaderGraphMap / .lm 扫描）+ vfx-asset-mapping.json；C# 版改接插件 `ResoureMap`（导出资源时分配 UUID）。测试期可仿 `genmap.js` 用 guidmap.json + laya-defs（getInputs/getOutputs 需要 IDE def 数据，见 `laya-defs-loader`）。
- ⚠依赖 laya def 数据（`getInputs`/`getOutputs`/`getAffinity`/`fillDefaults`，来自 `tools/laya-defs-loader.js` 读 IDE VfxOperatorDefs 等）——2.4 需要把这套 def 数据也带进 C#（或运行期从 IDE 读）。

---

---

## 0. 目标

把两个已在真实项目验证过的 JS 转换工具翻译成 C#，集成进 Unity 插件 `LayaAir3.0UnityPlugin`：
1. **蓝图转换器**：Unity ShaderGraph(`.shadergraph`) → Laya 蓝图 shader(`.bps`)
2. **VFX 转换器**：Unity VFX Graph(`.vfx`) → Laya VFX(`.laya.vfx`)（含 prefab-variant 流程）

## 1. 决策：方案 A（移植 JS 裸文件解析），不用 Unity 内部 API

### 决定性事实（已实地调研）
- **Unity VFX/ShaderGraph 的图模型类型全是 `internal`**：`VFXGraph`/`VFXContext`/`VFXBlock`/`VFXOperator`/`VFXSlot`/`VFXParameter`、`GraphData`/`AbstractMaterialNode`/`MaterialSlot`/`SampleTexture2DNode` 都在 `Unity.VisualEffectGraph.Editor` / `Unity.ShaderGraph.Editor` 程序集里，外部**无法直接引用**。`VisualEffectAsset`(public) 不暴露图结构。
- **无法加 `InternalsVisibleTo`**（Unity 官方包，不受我们控制）→ 方案 B 只能靠**反射**（字符串名访问 internal），无编译期检查、Unity 小版本改名即崩、运行时才爆。
- **插件既有 `CustomShaderExporter`（11522 行）已经是"读 shader 文本 + 公开 `ShaderUtil` API"，完全没碰内部 ShaderGraph API** → 方案 A 与插件既有风格一致。
- **JS 已逐特效在项目里验证** → 方案 A = 翻译已验证代码；C# 产物可与 JS 产物 **diff 对齐**验证，等于间接复用验证。方案 B 得把所有特效重验一遍。
- Unity 包版本已锁 **17.3.0**（ShaderGraph / VisualEffectGraph / URP 都是 17.3.0）；`.vfx`/`.shadergraph` 序列化格式在同大版本内稳定。

### 结论
方案 A 唯一代价 = C# 里要有一个 Unity-YAML 解析器(~150 行) + 多 JSON 解析(几十行)，换来"翻译已验证代码"的确定性，远胜反射 internal API。

## 2. 要移植的 JS 源（都在 `F:\git\LayaAir3.0\LayaVFXSample`，均已提交 origin/master）

| JS 文件 | 行数 | 职责 |
|---|---|---|
| `shadertools/unity-shader-to-laya.js` | 1982 | **蓝图转换器**：`.shadergraph`(多JSON) → `.bps`。最独立，建议**第一阶段先做**。 |
| `tools/unity-vfx-to-laya.js` | 5528 | **VFX 主转换器**：base `.vfx`(Unity YAML) → `.laya.vfx`。最大。 |
| `tools/convert-uni-vfx.js` | 1029 | **prefab-variant 编排器**：五阶段 scan/copy/automap/convert/**prefab-variant**（把 prefab 的 m_PropertySheet override 烘到变体 + mesh-sync/tex-default binding/exposed-sync）。Smoke DM Black / Abrupt DM Teal 就是靠它重转的。 |

合计 ~8500 行。**临时脚本（`_patch_*`/`fix-*`/`dump-*`/`_disable_*` 等）不用翻**——已审计确认它们是一次性/实验/已被正式转换器取代（详见 §6）。

## 3. JS 的输入解析方式（C# 要复刻）
- **`.shadergraph` = 多 JSON**：`{...}{...}{...}` 拼接的多个 JSON 对象。JS 用 `parseMultiJSON`（按大括号深度切分）。C# 用插件的 `JSONObject` 逐个解析。
- **`.vfx` = Unity YAML**：`--- !u!<classId> &<fileId>` 文档块。JS 用自研 `parseEntries(yamlText, guidToClass)`（`tools/unity-vfx-to-laya.js` L359）+ `parseRefIds`（L435）：按 `^(?=--- !u!\d+ &\d+)` 切文档块、按 2 空格缩进提字段、`{fileID: N}` 解析引用。**C# 要移植这套（~150 行）**，不要用通用 YAML 库（Unity YAML 有特殊性）。

## 4. 目标插件结构（已调研）
```
LayaAir3.0UnityPlugin/
├─ Editor/Export/           # 导出转换核心
│   ├─ CustomShaderExporter.cs (11522行, 产 .shader+.glsl 直接GLSL, 非.bps; 用 File.ReadAllText+ShaderUtil)
│   ├─ LayaParticleExportV2.cs (ns LayaExport, Shuriken粒子, 不处理VFXGraph)
│   ├─ ShaderMappingEngine.cs
│   ├─ LayaAir3Export.cs     # 入口 ExportScene() 场景驱动
│   ├─ filter/*.cs           # FileData 子类: JsonFile/MaterialFile/MeshFile/TextureFile/HierarchyFile...
│   ├─ resoure/ResoureMap.cs (1427行) # 资源→FileData→UUID 映射
│   └─ utils/JsonUtils.cs, MeshUitls.cs...
│   └─ Util/JSONObject.cs (1112行, 自研JSON)
├─ Runtime/  LayaShader/  Documentation/
```
### 关键约定
- **JSON**：自研 `JSONObject` 类（`Util/JSONObject.cs`），`.Print(true)` 序列化。
- **文件写入**：`FileData` 基类 + 子类，`SaveFile(Dictionary<string,FileData> exportFiles)`；跨文件引用用 `AddRegistList(path)` + 保存时 `jsonContent.Replace(filename, file.uuid)`；`saveMeta()` 写 `.meta`。参考 `filter/JsonFile.cs`。
- **资源 UUID**：`ResoureMap.GetTextureFile()/GetMeshFile()/GetMaterialFile()` 拿导出后的 FileData(.uuid)。**C# 版不需要 JS 那套 `vfx-asset-mapping.json`**——插件导出资源时就分配 UUID，转换器直接用。
- **命名空间**：多数类**无 namespace**（少数 `LayaExport`/`Util`）；跟随现有风格即可。
- **现状**：插件**不处理** ShaderGraph/VFX Graph（grep 无 `.shadergraph`/`VisualEffectAsset` 处理）→ 两个转换器都是**全新增**，与现有导出器并列。

## 5. C# 架构方案
```
Editor/Export/
├─ shadergraph/
│   ├─ ShaderGraphParser.cs      # 多JSON解析(用JSONObject) + 索引 m_Nodes/m_Slots/m_Edges
│   ├─ SGNodeMapping.cs          # NODE_MAPPING 等映射表(从JS L~180-560搬)
│   └─ ShaderGraphConverter.cs   # 端口 unity-shader-to-laya.js → 产 .bps (JSONObject)
├─ vfx/
│   ├─ UnityYamlParser.cs        # 端口 parseEntries/parseRefIds (Unity YAML)
│   ├─ VfxGraphConverter.cs      # 端口 unity-vfx-to-laya.js → 产 .laya.vfx
│   └─ VfxPrefabVariantConverter.cs  # 端口 convert-uni-vfx.js prefab-variant/mesh-sync/tex-default/exposed-sync
```
- **输出**：走 `JSONObject` + `JsonFile`（.bps / .laya.vfx）。
- **触发方式（待用户最终定）**：候选 (A) 新增菜单"导出 VFX/ShaderGraph 资源"批量转（对应 JS convert-uni-vfx 工作流）；(B) 挂进组件导出（场景遇 VisualEffect 组件自动转，像 LayaParticleExportV2）。**倾向 A**（批量、对齐 JS 已验证流程）。

## 6. 审计结论：修复都在正式转换器里（临时脚本不用翻）
- `_patch_unimasked_alpha_swizzle`（MainTexture .a 通道）→ 已被本战役 **`_mapUnityOutputSlotToLaya` 的 `s.m_SlotId`→`s.m_Id` 修复**取代（重转 UNI-Masked 验证：sampler 用 p0+p4）。见 [[feedback_shader_converter_mslotid_channel_bug]]。
- `_patch_unimasked_unlit`（VFXTarget m_Lit:false→Unlit）→ 已在转换器 `_detectShaderType`（重转验证 Unlit_fragment + materialType=2）。
- `fix-circle-center`/`fix-orient-axes`/`add-scale-curve` 等 → 相关修复后来都正式进转换器。
- `_patch-vfx2-size`/`_patch_remove_pos_reset`/`_disable_*`/`patch-vfx1-mesh`/`revert/switch-vfx1` → 一次性实验/调试，非修复。
- `sync-texture-settings` → 改 `.png.meta` 资产设置，非转换逻辑（另一范畴）。

## 7. 分阶段推进计划
1. **阶段1（先做）**：蓝图转换器（`.shadergraph`→`.bps`）。最独立、输入是 JSON 好解析。里程碑 = 转出一个 `.bps` 与 JS 产物 **diff 对齐**（如 UNI_materialize / UNI-Masked）。
2. **阶段2**：VFX 主转换器（`.vfx`→`.laya.vfx`，含 UnityYamlParser）。里程碑 = base .vfx 产物 diff 对齐（如 UNI_abrupt_dm）。
3. **阶段3**：prefab-variant 编排器。里程碑 = 变体产物 diff 对齐（如 Abrupt DM Magenta / Smoke DM Black）。
- **验证方法**：每阶段 C# 产物 vs JS 产物做 JSON diff（键序/浮点格式可能有差异，比语义）。

## 8. 已修复要点（移植时确保 C# 也带上，都在 JS 正式转换器里）
- **蓝图**：`_mapUnityOutputSlotToLaya` 用 `s.m_Id`（非 m_SlotId）→ SampleTexture2D 各输出端口正确（smoothness读A/metallic读R/step读overlay.A）；Type=Normal 加 UnpackNormal 解码；`target.m_AlphaClip`→alpha-test；`--support-vfx` flag；VFXTarget m_Lit→unlit。
- **VFX**：materialize OverlayColor 复制 EdgeColor 的 ColorOverLife 渐变（避免烤常量白）；setPositionMesh 注入 meshScale（cm-unit mesh 缩放）；**getProperty 节点纵向铺开** uiData（避免编辑器堆叠，§14d, `y=uiPos.y+parameterExpanded*64`）。
- 相关 memory：[[feedback_shader_converter_mslotid_channel_bug]] [[feedback_getproperty_nodes_stacked_editor_disconnect]] [[feedback_mesh_filescale_100x_export]] [[feedback_materialize_oneminus_only_r_black_reveal]]。

## 9. 关键路径速查
- JS 源：`F:\git\LayaAir3.0\LayaVFXSample\{shadertools\unity-shader-to-laya.js, tools\unity-vfx-to-laya.js, tools\convert-uni-vfx.js}`
- Unity 源资产：`F:\Unity\projects\Universal3D\Assets\UNI VFX\**\*.{vfx,shadergraph,prefab}`
- 插件：`F:\Unity\projects\Universal3D\Assets\LayaAir3.0UnityPlugin`
- Laya 产物参考（已验证的正确输出，用于 diff）：`F:\git\LayaAir3.0\LayaVFXSample\assets\resources\univfx\**\*.{laya.vfx,bps}`
