# PlanExecutor 规格（spec-before-code 纪要落档，2026-09-04）

> 状态：**纪要落档（2026-09-04）；D-1/D-2 已确认 = A/A（§7）；核心实现红线全绿（33/33）；**
> **[I] 层集成完成（ExecutorAdapter v3 三连跑收官）**：I-1 全链创建、I-4 许可 gate、MONO-1 执行期、
> I-2 回读对照（6 工序序名/6 刀具直径/MCS 原点 (75,0,100) 全 PASS，executor-run-20260904-014930）、
> I-3 跨会话重开（reopen-20260904-015129：ops=6）全点亮；I-5 模板选择（hole_making）预检实证已背书。
> 重建资产：samples/test.rebuilt-014933.prt（136K，自建件入库）。
> 需求源：docs/nx-plugin-design.md §7 步骤 2 / §2.1（PlanExecutor 行）/ §4 最小闭环；
> 合同：schema/autocam-plan.schema.json v3.0（**导出侧实际产物的偏差见 §1**）；
> API 事实源：docs/nx2406-install-index.md（§2.1 创建语义/批处理纪律/§2.5 不存在项/§3）；
> 参数写入面实证：samples/camprobe-finalize-20260904-010401.txt（PartStock 可写、Stepover 整链不可写 U-6）。
> 上游共享：src/NXPlugins/PlanExporter/{Model,PlanJson,Doc,WhiteList}.cs（PlanDocument/序列化器复用）。

## 0. 一段话结论

PlanExecutor = 「plan.json（PlanDocument）→ 引用校验 → 展开为**有序重建指令图**（四父组锚点 +
可写参数子集）→ 结构 diag」的纯逻辑核心（无 NX 依赖，[U] 红线）+ run_journal 批处理适配器
（[I]：建件 → CAM 会话 → 许可 gate → CreateCamSetup → 依指令序建组/工序/MCS/参数 → 落盘 prj′ →
回读对照报告）。**v1 无几何/无刀路**（U-5/U-5c：plan 无面锚点，face 指派与刀路超出合同能力）；
闭环验收口径 = 结构/刀具数值/MCS/可写参数回读对照，范围显式声明。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 输入 | plan.json（schema v3 合同族）。**导出侧实际产物事实**（test.plan.json，DataContractJsonSerializer 渲染）：① strategy/technology 为 **Key-Value 数组**形态（schema 对象形；模型层 Dictionary 已归一，解析层无感——记录不修）；② `nx_template.{type,subtype}` 成对（type=模板部件名，如 mill_contour/hole_making）；③ `method_ref`=方法父组名（test.plan 为根名 "METHOD"）；④ `tool.type`=**NX 中文家族串**（违反 schema 枚举，v1 简化 → D-2）；⑤ features 无面锚点；⑥ 每 op 经 workingstep 1:1 挂 setup_ref |
| 调用序列（[I]） | NX 会话 File → Execute 预编译 exe（csc 合编核心+适配器，scripts/compile-executor-adapter.ps1）：`NewDisplay` → `Session.CreateCamSession()` → 许可 gate（cam_base Reserve）→ `CreateCamSetup("mill_contour")`（camprobe-drill 先例：孔工序/刀具同 setup 可行；全钻 plan 用 hole_making，预检实证）→ 指令序执行（Program/Method/Tool/Geometry 组 → 工序 → MCS/fixture → 参数子集）→ **不生成刀路** → 落盘 prj′ → 回读对照报告。⚠️ 运行载体注记（2026-09-04 实测）：核心依赖 `DataContractJsonSerializer`，journal 编译器缺 `System.Runtime.Serialization` 引用 → **run_journal 单文件 journal 合并不适用**（试编译报命名空间缺失）；[I] 层与 ExporterAdapter 同款 GUI Execute 工作流 |
| 失败语义 | 结构级（解析失败/ref 断开/许可缺/PRE 系）→ 中止不落盘；单项（未知刀具家族/拒写参数/组重名）→ diag 继续 |
| 状态/所有权 | 纯逻辑核心无状态；NX 侧会话内新建 prj′（落盘件属仓库自建资产）；上游 PlanDocument/序列化器只读复用 |
| 版本兼容 | 消费 contract_version=3.0；≠3.0 → 拒绝（结构级失败） |

## 2. 数据结构要点

- `RebuildPlan`（纯逻辑指令容器）：Programs 树（名+层级，源 workplan DFS）、Tools 列表（数值字段 +
  经 D-2 决策得到的模板对）、Operations（op_id/名/四父**锚点名**/可写参数指令表）、SetupGeometries
  （每 setup：MCS 组名 + WORKPIECE 子组）。
