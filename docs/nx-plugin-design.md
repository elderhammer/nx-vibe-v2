# NX 插件设计 (v3) — Plan 双向验证闭环

> 更新时间：2026-09-03（NX2406 安装资料核对修正；API 事实索引见 [nx2406-install-index.md](./nx2406-install-index.md)）
> 定位转变：**初始版本不再直接消费云端 CAPP 计划**，而是以**工程师手编的 NX 工程
> 为 ground truth**，跑通「导出 plan.json → 按 plan 自动重建工程 → 对比偏差」三步闭环。
> 该闭环既验证 plan.json 合同是否无歧义，也为后续 CAPP 自动生成的工序提供校准基准。
>
> 前置阅读：[nxopen-research.md](./nxopen-research.md)（NXOpen API 能力全景 + Builder 参数面 + 回读能力；
> 其附 A 为 NX2406 属性取值形态与枚举宿主速查）、[nx2406-install-index.md](./nx2406-install-index.md)（本地安装资料资源索引 + 事实速查）。
> Plan 合同：[autocam-plan.schema.json](../schema/autocam-plan.schema.json)（导出与导入共用同一合同）。
>
> NX2406 核对摘要（2026-09-03）：无 `CAMSetupBuilder` 类；组/操作创建签名与 Builder 工厂宿主
> 已变（见 §3 与 nxopen-research §3.1-3.3）；builder 属性取值是四形态混合（§2.1 ⚠️ 与 §6）；
> 模板部件 `cam_general_mill.prt` 在 2406 不存在（用 `mill_contour.prt` 等，见 nx2406-install-index.md §1 与 nxopen-research 附 B）。

---

## 1. 核心工作流（初始版本三步骤）

```
① 导出 (Export — ground truth 采集)
   工程师手编 NX prj（含完整 CAMSetup：程序组/刀具/几何/MCS/工序）
      └─ PlanExporter：遍历四视图组树 + 每个 Operation
                      回读各 Builder 实际生效参数
                      序列化 plan.json（工程师工艺意图的数字化存档）

② 导入重建 (Reimport)
   打开原始 STEP 文件
      └─ 加载步骤①的 plan.json
      └─ PlanExecutor：建 CAMSetup → 建四组 → 逐 workingstep 创建工序
                       → 生成另外一个 prj′（自动生成工程）

③ 对比验证 (Compare)
   prj′（步骤②自动生成） vs prj（步骤① ground truth）
      └─ PlanComparer：按 plan 维度逐项对比，输出偏差报告
                       （工序/刀具/参数/MCS/几何/刀路时间长度）
```

> 步骤②③是"自动复刻 + 差距量化"：偏差报告同时回答
> **「plan.json 合同能否无歧义重建出工程师的意图」**与
> **「未来 CAPP 自动生成的 plan 与工程师手编工艺差多远」**。

## 2. 模块设计

| 组件 | 职责 | 状态 |
|---|---|---|
| `PlanExporter` | 读工程师手编 prj：遍历 CAMSetup 组树 + Operation，回读 Builder 实际参数 → plan.json | 🔧 新增 |
| `PlanParser` | plan.json → 强类型模型（对齐 schema v3） | 🔧 |
| `PlanExecutor` | 重建：STEP 打开 + 按 plan 建 CAMSetup/四组/逐工序创建 → prj′ | 🔧 |
| `FaceResolver` | OCCT face_id → NX Tag（质心+面积+曲面类型+法向匹配） | 🔧 |
| `PlanComparer` | prj′ vs prj 偏差计算：逐工序/刀具/参数/MCS/几何/刀路，输出报告 | 🔧 新增 |

### 2.1 PlanExporter（新增，导出侧核心）

| 读取对象 | plan.json 落点 | 方式 |
|:---|:---|:---|
| Program 组树 | `workplan` 树序 / `setup` 划分 | 按 Program 组名还原 setup/顺序 |
| Geometry 组 MCS | `setups[].mcs`（origin/z_axis/x_axis）+ 安全平面 | `MillOrientGeomBuilder` 回读 |
| Tool 组 | `resources.tools[]` | 刀具 Builder 全参数回读 |
| Method 组 | 工序 `method_ref` | 回读方法组名 |
| Operation 类型 | `operation_type` + `nx_template` | 按 typeName/subtypeName 映射 |
| Builder 参数 | `strategy` / `technology` | 各 Builder 实际值回读 |
| 关联几何 | `feature.geometry_ref` | 按 NX Tag → 几何属性锚点（与 FaceResolver 反向） |

