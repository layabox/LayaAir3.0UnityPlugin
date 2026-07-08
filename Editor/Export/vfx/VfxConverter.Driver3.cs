using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LayaAir3.Converter
{
    /// <summary>
    /// VfxConverter 驱动第三部分：shader 属性绑定（walkSlotTree）+ block 分派（processChildBlock）
    /// + context props。移植自 unity-vfx-to-laya.js L3046-4697。常见路径忠实移植，罕见特例标 TODO。
    /// </summary>
    public partial class VfxConverter
    {
        // ── shader 属性绑定（对应 JS walkSlotTree L3046-3275） ──
        private void BuildShaderBinding(VfxEntry ctx, Jval props, string layaCtxType)
        {
            var bindings = Jval.Obj();
            var shaderPropertyDefaults = Jval.Obj();
            var shaderPropertyExpressions = Jval.Obj();
            var shaderBindingLinks = Jval.Obj();
            string perParticleColorProp = null;  // mesh输出的SG float属性连spawnIndex→走color.b自由通道(见下)
            var ctxInputSlots = UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots");

            Action<string> walkSlotTree = null;
            walkSlotTree = (slotID) =>
            {
                var slot = Get(slotID);
                if (slot == null) return;
                string propName = UnityYamlParser.GetSlotPropertyName(slot);
                if (propName != null && VfxMaps.SHADER_UNIFORM_RENAME.ContainsKey(propName)) propName = VfxMaps.SHADER_UNIFORM_RENAME[propName];
                bool isSystemSubcomp = propName != null && Regex.IsMatch(propName, "^[xyzwrgba]$", RegexOptions.IgnoreCase);
                if (propName != null && !isSystemSubcomp)
                {
                    var linked = UnityYamlParser.GetLinkedSlots(slot);
                    if (linked.Count > 0)
                    {
                        var linkedSlot = Get(linked[0]);
                        if (linkedSlot != null)
                        {
                            string ownerID = UnityYamlParser.GetRefField(linkedSlot.Body, "m_Owner");
                            var owner = ownerID != null ? Get(ownerID) : null;
                            if (owner != null && owner.ClassType == "VFXParameter")
                            {
                                string exposedName = UnityYamlParser.GetStringField(owner.Body, "m_ExposedName");
                                if (exposedName != null)
                                {
                                    bindings.Set(exposedName, propName);
                                    SlotRef gp;
                                    if (SlotToOpId.TryGetValue(linked[0], out gp)) shaderBindingLinks.Set(exposedName, gp.LayaId);
                                }
                                if (shaderPropertyExpressions.Get(propName) == null)
                                {
                                    var exGraph = Jval.Obj();
                                    string exRoot = BuildExpression(slotID, exGraph, ctx);
                                    var rootNode = exRoot != null ? exGraph.Get(exRoot) : null;
                                    var rdv = rootNode != null ? rootNode.Get("defaultValue") : null;
                                    bool isTexRes = rdv != null && rdv.IsObject && rdv.Get("obj") != null && (int)rdv.Get("obj").NumOf("fileID", -1) == 2800000;
                                    string slotTy = UnityYamlParser.GetSlotType(slot) ?? "";
                                    bool isResUniform = isTexRes || Regex.IsMatch(slotTy, @"UnityEngine\.(Texture|Mesh|Cubemap)", RegexOptions.IgnoreCase);
                                    if (exRoot != null && rootNode != null && !isResUniform)
                                        shaderPropertyExpressions.Set(propName, Jval.Obj().Set("rootNodeId", exRoot).Set("outputType", rootNode.Get("outputType")).Set("nodes", exGraph));
                                }
                            }
                            else if (owner != null && layaCtxType == "outputShaderGraphMesh" && UnityYamlParser.GetStringField(owner.Body, "attribute") == "spawnIndex")
                            {
                                // ⭐ mesh输出的SG float属性连到 getAttribute(spawnIndex)(如蜡烛 _Color_Index 逐蜡烛不同蜡池色):
                                //   uniform 逐draw无法逐粒子 → 标记走 color.b 自由通道。后处理 InjectPerParticleColorIndex
                                //   给该系统 init 注入 setAttribute(color,channels=4[B],SpawnIndex);.bps 侧把该属性 uniform 用法
                                //   换成 vertexColor.b(ConverterWindow PatchPerParticleColorShaders)。只一个自由通道→只承载一个此类属性。
                                perParticleColorProp = propName;
                            }
                            else if (owner != null)
                            {
                                var graphNodes = Jval.Obj();
                                string rootId = BuildExpression(slotID, graphNodes, ctx);
                                if (rootId != null)
                                {
                                    shaderPropertyExpressions.Set(propName, Jval.Obj().Set("rootNodeId", rootId).Set("outputType", graphNodes.Get(rootId).Get("outputType")).Set("nodes", graphNodes));
                                    int? termId = GetLayaIdForFileID(ownerID);
                                    if (termId != null) shaderBindingLinks.Set(propName, termId.Value);
                                }
                            }
                        }
                    }
                    else
                    {
                        // master 无 link：检测子分量 combine
                        var childIDs = UnityYamlParser.GetRefArrayField(slot.Body, "m_Children");
                        var childLinks = new List<Jval>();
                        for (int ci = 0; ci < childIDs.Count; ci++)
                        {
                            var cEntry = Get(childIDs[ci]);
                            if (cEntry == null) continue;
                            childLinks.Add(Jval.Obj().Set("idx", ci).Set("name", UnityYamlParser.GetSlotPropertyName(cEntry) ?? "")
                                .Set("slotID", childIDs[ci]).Set("linked", UnityYamlParser.GetLinkedSlots(cEntry).Count > 0));
                        }
                        bool anyChildLinked = false; foreach (var c in childLinks) if (c.BoolOf("linked")) { anyChildLinked = true; break; }
                        if (anyChildLinked && (childLinks.Count == 2 || childLinks.Count == 3 || childLinks.Count == 4))
                        {
                            var inlineMaster = UnityYamlParser.GetSlotInlineValue(slot);
                            var graphNodes = Jval.Obj();
                            var inputNodeIds = new List<string>();
                            var comboSrcIds = new List<int>();
                            bool broke = false;
                            foreach (var c in childLinks)
                            {
                                if (c.BoolOf("linked"))
                                {
                                    string childRootId = BuildExpression(c.StrOf("slotID"), graphNodes, ctx);
                                    if (childRootId == null) { inputNodeIds.Clear(); broke = true; break; }
                                    inputNodeIds.Add(childRootId);
                                    var cl = UnityYamlParser.GetLinkedSlots(Get(c.StrOf("slotID")));
                                    if (cl.Count > 0)
                                    {
                                        int? cid = null;
                                        SlotRef sr;
                                        if (SlotToOpId.TryGetValue(cl[0], out sr)) cid = sr.LayaId;
                                        if (cid == null)
                                        {
                                            var cls = Get(cl[0]);
                                            string masterID = cls != null ? (UnityYamlParser.GetSlotMaster(cls) ?? cl[0]) : null;
                                            var master = masterID != null ? Get(masterID) : null;
                                            string coid = master != null ? UnityYamlParser.GetRefField(master.Body, "m_Owner") : null;
                                            if (coid != null) cid = GetLayaIdForFileID(coid);
                                        }
                                        if (cid != null && !comboSrcIds.Contains(cid.Value)) comboSrcIds.Add(cid.Value);
                                    }
                                }
                                else
                                {
                                    double v = (inlineMaster != null && inlineMaster.IsObject && c.StrOf("name") != null && inlineMaster.Get(c.StrOf("name")) != null && inlineMaster.Get(c.StrOf("name")).IsNumber)
                                        ? inlineMaster.NumOf(c.StrOf("name")) : 0;
                                    string cid = "c" + graphNodes.Count;
                                    graphNodes.Set(cid, Jval.Obj().Set("kind", "Constant").Set("outputType", "float").Set("value", v).Set("slotName", c.StrOf("name")));
                                    inputNodeIds.Add(cid);
                                }
                            }
                            if (!broke && inputNodeIds.Count > 0)
                            {
                                int dim = inputNodeIds.Count;
                                string kind = dim == 2 ? "CombineVec2" : dim == 3 ? "CombineVec3" : "CombineVec4";
                                string outType = dim == 2 ? "vec2" : dim == 3 ? "vec3" : "vec4";
                                string rid = "n" + graphNodes.Count;
                                var inArr = Jval.Arr(); foreach (var nid in inputNodeIds) inArr.Push(Jval.Of(nid));
                                graphNodes.Set(rid, Jval.Obj().Set("kind", kind).Set("outputType", outType).Set("inputs", inArr));
                                shaderPropertyExpressions.Set(propName, Jval.Obj().Set("rootNodeId", rid).Set("outputType", outType).Set("nodes", graphNodes));
                                if (comboSrcIds.Count == 1) shaderBindingLinks.Set(propName, comboSrcIds[0]);
                                else if (comboSrcIds.Count > 1) { var a = Jval.Arr(); foreach (var x in comboSrcIds) a.Push(Jval.Of(x)); shaderBindingLinks.Set(propName, a); }
                                return;
                            }
                        }
                        // inline default
                        var inlineV = UnityYamlParser.GetSlotInlineValue(slot);
                        if (inlineV != null && inlineV.IsObject && inlineV.Get("obj") != null && inlineV.Get("obj").StrOf("guid") != null)
                        {
                            var resolved = ResolveResourceRef(inlineV);
                            if (resolved != null) shaderPropertyDefaults.Set(propName, resolved);
                        }
                        else
                        {
                            var cv = inlineV;
                            if (cv != null && cv.IsObject && cv.Get("vector") != null) cv = cv.Get("vector");
                            if (cv != null && cv.IsNumber)
                                shaderPropertyExpressions.Set(propName, Jval.Obj().Set("rootNodeId", "c0").Set("outputType", "float")
                                    .Set("nodes", Jval.Obj().Set("c0", MkConst(cv.Num, null))));
                            else if (cv != null && cv.IsBool)
                                shaderPropertyExpressions.Set(propName, Jval.Obj().Set("rootNodeId", "c0").Set("outputType", "float")
                                    .Set("nodes", Jval.Obj().Set("c0", MkConst(cv.Bool ? 1 : 0, null))));
                            else if (cv != null && cv.IsObject && cv.Get("x") != null && cv.Get("x").IsNumber)
                            {
                                var comps = new List<string>();
                                foreach (var k in new[] { "x", "y", "z", "w" }) if (cv.Get(k) != null && cv.Get(k).IsNumber) comps.Add(k);
                                if (comps.Count >= 2)
                                {
                                    var nodes = Jval.Obj(); var ids = new List<string>();
                                    for (int i = 0; i < comps.Count; i++) { string id = "c" + i; nodes.Set(id, MkConst(cv.NumOf(comps[i]), comps[i])); ids.Add(id); }
                                    int dim = ids.Count;
                                    string kind = dim == 2 ? "CombineVec2" : dim == 3 ? "CombineVec3" : "CombineVec4";
                                    string outType = dim == 2 ? "vec2" : dim == 3 ? "vec3" : "vec4";
                                    var inArr = Jval.Arr(); foreach (var id in ids) inArr.Push(Jval.Of(id));
                                    nodes.Set("n", Jval.Obj().Set("kind", kind).Set("outputType", outType).Set("inputs", inArr));
                                    shaderPropertyExpressions.Set(propName, Jval.Obj().Set("rootNodeId", "n").Set("outputType", outType).Set("nodes", nodes));
                                }
                            }
                        }
                    }
                }
                foreach (var childID in UnityYamlParser.GetRefArrayField(slot.Body, "m_Children")) walkSlotTree(childID);
            };
            foreach (var sID in ctxInputSlots) walkSlotTree(sID);

            if (perParticleColorProp != null) props.Set("_perParticleColorIndexProp", perParticleColorProp);
            if (bindings.Count > 0) props.Set("shaderPropertyBindings", bindings);
            if (shaderPropertyDefaults.Count > 0)
            {
                foreach (var k in new[] { "_MainTexture", "MainTexture", "_MainTex", "MainTex" })
                    if (shaderPropertyDefaults.Get(k) != null) shaderPropertyDefaults.Set(k, SwapMainRepeatVariant(shaderPropertyDefaults.StrOf(k)));
                props.Set("shaderPropertyDefaults", shaderPropertyDefaults);
            }
            if (shaderPropertyExpressions.Count > 0) props.Set("shaderPropertyExpressions", shaderPropertyExpressions);
            if (shaderBindingLinks.Count > 0) props.Set("shaderBindingLinks", shaderBindingLinks);
        }

        private static Jval MkConst(double val, string sn)
        {
            var o = Jval.Obj().Set("kind", "Constant").Set("outputType", "float").Set("value", val);
            if (sn != null) o.Set("slotName", sn);
            return o;
        }

        // ── block 分派（对应 JS processChildBlock L3277-4185，常见路径） ──
        // _curById：subgraph 展开时=sub.ById（JS processChildBlock 的 currentByID 参数）；null=主图。
        private Dictionary<string, VfxEntry> _curById;
        private VfxEntry GetCur(string id)
        {
            if (id == null) return null;
            if (_curById != null) { VfxEntry v; _curById.TryGetValue(id, out v); return v; }
            return Get(id);
        }
        private bool IsMainGraphNow { get { return _curById == null; } }
        private void ProcessChildBlock(VfxEntry childEntry, Jval layaCtx, string layaCtxType, Dictionary<string, VfxEntry> curById = null)
        {
            var prevCur = _curById;
            _curById = curById;
            try { ProcessChildBlockCore(childEntry, layaCtx, layaCtxType); }
            finally { _curById = prevCur; }
        }
        private void ProcessChildBlockCore(VfxEntry childEntry, Jval layaCtx, string layaCtxType)
        {
            // disabled 检测
            var dbg = Regex.Match(childEntry.Body, @"^\s*m_Disabled:\s*(\d+)", RegexOptions.Multiline);
            if (dbg.Success && dbg.Groups[1].Value == "1") return;
            var act = Regex.Match(childEntry.Body, @"^\s*m_ActivationSlot:\s*\{fileID:\s*(-?\d+)", RegexOptions.Multiline);
            if (act.Success && act.Groups[1].Value != "0")
            {
                var actSlot = GetCur(act.Groups[1].Value);
                if (actSlot != null) { var v = UnityYamlParser.GetSlotInlineValue(actSlot); if (v != null && v.IsBool && !v.Bool) return; }
            }

            // VFXSubgraphBlock（对应 JS L3299-3361）：vortex.vfxblock → 单 vortex block；其他 .vfxblock → 通用展开
            if (childEntry.ClassType == "VFXSubgraphBlock")
            {
                var sm = Regex.Match(childEntry.Body, @"^\s*m_Subgraph:\s*\{fileID:\s*-?\d+,\s*guid:\s*([a-f0-9]{32})", RegexOptions.Multiline);
                if (!sm.Success) return;
                string guid = sm.Groups[1].Value;
                var sub = LoadSubgraph(guid);
                // basename 判定（有真实文件路径时与 JS 一致）；无路径时回退名字表（旧测试路径兼容）
                string subBaseName = null;
                if (sub != null && sub.FilePath != null) subBaseName = System.IO.Path.GetFileName(sub.FilePath).ToLowerInvariant();
                else { string nm; if (SubgraphNameByGuid.TryGetValue(guid, out nm)) subBaseName = nm != null ? nm.ToLowerInvariant() : null; }
                if (subBaseName == "vortex.vfxblock")
                {
                    int vbId = NewId();
                    var vb = Jval.Obj().Set("id", vbId).Set("typeId", "vortex").Set("enabled", true).Set("props", Jval.Obj());
                    BuildVortexProps(childEntry, vb.Get("props"));
                    layaCtx.Get("blocks").Push(vb);
                    return;
                }
                if (sub == null) return;
                // 通用展开：subgraph root = VFXBlockSubgraphContext，其 children 递归 processChildBlock（用 sub.ById）
                VfxEntry subRoot = null;
                foreach (var e in sub.Entries) if (e.ClassType == "VFXBlockSubgraphContext") { subRoot = e; break; }
                if (subRoot == null) { Warn("[skip-block] subgraph " + guid + " 没找到 VFXBlockSubgraphContext"); return; }
                foreach (var subChildID in UnityYamlParser.GetRefArrayField(subRoot.Body, "m_Children"))
                {
                    VfxEntry subChild;
                    if (sub.ById.TryGetValue(subChildID, out subChild) && subChild != null)
                        ProcessChildBlock(subChild, layaCtx, layaCtxType, sub.ById);
                }
                return;
            }

            // unified PositionShape / CollisionShape
            string unifiedShapeType = null;
            if (childEntry.ClassType == "PositionShape")
            {
                int shapeIdx = UnityYamlParser.GetIntField(childEntry.Body, "shape") ?? 0;
                string[] SHAPE = { "Sphere", "Box", "Cone", "Torus", "Circle", "Line", "SDF" };
                string shapeName = (shapeIdx >= 0 && shapeIdx < SHAPE.Length) ? SHAPE[shapeIdx] : "Sphere";
                unifiedShapeType = shapeName == "SDF" ? "positionSDF" : "setPositionShape";
            }
            else if (childEntry.ClassType == "CollisionShape")
            {
                int shapeIdx = UnityYamlParser.GetIntField(childEntry.Body, "shape") ?? 0;
                string[] COL = { "collisionSphere", "collisionAABox", "collisionCone", "collisionPlane", "collisionSDF" };
                unifiedShapeType = (shapeIdx >= 0 && shapeIdx < COL.Length) ? COL[shapeIdx] : "collisionSphere";
            }
            string unifiedSpawnType = null;
            if (childEntry.ClassType == "VFXSpawnerBurst")
            {
                int repeat = UnityYamlParser.GetIntField(childEntry.Body, "repeat") ?? 0;
                unifiedSpawnType = repeat == 1 ? "periodicBurst" : "singleBurst";
            }

            string layaBlockType = unifiedShapeType ?? unifiedSpawnType;
            if (layaBlockType == null) VfxMaps.BLOCK_MAP.TryGetValue(childEntry.ClassType, out layaBlockType);
            if (layaBlockType == null) return;   // skip 未知 block

            // affinity 检查
            var affinity = Defs.GetAffinity(layaBlockType);
            var SG_ALIAS = new Dictionary<string, string> { { "outputShaderGraphMesh", "outputMesh" }, { "outputShaderGraphQuad", "outputBillboard" } };
            string effectiveCtxType; if (!SG_ALIAS.TryGetValue(layaCtxType, out effectiveCtxType)) effectiveCtxType = layaCtxType;
            if (affinity != null && Array.IndexOf(affinity, layaCtxType) < 0 && Array.IndexOf(affinity, effectiveCtxType) < 0)
            {
                var POSITION_INIT = new HashSet<string> { "setPositionShape", "positionSDF", "setPositionMesh", "positionSequential" };
                if (POSITION_INIT.Contains(layaBlockType) && Array.IndexOf(affinity, "initialize") >= 0) { /* 保留，RerouteePositionBlocks post-fix 会搬到 init */ }
                else if (layaBlockType == "setAttributeCurve") { /* 保留 */ }
                else return;
            }

            int blockId = NewId();
            var block = Jval.Obj().Set("id", blockId).Set("typeId", layaBlockType).Set("enabled", true).Set("props", Jval.Obj());
            var blocks = layaCtx.Get("blocks");

            if (layaBlockType == "setAttribute")
            {
                block.Set("props", ConvertSetAttribute(childEntry));
                var attr = block.Get("props").StrOf("attribute");
                // SkinnedMesh position（对应 JS L3434-3538）：TransformPosition→transformPosition 块 / Random→随机 Y 列
                if (attr == "position")
                {
                    var tpOp = FindLinkedOp(childEntry, "TransformPosition");
                    if (tpOp != null)
                    {
                        string transformName = null;
                        Action<string> findParam = null;
                        findParam = (sID) =>
                        {
                            if (transformName != null) return;
                            var s = GetCur(sID); if (s == null) return;
                            foreach (var lk in UnityYamlParser.GetLinkedSlots(s))
                            {
                                var src = GetCur(lk); string oID = src != null ? UnityYamlParser.GetSlotOwner(src) : null; var o = oID != null ? GetCur(oID) : null;
                                if (o != null && o.ClassType == "VFXParameter") { var en = UnityYamlParser.GetStringField(o.Body, "m_ExposedName"); if (en != null) { transformName = en; return; } }
                            }
                            foreach (var cID in UnityYamlParser.GetRefArrayField(s.Body, "m_Children")) findParam(cID);
                        };
                        foreach (var isID in UnityYamlParser.GetRefArrayField(tpOp.Body, "m_InputSlots")) { findParam(isID); if (transformName != null) break; }
                        if (transformName != null)
                        {
                            blocks.Push(Jval.Obj().Set("id", blockId).Set("typeId", "transformPosition").Set("enabled", true)
                                .Set("props", Jval.Obj().Set("transformSource", VfxHelpers.SanitizePropName(transformName))));
                            return;
                        }
                    }
                    var rndOp = FindLinkedOp(childEntry, "Random");
                    if (rndOp != null)
                    {
                        var rSlots = UnityYamlParser.GetRefArrayField(rndOp.Body, "m_InputSlots");
                        double? half = rSlots.Count > 1 && rSlots[1] != null ? ExtractHalfHM(rSlots[1]) : null;
                        if (half != null && half.Value != 0)
                        {
                            var vals = block.Get("props").Get("_values");
                            double fx = vals != null ? Nz1(vals, "x", 0) : 0;
                            double fz = vals != null ? Nz1(vals, "z", 0) : 0;
                            double h = Math.Abs(half.Value);
                            block.Get("props").Set("random", "Per Component").Set("channels", 7)
                                .Set("_values", Jval.Obj().Set("x", fx).Set("y", -h).Set("z", fz).Set("b_x", fx).Set("b_y", h).Set("b_z", fz));
                        }
                    }
                }
                // noop position-reset：无 upstream link 的全 0 Overwrite Slot → skip（常见，值得移植）
                if (attr == "position" && block.Get("props").StrOf("composition") == "Overwrite" && block.Get("props").StrOf("source") == "Slot")
                {
                    var vals = block.Get("props").Get("_values");
                    bool allZero = vals != null && ValZero(vals, "x") && ValZero(vals, "y") && ValZero(vals, "z")
                        && ValZero(vals, "b_x") && ValZero(vals, "b_y") && ValZero(vals, "b_z");
                    if (allZero)
                    {
                        var blockInputs = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
                        bool hasLinkedInput = blockInputs.Count > 0 && HasLinkedSlotRecCur(blockInputs[0]);
                        if (!hasLinkedInput) return;
                    }
                }
            }
            else if (layaBlockType == "setAttributeCurve") block.Set("props", ConvertAttributeFromCurve(childEntry));
            else if (layaBlockType == "orient") block.Set("props", ConvertOrient(childEntry));
            else if (childEntry.ClassType == "VFXSpawnerSetAttribute")
            {
                var p = block.Get("props");
                string attrName = VfxMaps.NormalizeAttrName(UnityYamlParser.GetStringField(childEntry.Body, "attribute") ?? "lifetime");
                p.Set("attribute", attrName);
                var sInputs = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
                var valSlot = sInputs.Count > 0 ? GetCur(sInputs[0]) : null;
                if (valSlot != null)
                {
                    var v = UnityYamlParser.GetSlotInlineValue(valSlot);
                    if (v != null && v.IsNumber) p.Set("value", v.Num);
                    else if (v != null && v.IsObject)
                    {
                        if (v.Get("x") != null && v.Get("x").IsNumber) p.Set("valueX", v.NumOf("x"));
                        if (v.Get("y") != null && v.Get("y").IsNumber) p.Set("valueY", v.NumOf("y"));
                        if (v.Get("z") != null && v.Get("z").IsNumber) p.Set("valueZ", v.NumOf("z"));
                        if (v.Get("w") != null && v.Get("w").IsNumber) p.Set("valueW", v.NumOf("w"));
                    }
                    // modulo(loopIndex, N) / Add(spawnState.loopDuration+delayAfterLoop) 特例（对应 JS L3601-3646，用全局 byID）
                    var linked = UnityYamlParser.GetLinkedSlots(valSlot);
                    if (linked.Count > 0)
                    {
                        var linkSlot = Get(linked[0]);
                        string linkOpID = linkSlot != null ? UnityYamlParser.GetRefField(linkSlot.Body, "m_Owner") : null;
                        var linkOp = linkOpID != null ? Get(linkOpID) : null;
                        if (linkOp != null && Regex.IsMatch(linkOp.ClassType, "Modulo", RegexOptions.IgnoreCase))
                        {
                            // modulo(a, b)：a 接 LoopIndex（Spawn State 输出），b 是常量
                            var modIns = UnityYamlParser.GetRefArrayField(linkOp.Body, "m_InputSlots");
                            var aSlot = modIns.Count > 0 && modIns[0] != null ? Get(modIns[0]) : null;
                            var bSlot = modIns.Count > 1 && modIns[1] != null ? Get(modIns[1]) : null;
                            var aLinked = aSlot != null ? UnityYamlParser.GetLinkedSlots(aSlot) : new List<string>();
                            var aSourceSlot = aLinked.Count > 0 && aLinked[0] != null ? Get(aLinked[0]) : null;
                            string aSourcePropName = aSourceSlot != null ? UnityYamlParser.GetSlotPropertyName(aSourceSlot) : null;
                            if (Regex.IsMatch(aSourcePropName ?? "", "LoopIndex", RegexOptions.IgnoreCase))
                            {
                                p.Set("useLoopIndex", true);
                                var bVal = bSlot != null ? UnityYamlParser.GetSlotInlineValue(bSlot) : null;
                                if (bVal != null && bVal.IsNumber && bVal.Num > 0) p.Set("loopIndexModulo", bVal.Num);
                            }
                        }
                        // Add(spawnState.loopDuration, spawnState.delayAfterLoop) → fromSpawnStateLoop（src.lifetime 由 spawn task 运行时算）
                        if (linkOp != null && Regex.IsMatch(linkOp.ClassType, @"^Add\b"))
                        {
                            var addIns = UnityYamlParser.GetRefArrayField(linkOp.Body, "m_InputSlots");
                            bool allFromSpawnState = true;
                            foreach (var aid in addIns)
                            {
                                var aS = Get(aid);
                                var aLinked2 = aS != null ? UnityYamlParser.GetLinkedSlots(aS) : new List<string>();
                                var aSrc = aLinked2.Count > 0 && aLinked2[0] != null ? Get(aLinked2[0]) : null;
                                string aPropName = aSrc != null ? UnityYamlParser.GetSlotPropertyName(aSrc) : null;
                                if (!Regex.IsMatch(aPropName ?? "", "loopDuration|delayAfterLoop|delayBeforeLoop", RegexOptions.IgnoreCase)) { allFromSpawnState = false; break; }
                            }
                            if (allFromSpawnState && attrName == "lifetime")
                            {
                                p.Set("fromSpawnStateLoop", true);
                                p.Set("value", 1);   // fallback 1s（runtime 用 spawnerState.loopDuration+delayAfterLoop 替换）
                            }
                        }
                    }
                }
            }
            else if (childEntry.ClassType == "VelocityDirection")
            {
                var p = block.Get("props");
                var vIns = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
                var dirSlot = vIns.Count > 0 ? GetCur(vIns[0]) : null;
                var speedSlot = vIns.Count > 1 ? GetCur(vIns[1]) : null;
                var blendSlot = vIns.Count > 2 ? GetCur(vIns[2]) : null;
                if (dirSlot != null)
                {
                    var v = UnityYamlParser.GetSlotInlineValue(dirSlot);
                    if (v != null && v.IsObject) { var kk = new List<string>(v.Keys); if (kk.Count == 1 && Regex.IsMatch(kk[0], "^(direction|vector)$", RegexOptions.IgnoreCase)) v = v.Get(kk[0]); }
                    if (v != null && v.IsObject) p.Set("direction", Jval.Obj().Set("x", Nz1(v, "x", 0)).Set("y", Nz1(v, "y", 1)).Set("z", Nz1(v, "z", 0)));
                }
                if (speedSlot != null) { var v = UnityYamlParser.GetSlotInlineValue(speedSlot); if (v != null && v.IsNumber) p.Set("speed", v.Num); }
                if (blendSlot != null) { var v = UnityYamlParser.GetSlotInlineValue(blendSlot); if (v != null && v.IsNumber) p.Set("blendDirection", v.Num); }
                if (p.Get("speedMode") == null) p.Set("speedMode", "Constant");
                if (p.Get("composition") == null) p.Set("composition", "Overwrite");
            }
            else if (VfxMaps.SPAWN_BLOCK_CLASSES.Contains(childEntry.ClassType))
                block.Set("props", ConvertSpawnBlock(childEntry.ClassType, childEntry));
            else if (childEntry.ClassType == "Gravity")
            {
                var p = block.Get("props");
                var gIns = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
                var gSlot = gIns.Count > 0 ? GetCur(gIns[0]) : null;
                if (gSlot != null)
                {
                    var v = UnityYamlParser.GetSlotInlineValue(gSlot);
                    if (v != null && v.IsObject) { var kk = new List<string>(v.Keys); if (kk.Count == 1 && Regex.IsMatch(kk[0], "^(direction|position|vector)$", RegexOptions.IgnoreCase)) v = v.Get(kk[0]); }
                    if (v != null && v.IsObject) p.Set("force", Jval.Obj().Set("x", Nz1(v, "x", 0)).Set("y", Nz1(v, "y", -9.81)).Set("z", Nz1(v, "z", 0)));
                    var sm = Regex.Match(gSlot.Body, @"m_Space:\s*(-?\d+)");
                    int spaceInt = sm.Success ? int.Parse(sm.Groups[1].Value) : -1;
                    p.Set("_space_force", spaceInt == 0 ? "Local" : "World");
                }
            }
            else if (childEntry.ClassType == "Drag")
            {
                var p = block.Get("props");
                foreach (var sID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
                {
                    var s = GetCur(sID); if (s == null) continue;
                    var pname = UnityYamlParser.GetSlotPropertyName(s) ?? "";
                    if (Regex.IsMatch(pname, "dragCoefficient", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("dragCoefficient", v.Num); }
                }
                var ups = UnityYamlParser.GetIntField(childEntry.Body, "useParticleSize");
                if (ups != null) p.Set("useParticleSize", ups.Value != 0);
            }
            else if (childEntry.ClassType == "VortexForceField" || childEntry.ClassType == "Vortex")
            {
                BuildVortexProps(childEntry, block.Get("props"));
            }
            else if (childEntry.ClassType == "ConformToSphere")
            {
                var p = block.Get("props");
                foreach (var sID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
                {
                    var s = GetCur(sID); if (s == null) continue;
                    var pname = UnityYamlParser.GetSlotPropertyName(s); if (pname == null) continue;
                    if (Regex.IsMatch(pname, "^Sphere$", RegexOptions.IgnoreCase))
                    {
                        Jval sphereData = null;
                        var linkedIDs = UnityYamlParser.GetLinkedSlots(s);
                        if (linkedIDs.Count > 0)
                        {
                            var linkSlot = Get(linkedIDs[0]);
                            string oID = linkSlot != null ? UnityYamlParser.GetRefField(linkSlot.Body, "m_Owner") : null;
                            var ownerOp = oID != null ? Get(oID) : null;
                            if (ownerOp != null)
                            {
                                foreach (var oid in UnityYamlParser.GetRefArrayField(ownerOp.Body, "m_InputSlots"))
                                {
                                    var os = Get(oid); if (os == null) continue;
                                    var v2 = UnityYamlParser.GetSlotInlineValue(os);
                                    if (v2 != null && v2.IsObject && (v2.Get("sphere") != null || v2.Get("center") != null || v2.Get("transform") != null)) { sphereData = v2.Get("sphere") ?? v2; break; }
                                }
                                if (sphereData == null) { var v2 = UnityYamlParser.GetSlotInlineValue(linkSlot); if (v2 != null && v2.IsObject) sphereData = v2.Get("sphere") ?? v2; }
                            }
                        }
                        if (sphereData == null) { var v2 = UnityYamlParser.GetSlotInlineValue(s); if (v2 != null && v2.IsObject) sphereData = v2.Get("sphere") ?? v2; }
                        if (sphereData != null && sphereData.IsObject)
                        {
                            var t = sphereData.Get("transform");
                            var pos = t != null ? t.Get("position") : sphereData.Get("center");
                            if (pos != null && pos.IsObject) p.Set("center", Vec3O(Nz1(pos, "x", 0), Nz1(pos, "y", 0), Nz1(pos, "z", 0)));
                            if (sphereData.Get("radius") != null && sphereData.Get("radius").IsNumber) p.Set("radius", sphereData.NumOf("radius"));
                        }
                    }
                    else if (Regex.IsMatch(pname, "^attractionSpeed$", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("attractionSpeed", v.Num); }
                    else if (Regex.IsMatch(pname, "^attractionForce$", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("attractionForce", v.Num); }
                    else if (Regex.IsMatch(pname, "^stickDistance$", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("stickDistance", v.Num); }
                    else if (Regex.IsMatch(pname, "^stickForce$", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("stickForce", v.Num); }
                }
            }
            else if (childEntry.ClassType == "Turbulence" || childEntry.ClassType == "Force")
            {
                var p = block.Get("props");
                int modeInt = UnityYamlParser.GetIntField(childEntry.Body, "Mode") ?? (childEntry.ClassType == "Turbulence" ? 1 : 0);
                p.Set("mode", modeInt == 1 ? "Relative" : "Absolute");
                if (childEntry.ClassType == "Turbulence")
                {
                    int nt = UnityYamlParser.GetIntField(childEntry.Body, "NoiseType") ?? 0;
                    string[] NT = { "Value", "Perlin", "Cellular" };
                    p.Set("noiseType", (nt >= 0 && nt < 3) ? NT[nt] : "Value");
                }
                foreach (var sID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
                {
                    var s = GetCur(sID); if (s == null) continue;
                    var pname = UnityYamlParser.GetSlotPropertyName(s); if (pname == null) continue;
                    if (pname == "Intensity") { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("intensity", v.Num); }
                    else if (Regex.IsMatch(pname, "^frequency|octaves|roughness|lacunarity$", RegexOptions.IgnoreCase)) { var v = UnityYamlParser.GetSlotInlineValue(s); if (v != null && v.IsNumber) p.Set(pname.ToLowerInvariant(), v.Num); }
                    else if (Regex.IsMatch(pname, "^Drag$", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("drag", v.Num); }
                    else if (Regex.IsMatch(pname, "^Force$", RegexOptions.IgnoreCase) && childEntry.ClassType == "Force")
                    {
                        var v = UnityYamlParser.GetSlotInlineValue(s);
                        if (v != null && v.IsObject) { var kk = new List<string>(v.Keys); if (kk.Count == 1 && Regex.IsMatch(kk[0], "^(direction|position|vector)$", RegexOptions.IgnoreCase)) v = v.Get(kk[0]); }
                        if (v != null && v.IsObject) p.Set("force", Vec3O(Nz1(v, "x", 0), Nz1(v, "y", 0), Nz1(v, "z", 0)));
                        var sm = Regex.Match(s.Body, @"m_Space:\s*(-?\d+)");
                        int spaceInt = sm.Success ? int.Parse(sm.Groups[1].Value) : -1;
                        p.Set("_space_force", spaceInt == 0 ? "Local" : "World");
                    }
                    else if (Regex.IsMatch(pname, "^FieldTransform$", RegexOptions.IgnoreCase))
                    {
                        var tv = UnityYamlParser.GetSlotInlineValue(s);
                        if (tv != null && tv.IsObject && (tv.Get("position") != null || tv.Get("scale") != null || tv.Get("angles") != null))
                        {
                            var tp = tv.Get("position"); var ta = tv.Get("angles"); var ts = tv.Get("scale");
                            p.Set("fieldTransform", Jval.Obj()
                                .Set("position", Vec3O(Nz1(tp, "x", 0), Nz1(tp, "y", 0), -Nz1(tp, "z", 0)))
                                .Set("angles", Vec3O(-Nz1(ta, "x", 0), -Nz1(ta, "y", 0), Nz1(ta, "z", 0)))
                                .Set("scale", Vec3O(Nz1(ts, "x", 1), Nz1(ts, "y", 1), Nz1(ts, "z", 1))));
                        }
                    }
                }
            }
            else if (childEntry.ClassType == "TriggerEvent" && layaBlockType == "triggerEvent")
            {
                var p = block.Get("props");
                int modeInt = UnityYamlParser.GetIntField(childEntry.Body, "mode") ?? 3;
                string[] MODE = { "Always", "OverTime", "OverDistance", "OnDie", "OnDie" };
                p.Set("eventType", (modeInt >= 0 && modeInt < MODE.Length) ? MODE[modeInt] : "OnDie");
                foreach (var slotID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
                {
                    var se = GetCur(slotID); if (se == null) continue;
                    // 主图：跟随 link 折常量读权威值；子图展开(sub map)：只能读 inline（对应 JS L3880）
                    var v = IsMainGraphNow ? ReadSpawnNumberSlot(slotID) : UnityYamlParser.GetSlotInlineValue(se);
                    if (v != null && v.IsNumber) { p.Set("param", v.Num); break; }
                }
                if (p.Get("param") == null) p.Set("param", 1);
            }
            else if (VfxMaps.POSITION_SHAPE_FROM_CLASS.ContainsKey(childEntry.ClassType))
            {
                block.Get("props").Set("shape", VfxMaps.POSITION_SHAPE_FROM_CLASS[childEntry.ClassType]).Set("positionMode", "Volume");
            }
            else if (childEntry.ClassType == "PositionShape" && layaBlockType == "setPositionShape")
            {
                BuildSetPositionShape(childEntry, block.Get("props"));
            }
            else if (childEntry.ClassType == "FlipbookPlay" && layaBlockType == "flipbookPlay")
            {
                var p = block.Get("props");
                int modeInt = UnityYamlParser.GetIntField(childEntry.Body, "mode") ?? 0;
                int frmInt = UnityYamlParser.GetIntField(childEntry.Body, "frameRateMode") ?? 0;
                string layaMode = "Constant";
                if (modeInt == 0) { if (frmInt == 2) layaMode = "OverLife"; else if (frmInt == 3) layaMode = "BySpeed"; }
                p.Set("mode", layaMode);
                foreach (var slotID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
                {
                    var se = GetCur(slotID); if (se == null) continue;
                    if (UnityYamlParser.GetSlotPropertyName(se) == "frameRate")
                    {
                        var v = UnityYamlParser.GetSlotInlineValue(se);
                        if (v != null && v.IsNumber) p.Set("frameRate", v.Num);
                        else if (v != null && v.IsObject && v.Get("x") != null && v.Get("x").IsNumber) p.Set("frameRate", v.NumOf("x"));
                        break;
                    }
                }
            }
            else if (childEntry.ClassType == "PositionSequential" && layaBlockType == "positionSequential")
            {
                BuildPositionSequential(childEntry, block.Get("props"));
            }
            else if (childEntry.ClassType == "PositionMesh" && layaBlockType == "setPositionMesh")
            {
                BuildPositionMesh(childEntry, block.Get("props"));
            }

            // block input slot 注册（op→block link 依赖）+ 资源类型 slot 写 props
            int ctxIdForBlock = (int)layaCtx.NumOf("id");
            var blockInputsAll = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
            for (int i = 0; i < blockInputsAll.Count; i++)
            {
                var s0 = GetCur(blockInputsAll[i]);
                string pn0 = s0 != null ? UnityYamlParser.GetSlotPropertyName(s0) : null;
                RecordBlockSlotTree(blockInputsAll[i], blockId, ctxIdForBlock, i, pn0);

                var slotEntry = GetCur(blockInputsAll[i]);
                if (slotEntry == null) continue;
                string slotType = UnityYamlParser.GetSlotType(slotEntry) ?? "";
                if (!Regex.IsMatch(slotType, "Mesh|Texture|Cubemap", RegexOptions.IgnoreCase)) continue;
                string propName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                if (propName == null) continue;
                var rv = UnityYamlParser.GetSlotInlineValue(slotEntry);
                var resolved = ResolveResourceRef(rv);
                if (resolved != null) block.Get("props").Set(char.ToLowerInvariant(propName[0]) + propName.Substring(1), resolved);
            }

            blocks.Push(block);
        }

        private void RecordBlockSlotTree(string sID, int blockId, int ctxId, int slotIndex, string slotPropName)
        {
            SlotToOpId[sID] = new SlotRef { BlockId = blockId, CtxId = ctxId, SlotIdx = slotIndex, SlotPropName = slotPropName };
            var sEntry = GetCur(sID);
            if (sEntry == null) return;
            foreach (var cID in UnityYamlParser.GetRefArrayField(sEntry.Body, "m_Children"))
            {
                var cEntry = GetCur(cID);
                string cPropName = cEntry != null ? UnityYamlParser.GetSlotPropertyName(cEntry) : null;
                string childSlotProp = (slotPropName != null && cPropName != null) ? slotPropName + "_" + cPropName : slotPropName;
                RecordBlockSlotTree(cID, blockId, ctxId, slotIndex, childSlotProp);
            }
        }

        private static double NzP(Jval o, string parent, string key, double dflt)
        {
            var p = o != null ? o.Get(parent) : null;
            return p != null ? Nz1(p, key, dflt) : dflt;
        }
        private static bool BoolLoose(Jval v)
        {
            if (v == null) return false;
            if (v.IsBool) return v.Bool;
            if (v.IsNumber) return v.Num == 1;
            if (v.IsString) { var s = v.Str; return s == "True" || s == "true"; }
            return false;
        }

        // setPositionShape 详细 props（对应 JS L3892-4000）
        private void BuildSetPositionShape(VfxEntry childEntry, Jval p)
        {
            int shapeIdx = UnityYamlParser.GetIntField(childEntry.Body, "shape") ?? 0;
            string[] SHAPE = { "Sphere", "Box", "Cone", "Torus", "Circle", "Line" };
            p.Set("shape", (shapeIdx >= 0 && shapeIdx < SHAPE.Length) ? SHAPE[shapeIdx] : "Sphere");
            int posModeIdx = UnityYamlParser.GetIntField(childEntry.Body, "positionMode") ?? 1;
            string[] POS = { "Surface", "Volume", "Thickness Absolute", "Thickness Relative" };
            p.Set("positionMode", (posModeIdx >= 0 && posModeIdx < POS.Length) ? POS[posModeIdx] : "Volume");
            string[] COMP = { "Overwrite", "Add", "Multiply", "Blend" };
            int cp = UnityYamlParser.GetIntField(childEntry.Body, "compositionPosition") ?? 0;
            int cd = UnityYamlParser.GetIntField(childEntry.Body, "compositionDirection") ?? 0;
            int ca = UnityYamlParser.GetIntField(childEntry.Body, "compositionAxes") ?? 0;
            p.Set("positionComposition", (cp >= 0 && cp < 4) ? COMP[cp] : "Overwrite");
            p.Set("directionComposition", (cd >= 0 && cd < 4) ? COMP[cd] : "Overwrite");
            p.Set("axesComposition", (ca >= 0 && ca < 4) ? COMP[ca] : "Overwrite");
            int spawnModeIdx = UnityYamlParser.GetIntField(childEntry.Body, "spawnMode") ?? 0;
            string layaSpawnMode = spawnModeIdx == 1 ? "Custom" : "Random";
            var seqInputs = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
            if (layaSpawnMode == "Custom")
            {
                bool hasSeqLink = false, upstreamIsRandom = false; double? perStripSeed = null;
                foreach (var sID in seqInputs)
                {
                    var s = GetCur(sID); if (s == null) continue;
                    var pname = UnityYamlParser.GetSlotPropertyName(s); if (pname == null) continue;
                    if (Regex.IsMatch(pname, "^(arcSequencer|heightSequencer|lineSequencer)$", RegexOptions.IgnoreCase))
                    {
                        var linked = UnityYamlParser.GetLinkedSlots(s);
                        if (linked.Count > 0)
                        {
                            hasSeqLink = true;
                            var linkedSlot = Get(linked[0]);
                            string loID = linkedSlot != null ? UnityYamlParser.GetRefField(linkedSlot.Body, "m_Owner") : null;
                            var linkedOp = loID != null ? Get(loID) : null;
                            if (linkedOp != null)
                            {
                                upstreamIsRandom = linkedOp.ClassType == "Random";
                                foreach (var opSID in UnityYamlParser.GetRefArrayField(linkedOp.Body, "m_InputSlots"))
                                {
                                    var opSlot = Get(opSID); if (opSlot == null) continue;
                                    if (UnityYamlParser.GetSlotPropertyName(opSlot) == "seed") { var v = UnityYamlParser.GetSlotInlineValue(opSlot); if (v != null && v.IsNumber) perStripSeed = v.Num; break; }
                                }
                            }
                            break;
                        }
                    }
                }
                if (hasSeqLink && upstreamIsRandom) { p.Set("_perStripRand", true); if (perStripSeed != null) p.Set("_perStripSeed", perStripSeed.Value); }
            }
            p.Set("spawnMode", layaSpawnMode);
            var SHAPE_PROP = new Dictionary<string, string> {
                {"line","line"},{"circle","arcCircle"},{"sphere","arcSphere"},{"box","orientedBox"},{"cone","arcCone"},{"torus","arcTorus"},
                {"arccircle","arcCircle"},{"arcsphere","arcSphere"},{"arccone","arcCone"},{"arctorus","arcTorus"},{"orientedbox","orientedBox"},
            };
            foreach (var slotID in seqInputs)
            {
                var slotEntry = GetCur(slotID); if (slotEntry == null) continue;
                string propName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                string layaPropName = null;
                if (propName != null) SHAPE_PROP.TryGetValue(propName.ToLowerInvariant(), out layaPropName);
                if (layaPropName == null) continue;
                var v = UnityYamlParser.GetSlotInlineValue(slotEntry);
                // ⭐ 形状复合值(line/circle/sphere...)可能连线到算子输出(如 "Line Position" inline operator)：
                //    输入槽的 m_Value 是过期默认(如旧 Y=0 线)，真值在被连输出槽的 m_MasterData.m_Value。
                //    跟随 m_LinkedSlots 读输出槽真值(对应 [[feedback_inline_curve_output_first]]：output 优先于 input 默认)。
                var _shapeLinks = UnityYamlParser.GetLinkedSlots(slotEntry);
                if (_shapeLinks.Count > 0)
                {
                    var _outSlot = GetCur(_shapeLinks[0]);
                    if (_outSlot != null)
                    {
                        var _lv = UnityYamlParser.GetSlotInlineValue(_outSlot);
                        if (_lv != null && _lv.IsObject) v = _lv;
                    }
                }
                if (v != null && v.IsObject)
                {
                    UnityYamlParser.ConvertUnityToLayaHandedness(v);
                    if (v.Get("angles") != null && v.Get("angle") == null) v.Set("angle", v.Get("angles"));
                    p.Set(layaPropName, v);
                }
            }
        }

        // positionSequential 详细 props（对应 JS L4028-4079）
        private void BuildPositionSequential(VfxEntry childEntry, Jval p)
        {
            int shapeIdx = UnityYamlParser.GetIntField(childEntry.Body, "shape") ?? 0;
            string[] SHAPE = { "Line", "Circle", "ThreeDimensional" };
            p.Set("mode", (shapeIdx >= 0 && shapeIdx < SHAPE.Length) ? SHAPE[shapeIdx] : "Line");
            int compIdx = UnityYamlParser.GetIntField(childEntry.Body, "compositionPosition") ?? 0;
            p.Set("composition", compIdx == 1 ? "Add" : "Overwrite");
            var stepXYZ = Jval.Obj().Set("x", 1).Set("y", 1).Set("z", 1);
            foreach (var sID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
            {
                var sEntry = GetCur(sID); if (sEntry == null) continue;
                string propName = UnityYamlParser.GetSlotPropertyName(sEntry); if (propName == null) continue;
                var v = UnityYamlParser.GetSlotInlineValue(sEntry);
                Jval inner = v;
                if (v != null && v.IsObject) { var kk = new List<string>(v.Keys); if (kk.Count == 1 && Regex.IsMatch(kk[0], "^(position|direction|vector)$", RegexOptions.IgnoreCase)) inner = v.Get(kk[0]); }
                Func<double> numFromLinkOrInline = () =>
                {
                    if (UnityYamlParser.GetLinkedSlots(sEntry).Count > 0) { var e = EvalConstOperatorFromInputSlot(sID); if (e != null) return e.Value; }
                    return (v != null && v.IsNumber) ? v.Num : 1;
                };
                switch (propName)
                {
                    case "CountX": p.Set("countX", numFromLinkOrInline()); break;
                    case "CountY": p.Set("countY", numFromLinkOrInline()); break;
                    case "CountZ": p.Set("countZ", numFromLinkOrInline()); break;
                    case "Count": p.Set("countPerLine", numFromLinkOrInline()); break;
                    case "Origin": if (inner != null && inner.IsObject) p.Set("origin", inner); break;
                    case "StepX": if (v != null && v.IsNumber) stepXYZ.Set("x", v.Num); break;
                    case "StepY": if (v != null && v.IsNumber) stepXYZ.Set("y", v.Num); break;
                    case "StepZ": if (v != null && v.IsNumber) stepXYZ.Set("z", v.Num); break;
                    case "Start": if (inner != null && inner.IsObject) p.Set("start", inner); break;
                    case "End": if (inner != null && inner.IsObject) p.Set("end", inner); break;
                    case "Center": if (inner != null && inner.IsObject) p.Set("center", inner); break;
                    case "Radius": if (v != null && v.IsNumber) p.Set("radius", v.Num); break;
                    case "Axis": if (inner != null && inner.IsObject) p.Set("axis", inner); break;
                    case "Normal": if (inner != null && inner.IsObject) p.Set("axis", inner); break;
                }
            }
            if (stepXYZ.NumOf("x") != 1 || stepXYZ.NumOf("y") != 1 || stepXYZ.NumOf("z") != 1) p.Set("stepSize", stepXYZ);
        }

        // setPositionMesh 详细 props（对应 JS L4080-4141）
        private void BuildPositionMesh(VfxEntry childEntry, Jval p)
        {
            int sourceMeshIdx = UnityYamlParser.GetIntField(childEntry.Body, "sourceMesh") ?? 0;
            int placementIdx = UnityYamlParser.GetIntField(childEntry.Body, "placementMode") ?? 0;
            string[] PLACE = { "Vertex", "Edge", "Surface" };
            p.Set("sampleMode", (placementIdx >= 0 && placementIdx < PLACE.Length) ? PLACE[placementIdx] : "Vertex");
            var blockInputs = UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots");
            if (sourceMeshIdx == 1)
            {
                var skinnedSlot = blockInputs.Count > 0 ? GetCur(blockInputs[0]) : null;
                string exposedName = UnityYamlParser.ResolveExposedNameForInputSlot(skinnedSlot, GetCur);
                if (exposedName != null)
                {
                    string safeName = Regex.Replace(exposedName, "[^A-Za-z0-9_]", "_"); if (safeName == "") safeName = "default";
                    p.Set("skinnedMeshSource", safeName).Set("skinnedMeshExposedName", exposedName);
                }
                else { p.Set("skinnedMeshSource", "New_SkinnedMeshRenderer").Set("skinnedMeshExposedName", "New SkinnedMeshRenderer"); }
            }
            else
            {
                Jval meshObj = null;
                if (blockInputs.Count > 0)
                {
                    var ms = GetCur(blockInputs[0]);
                    var v = ms != null ? UnityYamlParser.GetSlotInlineValue(ms) : null;
                    if (v != null && v.Get("obj") != null) meshObj = v.Get("obj");
                    if (meshObj == null && ms != null)
                    {
                        var lk = UnityYamlParser.GetLinkedSlots(ms);
                        if (lk.Count > 0)
                        {
                            var ls = GetCur(lk[0]);
                            string oID = ls != null ? UnityYamlParser.GetRefField(ls.Body, "m_Owner") : null;
                            var owner = oID != null ? GetCur(oID) : null;
                            if (owner != null) { var pv = UnityYamlParser.GetSlotInlineValue(owner); if (pv != null && pv.Get("obj") != null) meshObj = pv.Get("obj"); }
                        }
                    }
                }
                if (meshObj != null && Regex.IsMatch(meshObj.StrOf("guid") ?? "", "^0{16}e0{15}$"))
                {
                    var BUILTIN = new Dictionary<int, string> { { 10202, "Cube" }, { 10206, "Cylinder" }, { 10207, "Sphere" }, { 10208, "Capsule" }, { 10209, "Plane" }, { 10210, "Quad" } };
                    string nm; if (BUILTIN.TryGetValue((int)meshObj.NumOf("fileID"), out nm)) p.Set("mesh", "builtin:" + nm);
                }
                else if (meshObj != null && meshObj.StrOf("guid") != null) p.Set("mesh", meshObj.StrOf("guid") + "@lm0");
            }
        }

        private static bool ValZero(Jval vals, string k) { var v = vals.Get(k); return v == null || (v.IsNumber && v.Num == 0); }

        // 在 block 的 input slot 树里找 linked 到 classType 算子的 owner（对应 JS findLinkedOp）
        private VfxEntry FindLinkedOp(VfxEntry childEntry, string classType)
        {
            VfxEntry found = null;
            Action<string> walk = null;
            walk = (sID) =>
            {
                if (found != null) return;
                var s = GetCur(sID); if (s == null) return;
                foreach (var lk in UnityYamlParser.GetLinkedSlots(s))
                {
                    var src = GetCur(lk); string oID = src != null ? UnityYamlParser.GetSlotOwner(src) : null; var o = oID != null ? GetCur(oID) : null;
                    if (o != null && o.ClassType == classType) { found = o; return; }
                }
                foreach (var cID in UnityYamlParser.GetRefArrayField(s.Body, "m_Children")) walk(cID);
            };
            foreach (var sID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots")) { walk(sID); if (found != null) break; }
            return found;
        }

        // 在槽树下找 Multiply(VFXParameter h, inline m) → 返回 h×m（对应 JS extractHM）
        private double? ExtractHalfHM(string slotID)
        {
            double? h = null, m = null;
            Action<string, int> walk = null;
            walk = (sID, depth) =>
            {
                if (depth > 6) return;
                var s = GetCur(sID); if (s == null) return;
                foreach (var lk in UnityYamlParser.GetLinkedSlots(s))
                {
                    var src = GetCur(lk); string oID = src != null ? UnityYamlParser.GetSlotOwner(src) : null; var o = oID != null ? GetCur(oID) : null;
                    if (o == null) continue;
                    if (o.ClassType == "Multiply")
                    {
                        foreach (var mi in UnityYamlParser.GetRefArrayField(o.Body, "m_InputSlots"))
                        {
                            var ms = GetCur(mi); if (ms == null) continue;
                            var mlk = UnityYamlParser.GetLinkedSlots(ms);
                            if (mlk.Count == 0) { var iv = UnityYamlParser.GetSlotInlineValue(ms); if (iv.IsNumber && iv.Num != 0) m = iv.Num; }
                            else
                            {
                                var msrc = GetCur(mlk[0]); string moID = msrc != null ? UnityYamlParser.GetSlotOwner(msrc) : null; var mo = moID != null ? GetCur(moID) : null;
                                if (mo != null && mo.ClassType == "VFXParameter") { var hv = UnityYamlParser.GetSlotInlineValue(msrc); if (hv.IsNumber) h = hv.Num; }
                            }
                        }
                    }
                    foreach (var isID in UnityYamlParser.GetRefArrayField(o.Body, "m_InputSlots")) walk(isID, depth + 1);
                }
                foreach (var cID in UnityYamlParser.GetRefArrayField(s.Body, "m_Children")) walk(cID, depth + 1);
            };
            walk(slotID, 0);
            return (h != null && m != null) ? (double?)(h.Value * m.Value) : null;
        }

        // ── context props（对应 JS L4194-4697，常见 ctx 类型） ──
        private void BuildContextProps(VfxEntry ctx, Jval layaCtx, Jval props, string layaCtxType)
        {
            if (ctx.ClassType == "VFXBasicSpawner")
            {
                int dMode = UnityYamlParser.GetIntField(ctx.Body, "loopDuration") ?? 0;
                int cMode = UnityYamlParser.GetIntField(ctx.Body, "loopCount") ?? 0;
                string[] MODE = { "Infinite", "Constant", "Random" };
                var slotByName = new Dictionary<string, string>();
                foreach (var sID in UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots"))
                {
                    var se = Get(sID); if (se == null) continue;
                    var pn = UnityYamlParser.GetSlotPropertyName(se); if (pn != null) slotByName[pn] = sID;
                }
                Func<string, double?> readVal = (name) =>
                {
                    string sID; if (!slotByName.TryGetValue(name, out sID)) return null;
                    var s = Get(sID); if (s == null) return null;
                    if (UnityYamlParser.GetLinkedSlots(s).Count > 0) { var f = EvalConstOperatorFromInputSlot(sID); if (f != null) return f; }
                    var v = UnityYamlParser.GetSlotInlineValue(s); return v.IsNumber ? (double?)v.Num : null;
                };
                Func<string, Jval> readVec2 = (name) =>
                {
                    string sID; if (!slotByName.TryGetValue(name, out sID)) return null;
                    var s = Get(sID); if (s == null) return null;
                    var v = UnityYamlParser.GetSlotInlineValue(s);
                    if (v != null && v.IsObject && (v.Get("x") != null || v.Get("y") != null)) return Jval.Obj().Set("x", Nz1(v, "x", 0)).Set("y", Nz1(v, "y", 0));
                    return null;
                };
                string durationMode = (dMode >= 0 && dMode < 3) ? MODE[dMode] : "Infinite";
                string countMode = (cMode >= 0 && cMode < 3) ? MODE[cMode] : "Infinite";
                double loopDuration = dMode == 0 ? -1 : 1;
                Jval loopDurationRange = Jval.Obj().Set("x", 1).Set("y", 3);
                if (dMode == 1) { var v = readVal("LoopDuration"); if (v != null) loopDuration = v.Value; }
                else if (dMode == 2) { var r = readVec2("LoopDurationRange"); if (r != null) loopDurationRange = r; }
                double loopCount = cMode == 0 ? -1 : 1;
                Jval loopCountRange = Jval.Obj().Set("x", 1).Set("y", 3);
                if (cMode == 1) { var v = readVal("LoopCount"); if (v != null) loopCount = Math.Max(1, Math.Round(v.Value)); }
                else if (cMode == 2) { var r = readVec2("LoopCountRange"); if (r != null) loopCountRange = r; }
                int delayBeforeMode = UnityYamlParser.GetIntField(ctx.Body, "delayBeforeLoop") ?? 0;
                int delayAfterMode = UnityYamlParser.GetIntField(ctx.Body, "delayAfterLoop") ?? 0;
                double delayBeforeLoop = 0, delayAfterLoop = 0;
                if (delayBeforeMode > 0) { var v = readVal("DelayBeforeLoop"); if (v != null) delayBeforeLoop = v.Value; }
                if (delayAfterMode > 0) { var v = readVal("DelayAfterLoop"); if (v != null) delayAfterLoop = v.Value; }
                layaCtx.Set("props", Jval.Obj()
                    .Set("durationMode", durationMode).Set("loopDuration", loopDuration).Set("loopDurationRange", loopDurationRange)
                    .Set("countMode", countMode).Set("loopCount", loopCount).Set("loopCountRange", loopCountRange)
                    .Set("delayBeforeLoop", delayBeforeLoop).Set("delayAfterLoop", delayAfterLoop));
            }
            else if (ctx.ClassType == "VFXBasicInitialize")
            {
                string dataRef = UnityYamlParser.GetRefField(ctx.Body, "m_Data");
                double capacity = 256;
                if (dataRef != null) { var de = Get(dataRef); if (de != null) { var cap = UnityYamlParser.GetIntField(de.Body, "capacity"); if (cap != null) capacity = cap.Value; } }
                var p = Jval.Obj().Set("space", "Local").Set("capacity", capacity).Set("boundsMode", "Manual")
                    .Set("boundsCenter", Jval.Obj().Set("x", 0).Set("y", 0).Set("z", 0))
                    .Set("boundsSize", Jval.Obj().Set("x", 4).Set("y", 4).Set("z", 4));
                layaCtx.Set("props", p);
                // initialize bounds shaderBindingLinks（对应 JS L4302-4321）
                var initBlinks = Jval.Obj();
                Action<string> collectInitLink = null;
                collectInitLink = (slotID) =>
                {
                    var slot = Get(slotID); if (slot == null) return;
                    var linked = UnityYamlParser.GetLinkedSlots(slot);
                    if (linked.Count > 0)
                    {
                        SlotRef t;
                        if (SlotToOpId.TryGetValue(linked[0], out t) && t.LayaId != 0)
                        {
                            var up = Get(linked[0]);
                            string ownerID = up != null ? UnityYamlParser.GetRefField(up.Body, "m_Owner") : null;
                            var owner = ownerID != null ? Get(ownerID) : null;
                            string exposedName = owner != null ? UnityYamlParser.GetStringField(owner.Body, "m_ExposedName") : null;
                            string key = exposedName != null ? VfxHelpers.SanitizePropName(exposedName) : ("input_" + slotID);
                            initBlinks.Set(key, t.LayaId);
                        }
                    }
                    foreach (var c in UnityYamlParser.GetRefArrayField(slot.Body, "m_Children")) collectInitLink(c);
                };
                foreach (var sID in UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots")) collectInitLink(sID);
                if (initBlinks.Count > 0) p.Set("shaderBindingLinks", initBlinks);
            }
            else if (ctx.ClassType == "VFXBasicUpdate")
            {
                layaCtx.Set("props", Jval.Obj().Set("updatePosition", true).Set("ageParticles", true).Set("reapParticles", true).Set("skipZeroDeltaTime", false));
            }
            else if (ctx.ClassType == "VFXPlanarPrimitiveOutput" || ctx.ClassType == "VFXURPLitPlanarPrimitiveOutput" || ctx.ClassType == "VFXLitPlanarPrimitiveOutput")
            {
                BuildPlanarOutputProps(ctx, props);
            }
            // outputTrail(strip) 详细 props（对应 JS L4393-4530）
            if (layaCtxType == "outputTrail") BuildOutputTrailProps(ctx, props);

            // mainTexture / mesh / SG blendMode（对应 JS L4536-4633），所有 output ctx 类型
            if (Regex.IsMatch(layaCtxType, "^output(Billboard|Mesh|StaticMesh|ComposedParticle|Cube|Trail|ShaderGraphMesh|ShaderGraphQuad)$"))
                BuildOutputTextureMesh(ctx, layaCtx, props, layaCtxType);
        }

        private void BuildOutputTrailProps(VfxEntry ctx, Jval props)
        {
            string dataRef = UnityYamlParser.GetRefField(ctx.Body, "m_Data");
            double stripCapacity = 1, particlePerStripCount = 128;
            if (dataRef != null)
            {
                var de = Get(dataRef);
                if (de != null)
                {
                    var sc = UnityYamlParser.GetIntField(de.Body, "stripCapacity");
                    var ppsc = UnityYamlParser.GetIntField(de.Body, "particlePerStripCount");
                    if (sc != null && sc.Value > 0) stripCapacity = sc.Value;
                    if (ppsc != null && ppsc.Value > 0) particlePerStripCount = ppsc.Value;
                }
            }
            var blendModeInt = UnityYamlParser.GetIntField(ctx.Body, "blendMode");
            int colorMappingInt = UnityYamlParser.GetIntField(ctx.Body, "colorMapping") ?? 0;
            string[] COLOR_MAP = { "Default", "GradientMapped" };
            int uvModeInt = UnityYamlParser.GetIntField(ctx.Body, "uvMode") ?? 0;
            string[] UV_MODE = { "Default", "Flipbook", "FlipbookBlend", "ScaleAndBias", "ScaleAndBiasRandom" };
            int tilingModeInt = UnityYamlParser.GetIntField(ctx.Body, "tilingMode") ?? 0;
            string[] TILING = { "Stretch", "RepeatPerSegment", "Custom" };
            bool useSoftParticle = (UnityYamlParser.GetIntField(ctx.Body, "useSoftParticle") ?? 0) != 0;
            bool swapUV = (UnityYamlParser.GetIntField(ctx.Body, "swapUV") ?? 0) != 0;
            var ctxIns = UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots");
            double softParticleFade = 0;
            Jval uvScale = null, uvBias = null, gradientInline = null, uvBiasScrollX = null, uvBiasScrollY = null;
            foreach (var sID in ctxIns)
            {
                var s = Get(sID); if (s == null) continue;
                string pname = UnityYamlParser.GetSlotPropertyName(s); if (pname == null) continue;
                if (pname == "softParticleFadeDistance" && useSoftParticle) { var v = UnityYamlParser.GetSlotInlineValue(s); if (v != null && v.IsNumber) softParticleFade = v.Num; }
                else if (pname == "uvScale")
                {
                    var v = Unwrap2(UnityYamlParser.GetSlotInlineValue(s));
                    if (v != null && v.IsObject) uvScale = Jval.Obj().Set("x", Nz1(v, "x", 1)).Set("y", Nz1(v, "y", 1));
                }
                else if (pname == "uvBias")
                {
                    var v = Unwrap2(UnityYamlParser.GetSlotInlineValue(s));
                    if (v != null && v.IsObject) uvBias = Jval.Obj().Set("x", Nz1(v, "x", 0)).Set("y", Nz1(v, "y", 0));
                    // dynamic UV scroll 检测：uvBias.x/y linked add(TotalTime, Random)
                    var childIDs = UnityYamlParser.GetRefArrayField(s.Body, "m_Children");
                    for (int ci = 0; ci < childIDs.Count; ci++)
                    {
                        string childAxis = ci == 0 ? "x" : ci == 1 ? "y" : null;
                        if (childAxis == null) break;
                        var childSlot = Get(childIDs[ci]); if (childSlot == null) continue;
                        var linked = UnityYamlParser.GetLinkedSlots(childSlot);
                        if (linked.Count == 0) continue;
                        var linkSlot = Get(linked[0]);
                        string linkOpID = linkSlot != null ? UnityYamlParser.GetRefField(linkSlot.Body, "m_Owner") : null;
                        var linkOp = linkOpID != null ? Get(linkOpID) : null;
                        if (linkOp != null && Regex.IsMatch(linkOp.ClassType ?? "", @"^Add\b"))
                        {
                            double scrollSpeed = 0, randomMin = 0, randomMax = 0;
                            foreach (var aid in UnityYamlParser.GetRefArrayField(linkOp.Body, "m_InputSlots"))
                            {
                                var asl = Get(aid); if (asl == null) continue;
                                var aLinked = UnityYamlParser.GetLinkedSlots(asl);
                                if (aLinked.Count == 0) continue;
                                var aLinkSlot = Get(aLinked[0]);
                                string aLinkOpID = aLinkSlot != null ? UnityYamlParser.GetRefField(aLinkSlot.Body, "m_Owner") : null;
                                var aLinkOp = aLinkOpID != null ? Get(aLinkOpID) : null;
                                if (aLinkOp == null) continue;
                                if (Regex.IsMatch(aLinkOp.ClassType ?? "", "TotalTime|TimeBuiltIn|VFXTotalTime", RegexOptions.IgnoreCase) || Regex.IsMatch(aLinkOp.Body ?? "", "a72fbb93", RegexOptions.IgnoreCase)) scrollSpeed = 1;
                                if (Regex.IsMatch(aLinkOp.ClassType ?? "", "VFXDynamicBuiltInParameter", RegexOptions.IgnoreCase)) scrollSpeed = 1;
                                if (Regex.IsMatch(aLinkOp.ClassType ?? "", @"^Random\b"))
                                {
                                    var rIns = UnityYamlParser.GetRefArrayField(aLinkOp.Body, "m_InputSlots");
                                    if (rIns.Count > 0) { var rmin = UnityYamlParser.GetSlotInlineValue(Get(rIns[0])); if (rmin != null && rmin.IsNumber) randomMin = rmin.Num; }
                                    if (rIns.Count > 1) { var rmax = UnityYamlParser.GetSlotInlineValue(Get(rIns[1])); if (rmax != null && rmax.IsNumber) randomMax = rmax.Num; }
                                }
                            }
                            if (scrollSpeed > 0)
                            {
                                var scroll = Jval.Obj().Set("speed", scrollSpeed).Set("randomMin", randomMin).Set("randomMax", randomMax);
                                if (childAxis == "x") uvBiasScrollX = scroll; else if (childAxis == "y") uvBiasScrollY = scroll;
                            }
                        }
                    }
                }
                else if (pname == "gradient")
                {
                    string slotType = UnityYamlParser.GetSlotType(s) ?? "";
                    if (Regex.IsMatch(slotType, "Gradient", RegexOptions.IgnoreCase))
                    {
                        var g = UnityYamlParser.GetSlotInlineValue(s);
                        if (g != null && g.IsObject && g.Get("colorKeys") != null && g.Get("colorKeys").IsArray)
                        {
                            var ak = g.Get("alphaKeys");
                            if (ak == null || !ak.IsArray) ak = Jval.Arr(Jval.Obj().Set("alpha", 1).Set("time", 0), Jval.Obj().Set("alpha", 1).Set("time", 1));
                            gradientInline = Jval.Obj().Set("colorKeys", g.Get("colorKeys")).Set("alphaKeys", ak);
                        }
                    }
                }
            }
            props.Set("blendMode", VfxHelpers.UnityBlendModeToLaya(blendModeInt ?? -99));
            props.Set("stripCapacity", stripCapacity);
            props.Set("particlePerStripCount", particlePerStripCount);
            props.Set("colorMapping", (colorMappingInt >= 0 && colorMappingInt < COLOR_MAP.Length) ? COLOR_MAP[colorMappingInt] : "Default");
            props.Set("uvMode", (uvModeInt >= 0 && uvModeInt < UV_MODE.Length) ? UV_MODE[uvModeInt] : "Default");
            props.Set("tilingMode", (tilingModeInt >= 0 && tilingModeInt < TILING.Length) ? TILING[tilingModeInt] : "Stretch");
            props.Set("useSoftParticle", useSoftParticle);
            props.Set("softParticleFade", softParticleFade);
            props.Set("swapUV", swapUV);
            if (uvScale != null) props.Set("uvScale", uvScale);
            if (uvBias != null) props.Set("uvBias", uvBias);
            if (gradientInline != null) props.Set("gradient", gradientInline);
            if (uvBiasScrollX != null) props.Set("uvBiasScrollX", uvBiasScrollX);
            if (uvBiasScrollY != null) props.Set("uvBiasScrollY", uvBiasScrollY);
        }

        // subgraph noop-check 变体：在当前图(主图或 sub map)里查 slot（JS checkLinked 用 currentByID）
        private bool HasLinkedSlotRecCur(string slotID)
        {
            var sE = GetCur(slotID); if (sE == null) return false;
            if (UnityYamlParser.GetLinkedSlots(sE).Count > 0) return true;
            foreach (var cid in UnityYamlParser.GetRefArrayField(sE.Body, "m_Children")) if (HasLinkedSlotRecCur(cid)) return true;
            return false;
        }

        private void BuildVortexProps(VfxEntry childEntry, Jval p)
        {
            foreach (var sID in UnityYamlParser.GetRefArrayField(childEntry.Body, "m_InputSlots"))
            {
                var s = GetCur(sID); if (s == null) continue;
                var pname = UnityYamlParser.GetSlotPropertyName(s); if (pname == null) continue;
                if (Regex.IsMatch(pname, "^Vortex Plane|^Plane$", RegexOptions.IgnoreCase))
                {
                    var v = UnityYamlParser.GetSlotInlineValue(s);
                    if (v != null && v.IsObject)
                        p.Set("vortexPlane", Jval.Obj()
                            .Set("position", Vec3O(NzP(v, "position", "x", 0), NzP(v, "position", "y", 0), NzP(v, "position", "z", 0)))
                            .Set("normal", Vec3O(NzP(v, "normal", "x", 0), NzP(v, "normal", "y", 1), NzP(v, "normal", "z", 0))));
                }
                else if (Regex.IsMatch(pname, "^Drag$", RegexOptions.IgnoreCase)) { var v = ReadSpawnNumberSlot(sID); if (v != null && v.IsNumber) p.Set("drag", v.Num); }
                else if (Regex.IsMatch(pname, "Channel.*Distance", RegexOptions.IgnoreCase)) p.Set("channelDistance", VfxCurveGradient.ConvertUnityCurveToLaya(UnityYamlParser.GetSlotInlineValue(s)));
                else if (Regex.IsMatch(pname, "Gravity.*Distance", RegexOptions.IgnoreCase)) p.Set("gravityDistance", VfxCurveGradient.ConvertUnityCurveToLaya(UnityYamlParser.GetSlotInlineValue(s)));
                else if (Regex.IsMatch(pname, "Vortex.*Distance", RegexOptions.IgnoreCase)) p.Set("vortexDistance", VfxCurveGradient.ConvertUnityCurveToLaya(UnityYamlParser.GetSlotInlineValue(s)));
                else if (Regex.IsMatch(pname, "System is World Space", RegexOptions.IgnoreCase)) { var wv = UnityYamlParser.GetSlotInlineValue(s); p.Set("worldSpace", BoolLoose(wv)); }
            }
        }

        private static Jval Unwrap2(Jval v)
        {
            if (v != null && v.IsObject) { var kk = new List<string>(v.Keys); if (kk.Count == 1 && Regex.IsMatch(kk[0], "^(direction|position|vector)$", RegexOptions.IgnoreCase)) return v.Get(kk[0]); }
            return v;
        }

        private void BuildOutputTextureMesh(VfxEntry ctx, Jval layaCtx, Jval props, string layaCtxType)
        {
            var ctxInputs = new List<string>(UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots"));
            foreach (var subID in UnityYamlParser.GetRefArrayField(ctx.Body, "m_SubOutputs"))
            {
                var subEntry = Get(subID); if (subEntry == null) continue;
                foreach (var sid in UnityYamlParser.GetRefArrayField(subEntry.Body, "m_InputSlots")) ctxInputs.Add(sid);
            }
            // mainTexture
            foreach (var slotID in ctxInputs)
            {
                var slotEntry = Get(slotID); if (slotEntry == null) continue;
                string slotType = UnityYamlParser.GetSlotType(slotEntry) ?? "";
                if (!Regex.IsMatch(slotType, "Texture2D", RegexOptions.IgnoreCase)) continue;
                string propName = UnityYamlParser.GetSlotPropertyName(slotEntry);
                if (propName != "mainTexture" && propName != "baseColorMap") continue;
                string texExposed = UnityYamlParser.ResolveExposedNameForInputSlot(slotEntry, Get);
                var v = UnityYamlParser.GetSlotInlineValue(slotEntry);
                var resolved = ResolveResourceRef(v);
                if (resolved == null && texExposed == null) continue;
                if (texExposed != null) props.Set("_mainTextureProperty", VfxHelpers.SanitizePropName(texExposed));
                if (resolved != null) props.Set("mainTexture", SwapMainRepeatVariant(resolved));
                break;
            }
            // fallback：shaderPropertyDefaults 里的 MainTexture
            if (props.Get("mainTexture") == null && props.Get("shaderPropertyDefaults") != null)
            {
                var spd = props.Get("shaderPropertyDefaults");
                string mt = spd.StrOf("_MainTexture") ?? spd.StrOf("MainTexture") ?? spd.StrOf("_MainTex") ?? spd.StrOf("MainTex");
                if (mt != null && mt.StartsWith("res://")) props.Set("mainTexture", mt);
            }
            // mesh
            var BUILTIN = new Dictionary<int, string> { { 10202, "Sphere" }, { 10207, "Capsule" }, { 10208, "Cube" }, { 10209, "Plane" }, { 10210, "Cylinder" }, { 10211, "Quad" } };
            if (layaCtxType == "outputMesh" || layaCtxType == "outputStaticMesh" || layaCtxType == "outputComposedParticle" || layaCtxType == "outputShaderGraphMesh")
            {
                foreach (var slotID in ctxInputs)
                {
                    var slotEntry = Get(slotID); if (slotEntry == null) continue;
                    string slotType = UnityYamlParser.GetSlotType(slotEntry) ?? "";
                    if (!Regex.IsMatch(slotType, @"UnityEngine\.Mesh", RegexOptions.IgnoreCase)) continue;
                    if (UnityYamlParser.GetSlotPropertyName(slotEntry) != "mesh") continue;
                    var v = UnityYamlParser.GetSlotInlineValue(slotEntry);
                    var obj = v != null ? v.Get("obj") : null;
                    string guid = obj != null ? obj.StrOf("guid") : null;
                    int fileID = obj != null ? (int)obj.NumOf("fileID") : 0;
                    string nm;
                    if (guid == "0000000000000000e000000000000000" && BUILTIN.TryGetValue(fileID, out nm)) props.Set("mesh", "builtin:" + nm);
                    else { var resolved = ResolveResourceRef(v); if (resolved != null) props.Set("mesh", resolved); }
                    string meshExposed = UnityYamlParser.ResolveExposedNameForInputSlot(slotEntry, Get);
                    if (meshExposed != null) props.Set("_meshProperty", VfxHelpers.SanitizePropName(meshExposed));
                    break;
                }
            }
            // SG blendMode override
            if (layaCtxType == "outputShaderGraphMesh" || layaCtxType == "outputShaderGraphQuad")
            {
                var sgBlend = UnityYamlParser.GetIntField(ctx.Body, "blendMode");
                if (sgBlend != null) props.Set("blendMode", VfxHelpers.UnityBlendModeToLaya(sgBlend.Value));
            }
            // _Color alpha=0 → setAttribute(alpha,0) 注入已在 ConvertContexts（Driver2，对应 JS L4635-4674）实现。
        }

        private void BuildPlanarOutputProps(VfxEntry ctx, Jval props)
        {
            int? primEnum = UnityYamlParser.GetIntField(ctx.Body, "primitiveType");
            var PRIM = new Dictionary<int, string> { { 0, "Triangle" }, { 1, "Quad" }, { 2, "Octagon" } };
            int uvModeInt = UnityYamlParser.GetIntField(ctx.Body, "uvMode") ?? 0;
            int blendFrames = UnityYamlParser.GetIntField(ctx.Body, "flipbookBlendFrames") ?? 0;
            string uvMode = "Default";
            if (uvModeInt == 1) uvMode = blendFrames != 0 ? "FlipbookBlend" : "Flipbook";
            else if (uvModeInt == 2 || uvModeInt == 3) uvMode = "FlipbookBlend";
            Jval flipbookSize = Jval.Obj().Set("x", 4).Set("y", 4);
            string flipbookExposed = null;
            var ctxInputSlots = UnityYamlParser.GetRefArrayField(ctx.Body, "m_InputSlots");
            foreach (var slotID in ctxInputSlots)
            {
                var se = Get(slotID); if (se == null) continue;
                if (UnityYamlParser.GetSlotPropertyName(se) == "flipBookSize")
                {
                    var v = UnityYamlParser.GetSlotInlineValue(se);
                    if (v != null && v.IsObject) flipbookSize = Jval.Obj().Set("x", v.Get("x") != null && v.Get("x").IsNumber && v.NumOf("x") != 0 ? v.NumOf("x") : 4).Set("y", v.Get("y") != null && v.Get("y").IsNumber && v.NumOf("y") != 0 ? v.NumOf("y") : 4);
                    flipbookExposed = UnityYamlParser.ResolveExposedNameForInputSlot(se, Get);
                    break;
                }
            }
            int? blendModeInt = UnityYamlParser.GetIntField(ctx.Body, "blendMode");
            bool useAlphaClipping = (UnityYamlParser.GetIntField(ctx.Body, "useAlphaClipping") ?? 0) != 0;
            double alphaThreshold = 0.5;
            if (useAlphaClipping)
                foreach (var slotID in ctxInputSlots)
                {
                    var se = Get(slotID); if (se == null) continue;
                    if (UnityYamlParser.GetSlotPropertyName(se) == "alphaThreshold") { var v = UnityYamlParser.GetSlotInlineValue(se); if (v != null && v.IsNumber) alphaThreshold = v.Num; break; }
                }
            props.Set("primitive", primEnum != null && PRIM.ContainsKey(primEnum.Value) ? PRIM[primEnum.Value] : "Quad");
            props.Set("blendMode", VfxHelpers.UnityBlendModeToLaya(blendModeInt ?? -99));
            props.Set("cameraSort", false);
            props.Set("frustumCull", false);
            props.Set("uvMode", uvMode);
            props.Set("flipbookSize", flipbookSize);
            props.Set("softParticleFade", 0);
            props.Set("cropFactor", 0.146);
            props.Set("useAlphaClipping", useAlphaClipping);
            props.Set("alphaThreshold", alphaThreshold);
            if (flipbookExposed != null) props.Set("_flipbookProperty", VfxHelpers.SanitizePropName(flipbookExposed));
        }
    }
}