- 四父锚点解析规则：**Program**=workplan 树序建组（缺父挂根，与导出 A8 同口径）；**Method**=模板
  默认组复用（MILL_ROUGH/MILL_METHOD/MILL_SEMI_FINISH/MILL_FINISH/DRILL_METHOD 按名找，找不到建组
  diag+MILL_METHOD 族占位）或根名("METHOD"/空)挂方法根；**Tool**=按 tool_ref 建的刀具组；
  **Geometry**=op 的 ws.setup_ref → 该 setup 的 MCS/WORKPIECE 链。
- 参数指令 = **(NX 成员路径, 取值形态, 值)** 三元组，仅含**写入面白名单**（实证可写：PartStock/
  FloorStock/DepthPerCut `.Value`；fixture_offset 待 [I] 证）；**stepover 拒收 → diag**（U-6）。
- 刀具重建对（D-2 决策前以推荐 A 表述；实证补记见 §5b）：关键词表「铣刀*→(mill_planar,MILL)；
  钻刀/倒斜铣刀→(hole_making,STD_DRILL)；未知→(mill_planar,MILL)+warning」。**CutterSubtype 库刀具
  读回已证可行（§5b）→ U-7 无技术障碍**，导出侧补 schema 枚举后 Executor 可按枚举映射重建。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证 [T]=待实测）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| PRE-1 | 输入可反序列化为 PlanDocument 且 contract_version=3.0、必填域齐 | 协议 | 坏 JSON/版本异 → 明确错误 | [U] |
| PRE-2 | ref 闭合：tool_ref/setup_ref/feature_ref/operation_ref 指向存在项 | 合同定义 | 夹具引用完整性 | [U] |
| PRE-3 | 每个 op 的 nx_template 对非空 ∈ 支持集（白名单：CAVITY_MILL/DRILLING/SPOT_DRILLING/…） | 创建语义实证 | 空/未知 → error diag 且该 op 不入指令 | [U] |
| PRE-4 | 参数写入面白名单非空且仅含实证可写成员（stepover 家族排除；含 .Value 直写参数与刀具 Tl*、fixture_offset——均 2026-09-04 预检实证） | camprobe-finalize U-6 + camprobe-executor 012518 | 白名单表断言 | [U] |
| POST-1 | 每 op 四父锚点可解析（program/method/tool/geometry 各有落点或显式兜底） | 结构定义 | 锚点解析失败 → 该 op error diag | [U] |
| POST-2 | 任何不支持/拒写项 → 结构化 diag（code+scope+所属 op/ws），不静默缺字段 | 实证口级纪律 | 替身抛错 → diag 含 code+scope | [U] |
| INV-1 | op ↔ workingstep 1:1 保持（与导出同口径） | 决策⑤ | 夹具断言 | [U] |
| INV-2 | 每 op 参数指令集与 plan 参数字段一一对应（无凭空参数、无缺漏可写项） | 映射纪律 | 白名单双向往返 | [U] |
| INV-3 | 指令序 = workplan DFS 前序（刀路输出序先序一致） | 组树语义 | 乱序输入 → 输出仍 DFS | [U] |
| MONO-1 | NX 无事务：**全部可预检错误在首个 Create 前完成**；执行期只增不删 | 会话纪律 | 预检器先于一切创建；评审+集成 | [U]+[I] |
| INV-4 | diagnostics：同类同 scope 聚合一次；error 级对应结构缺失 | 工程纪律 | 重复场景 → 单条 | [U] |

[I] 集成验证清单（不进单测；2026-09-04 已全部点亮，源 executor-run-20260904-014930/reopen-015129）：
I-1 空件全链创建成功（程序组 A01 + 6 刀具组 + MCS_MILL/WORKPIECE 链 + 6 op DFS 序，无异常）；
I-2 回读对照全 PASS（工序数/序/名、6 刀具直径 vs plan、MCS 原点 (75,0,100)、fixture=1 默认（plan 未带））；
I-3 prj′ 落盘（SaveAs 时间戳名兜底）并跨会话重开复核 ops=6；
I-4 许可 gate（cam_base Reserve）前置通过；
I-5 CreateCamSetup("hole_making") 模板选择由预检探针实证（P6），适配器按"全钻→hole_making"分派。

## 4. 算法（步骤 → 性质映射）

A1 解析 + 结构校验（contract_version/必填）→ PRE-1
A2 ref 闭合校验 → PRE-2
A3 nx_template 白名单过滤（未知 → error diag 剔除）→ PRE-3
A4 四父锚点展开 + 可写参数面白名单过滤（拒写 → diag）→ POST-1/POST-2/PRE-4
A5 DFS 拓扑排序生成有序指令图 → INV-3/MONO-1（预检全量后置执行序）
A6 指令图自检（ref/1:1/参数往返）→ INV-1/INV-2/INV-4
A7 [I] 会话执行（适配器：建件→CAM 会话→gate→CAMSetup→指令序→落盘）
A8 [I] 回读对照报告（I-1..I-4）
终止性：树/表遍历有界（沿用导出 MONO-2 口径）；执行失败 → 中止不落盘（MONO-1）。