> ⚠️ 关键点：NX Builder 参数**未显式设置时继承父组/方法组默认值**。
> 导出必须回读**生效值（resolved value）**而非仅显式值，否则 plan 缺字段，
> 步骤②重建结果与 ground truth 必然不一致。
>
> ⚠️ NX2406 实现前提（实证）：① 组树遍历用 `CAMSetup.GetRoot(CAMSetup.View)`（根组）
> + `NCGroup.GetMembers()/GetParent()` + `Operation` 四父链（`ParentProgramOrder`/
> `ParentMachineTool`/`ParentGeometry`/`ParentMachineMethod`）；② Builder 属性取值是
> **四种形态混合**（Inheritable*Builder→.Value / 直接 double / 直接枚举 / 类+嵌套枚举 .Type），
> 回读/写入必须按形态分支（速查表见 nxopen-research 附 A）；③ 操作 Builder 工厂在
> `OperationCollection` 上；④ 组/操作创建的第 4/7 参是 `UseDefaultName` 枚举。
> 另：`MillCutParameters.DepthPerCut` 不存在（在 `PlanarOperationBuilder`/`CavityMillingBuilder`
> 直接成员上）；孔加工无 `Cycle` 属性（用 `CycleTable`）——均勿写入 plan 映射表。

### 2.2 PlanComparer（新增，偏差量化）

对比维度（prj′ vs prj，按 plan 字段逐个对齐）：

| 维度 | 对比项 | 偏差口径 |
|:---|:---|:---|
| 结构 | 工序数/类型/顺序、组树层级 | 类型 mismatch 计数、顺序差异 |
| 刀具 | 直径/刃数/圆角/螺距/类型 | 数值差 + 容差(如 0.01mm) |
| 技术参数 | 转速/进给/余量/步距/深度 | 数值差 + 相对偏差% |
| 策略 | cut_pattern/cycle/顺逆铣/安全平面 | 枚举一致性 |
| MCS/装夹 | 原点/轴/夹具偏置/安全高度 | 向量距离 + 标量差 |
| 几何 | 工序关联面集 | FaceResolver 匹配率（漏/错/多） |
| 刀路 | 生成刀路时间/长度/过切 | 回读 `Operation.GetToolpathTime()/GetToolpathLength()`；过切经 `CAMSetup.GougeCheck(...)`/`CreateGougeCheckBuilder` 后读 `Operation.GougeCheckStatus/Results`（2406 无 `Operation.gougeCheck`） |

输出：**逐工序偏差表 + 汇总评分**（结构一致率 / 参数偏差均值 / 几何匹配率），
并写回 `diagnostics[]` 供报告页展示。

## 3. 宿主要求与入口

- **SDK**：NXOpen for .NET（C# .NET Framework 4.8；2406 样例工程目标 v4.5，兼容）
- **入口**：NXOpen `INXAddIn` 或 Journal（`run_journal.exe -nogui` 支持 CI 批处理三步；`run_journal.exe` 存在于 NXBIN，`-nogui` 参数需实测）
- **建模对象**：`CAM.CAMSetup`（含 `CAMGroupCollection` → `NCGroupCollection`、`CAMOperationCollection` → `OperationCollection`；**无 `CAMSetupBuilder` 类**——NX2406 实证）。初始化：空 Part 先 `Part.CreateCamSetup(templateName)`（2406 模板如 `mill_contour`，见 nxopen-research 附 B）。

## 4. 测试路径（先于插件落地）

- 云端 `/api/v1/tests/nx-project/render`（渲染 NX Journal）继续保留，
  作为步骤②「plan → 自动建工序」的**零部署替代**（配套人工验收清单待该
  测试路径落地时再补充，不占位）。