## 5. 冲突与已知限制（不静默）

- **strategy 数组 vs 对象**：导出侧 DataContract 渲染偏差，模型层归一 → 记录不修（§1 输入事实 ①）。
- **tool.type 违反 schema 枚举**：v1 简化 → D-2（§7）；挂 **U-7**（导出侧 CutterSubtype→schema 枚举
  映射，需先实测库刀具 CutterSubtype 读回，[T]）。**【收口 2026-09-04】：定案通道 = `NXOpen.CAM.Tool.GetTypeAndSubtype`
  （全家族、语言无关、钻族可读，camprobe-u7 六刀全实证）；schema 词集 A′（NX Types/Subtypes 原文收编）
  设计落 docs/nx-tool-type-enum-spec.md；实现待指令。**
- **stepover 不可写**（U-6）：重建侧步距字段拒收 + diag；有效写入通道未明。
- **几何/刀路缺席**：D-1 决策；对比范围声明见 §0，Comparer 落地时须显式限定维度。
- **method_ref 语义**：导出写父组名；重建侧"复用模板默认组/挂根"兜底与 ground truth 的组名一致性
  以回读对照（I-2）为判据，偏差入 diag。
- **GetNameOfType 语言敏感**（2026-09-04 预检实证）：刀具家族串随会话语言变化（中文/英文）→ 凡基于
  家族串的导出字段（tools[].type）会跨会话漂移；op 级白名单键为英文模板大类不受影响。U-7 刀具枚举化
  一并解决。

## 5b. 预检探针实证补记（2026-09-04，源 samples/camprobe-executor-20260904-012518.txt，ok=6/fail=0）

- P1 **CutterSubtype 库刀具读回**：铣刀 ×3=Mill5、中心钻(Chamfer Mill 家族)=ChamferTool；钻具运行时
  DrillStdToolBuilder（无 MillToolBuilder 面）；新建 (mill_planar,MILL) 默认 Mill5。
- P2 **新建刀具写链**：(mill_planar,MILL) 直径 10/刃数 4、(hole_making,STD_DRILL) 直径 8.5 → Commit → 重开持久 ✓。
- P3 **MCS/csys 写链**：`BasePart.CoordinateSystems.CreateCoordinateSystem(Point3d, Matrix3x3, bool=false)` →
  赋 `builder.Mcs` → Commit → 重开 o=(75,0,100)、X/Z 行一致；Matrix Element 行语义 row0=X/row2=Z 与导出同口径。
- P4 **FixtureOffset 写链**：Value=2 → 重开 2、status False ✓（PRE-4 白名单转实证）。
- P5 **方法父两形态**：op 挂方法根 METHOD 与模板默认 MILL_ROUGH 组均创建成功。
- P6 `CreateCamSetup("hole_making")` 可用（默认 DRILL_METHOD/MILL_METHOD；MachineTool 仅 NONE）。

## 6. 实现策略（确认后执行）

复用 PlanExporter 纯逻辑工程惯例（无 NX 依赖核心 + csc 临时编译 Journal 适配器 + run_journal 批处理
执行，纪律见索引 §2.1）；测试骨架先行钉 API（PlanParser→复用 PlanDocument/PlanJsonSerializer 反序列化
侧、新 ExecutorCore + RebuildPlan），每条 [U] 性质一个显式红测试（Assert.Fail 占位 → 实现点亮全绿）。
落盘资产：samples/test.rebuilt.prt + samples/executor-run-<ts>.txt。

> 执行记录（2026-09-04）：核心四文件 + 16 条 [U] 测试入库，33/33 全绿（含导出侧 17 条回归）；
> 红线回归脚本 scripts/run-unittests.ps1（csc 响应文件编译，绕开 shell 参数路径剥除）。
> [I] 层适配器完成（ExecutorAdapter 三连跑收官，da7e028；状态见文档头与 §3 集成清单）。

## 7. 范围决策（已确认 2026-09-04：D-1=A，D-2=A）

- **D-1 几何范围 = A**：v1 空件无几何重建（plan 无面锚点，几何/刀路超出合同能力；对比=结构/刀具/
  MCS/参数层并显式声明，见 §0）。B（test.step 几何背景件）不推进 v1 断言，归 v2/FaceResolver 链路。
- **D-2 刀具类型策略 = A**：NX 家族关键词表（含中英文键——GetNameOfType 语言敏感，§5b）+ 未知默认
  (mill_planar,MILL)+warning；数值字段直填（P2 实证）。**U-7 挂出**：导出侧补 CutterSubtype→schema type
  枚举（P1 证无技术障碍；C 选项可随时切）。**【U-7 已收口 2026-09-04，A′ 定案：schema type 词集换 NX
  Tool.Types/Subtypes 原文（docs/nx-tool-type-enum-spec.md），本决策键源随之替换，实现待指令。】**