- 步骤①③（导出/对比）必须先由插件在 NX 内完成：先跑通「手编工程 → 导出 →
  同一工程内重建 → 对比」的最小闭环，再扩展 STEP 打开与跨工程对比。

## 5. 平面化字段清单（MVP 所需）

```
plan_id / input_ref / name
setups[]        mcs(origin,z_axis,x_axis), safe_plane_z, fixture_offset
resources.tools[] type,diameter,num_flutes,(flute_length),lower_corner_radius
features[]      feature_id,feature_type,geometry_ref(anchor_point),params
operations[]    operation_id,operation_type(+nx_template),tool_ref,strategy,technology
workingsteps[]  workingstep_id,feature_ref,operation_ref,setup_ref
workplan(root,elements)  → Program 组树
diagnostics[]   (info/warning/error)
```

> 导出时该清单为**必填输出**（尽量完整）；导入时缺省字段允许继承组默认值（见 2.1 风险）。
> 对比时以清单字段为对齐基线，其余增强字段（非切削细分/避让点等）逐步加入。

## 6. 风险与备注

- **继承值捕获**：导出需回读生效值，否则 plan 不完整、对比偏差失真（2.1）。
- **属性取值四形态**（新增，NX2406 实证）：Inheritable*Builder→.Value / 直接 double /
  直接枚举 / 类+嵌套枚举 .Type 四种形态混合；回读与重建 Mapper 必须按形态分支
  （速查见 nxopen-research 附 A），否则统一 `xxx.Value = …` 的写法在大量参数上不成立。
- **API 面版本漂移**（新增，NX2406 实证）：四视图对象（`ProgramOrderView` 等）已移除 →
  `GetRoot(CAMSetup.View)`；操作 Builder 工厂移到 `OperationCollection`；创建参数枚举化
  （`UseDefaultName`）；`CAMSetupBuilder` 类不存在。成员级版本与许可可从 NXOpen XML remarks
  （`Created in NXxxxx` / `License requirements:`）程序化读取，插件按版本做能力探测（nx2406-install-index.md §2.4）。
- **许可**（新增）：cam_base 覆盖组/操作创建，但部分功能许可不同（实证：`CreateFeatureProcessBuilder`
  需 `ug_holemaking`）；前置许可检查建议按 XML remarks 探测，而非手写许可表。
- **几何映射**：跨 prt 的面 Tag 无共享标识，导出/对比均经 FaceResolver 属性匹配；
  对称特征可能命中错面 → 标 diagnostic 提示人工复核。
- **近似工序**：FreeCAD 口径的 approximation（chamfer→deburr/profile）在重建侧
  以 `nx_template` 真实类型落地；对比时按 nx_template 对齐。
- **2.5D 边界**：曲面/回转类超出当前口径，不作为初始版本目标。
- **版本差异**：Builder 参数面随 NX 版本微调（如 `BottomClearance` NX2312 新增，已被
  XML "Created in NX2312.0.0" 实证），按 NX 版本做能力探测。

## 7. 实施顺序

0. **API 形态基表**（先行）：按 nxopen-research 附 A 与附 B 验证项，先跑通最小 journal
   （组创建 → 操作创建 → 参数读写 → 刀路生成），沉淀「属性形态 + 枚举宿主 + 组/操作创建
   签名」实现基表，作为全部 Mapper 代码的依据。
1. **PlanExporter**：读手编 prj（铣+孔最小集）→ plan.json（组树遍历：`GetRoot(View)` +
   `NCGroup.GetMembers()` + `Operation` 四父链；回读按形态表分支）
2. **PlanExecutor 重建**：STEP + plan → prj′（复用 v2 的 PlanMapper 设计；建组/机床/刀具
   参考官方样例 `CAMSetupImport`，见 nx2406-install-index.md §1）
3. **PlanComparer**：prj′ vs prj 偏差表 + 汇总评分
4. 并入 `Autocam.Plugins.sln` 发布

> 运行时前置验证项（typeName 字面量、Stepover 链路、模板初始化、`-nogui` 批处理等）见
> nxopen-research 附 B 与 nx2406-install-index.md §3。
