# PlanComparer 规格（spec-before-code 纪要落档，2026-09-04）

> 状态：**已实现收官（2026-09-04）**——CompareCore [U] 全绿（全量 78/78，含 Comparer 24 条）+ 共享
> 采集 NxCollect（as Tool 入选判据 + FixtureOffset 补读）+ ComparerAdapter 三连 [I] 收官：
> comparer-run-20260904-144237（终跑：issues=6 与校准清单逐条一致、fixture=1/1 闭环）、
> adapter-run-143344/142216（重导回归）、executor-run-143426（ok=17，fixture 真对照）。
> 范围决策 D-1（对比维度）= A（结构/刀具/MCS/白名单参数）与输入形态 C（单会话双件轮换采集）、
> 采集层共享 NxCollect、首跑工具链+变异校准（§5）。设计 §7 步骤 3 完成——三步闭环 v1 收官。
> 需求源：docs/nx-plugin-design.md §7 步骤 3 / §2.2（维度表与输出口径）；前置范围：nx-plan-executor-spec.md
> §0/§7 D-1（重建 v1 空件无几何无刀路 → 对比维度显式声明）；事实源：nx2406-install-index.md §2.1。
> 上游共享：ExportSnapshot（PlanExporter/Model.cs，导出与对比共用采集口径）+ WhiteList / ToolFamilyMap 归一思想。

## 0. 一段话结论

PlanComparer = 「ComparerAdapter（NX 会话）对两件 prt（gt 手编件 + 自动重建件 prj′）各采集一份
ExportSnapshot（与导出同采集面、同口径）→ CompareCore（纯逻辑，无 NX 依赖）按维度对齐对比 →
ComparerResult（逐项偏差 + 汇总评分）→ comparer-run-<ts>.txt 报告」。**v1 维度 = 结构（op 集/序名/
父组/模板对 + 顶层组序）+ 刀具（类型词+数值）+ MCS/fixture + 白名单参数**——与重建侧回读对照（executor
I-2）同 API 面，全部已实证，零新探针；几何/刀路维度显式缺席（重建 v1 空件，D-1 声明）；策略/技术全参数
面待导出扩展（v1.5）。唯一新实证点 = **双 Part 同会话轮换采集纪律**（[T]，I-1 首跑点亮）。评分规格：
容差默认 ComparerOptions{EpsLen=0.01mm, RelTol=5%, EpsAxis=1e-6}（决策④直觉默认），经首批样例校准
（I-2/I-4）后固化。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 语义 | 回答「plan 合同重建保真度」：prj′（自动）vs prj（手编 gt）的工艺差；报告逐项偏差 + 汇总评分 |
| 输入 | 两件 .prt（gt 与 prj′）；快照 = ExportSnapshot ×2（采集层与导出共用，保证同口径可比） |
| 输出 | ComparerResult（结构体，供单测断言）+ samples/comparer-run-<ts>.txt（逐项偏差表 + 汇总评分） |
| 调用序列（[I]） | 干净 NX 会话 → 取件纪律开 Part A（test.prt）→ SetWork+显示 → 采集快照 A → 开 Part B（rebuilt）→ SetWork+显示轮换 → 采集快照 B → CompareCore.Compare(A, B) → 落盘报告 → 退出。⚠️ 双件轮换纪律 = [T]，I-1 首跑点亮（943006 已装载拒绝 Open*：A、B 各 OpenDisplay 一次，后续轮换仅 SetWork/SetDisplay） |
| 失败语义 | 结构级（件打不开/无 CAMSetup/许可缺/快照非法）→ 中止不落盘；条目级（单 op 参数读回失败）→ diag 继续 |
| 只读纪律 | 不 Commit/不修改/不保存两件源 prt；SetWork/SetDisplay 属会话状态非修改（MONO-1 评审） |
| 状态/所有权 | CompareCore 无状态幂等；快照所有权在调用方（Compare 不改写，INV-C2） |
| 版本兼容 | 快照/结果模型随导出侧演进；本模块无独立文件格式（报告 txt 为展示层） |

## 2. 数据结构要点

- 输入复用 `ExportSnapshot`（PlanExporter/Model.cs）：Operations（Name/TypeFamily/四父/Params 字典）、
  Tools（Name/NxType/NxSubtype/数值字段）、Setups（Name/Mcs*/SafePlaneZ/FixtureOffset/MissingMcs）、
  ProgramOrder（顶层组序）。
- 对齐键：**op = Name**（gt 名 = plan 名 = prj′ 名，同一 plan 链成立；名失配 → 结构失配不猜）；
  **setup = Name**（MCS_MILL）；**tool = 采集序 i**（同 plan 链两件刀具序一致，executor 按 plan tools[]
  序建；数量不等 → 尾多/尾少失配；名差 → Notes 注记非致命）。重复名 → DUP 失配条目（INV-C1）。
- ⚠️ **op 父组/树形层级 v1 不比对**（2026-09-04 规格修订——口径破绽实证）：v1 exporter workplan 简化
  "嵌套组不展开"（A1-1/A1-3 等嵌套父组归 root）且重建树把顶层组落到模板默认 PROGRAM 组下
  （根语义错位一级）→ 两侧父名口径不可比，比对必然误报。结构维度 = op 集（单侧/序）+ 顶层组序
  （ProgramOrder）+ 数量差。
  **v1.5-① 已实现并 [I] 验证（2026-09-04）**：exporter 改 ProgramTree 真实嵌套渲染（root=NC_PROGRAM
  镜像，ws 经 TagKey 定位，组内成员序=GetMembers 序）；executor 根语义对齐（顶层组→NX 程序根，
  顶层同名 PROGRAM→复用默认组）+ Steps DFS 交错保序（组/工序交错创建，NX 成员序=创建序）。
  160817 复跑：PROGRAM_ORDER_DIFF 与 ORDER_SHIFT 均归零（issues 6→5，见 §3 校准记录）。
  树形/父链比对维度待 ② 实现（op 父链采集与 CompareCore 链比对排队）。
- 类型键（语言无关优先）：刀 = NxType/NxSubtype 优先、TypeFamily 兜底（同 ToolFamilyMap 回退思想——
  014933 等 D-2 时代重建件 NxType 空）；op 模板对 = WhiteList.Resolve(TypeFamily) 归一后比对
  （同会话采集 → 两侧 TypeFamily 同语言，[U] 夹具不受限）。
- 容差：`ComparerOptions { EpsLen = 0.01mm, RelTol = 0.05, EpsAxis = 1e-6 }`（决策④直觉默认；
  校准后固化为评分规格文档）。数值判据 = `|a-b| ≤ EpsLen` **或** 相对偏差 `|a-b|/max(|a|,1e-9) ≤ RelTol`。
- 结果模型（新，纯逻辑）：`ComparerResult { OpDiffs, ToolDiffs, SetupDiffs, StructureIssues, Score, Diags }`；
  条目含 key（op 名/setup 名/刀序号）与双侧值——INV-C3 可溯。
- 表示决策：枚举不做（无实证枚举面）；参数键 = 导出侧现有键集（part_stock/floor_stock/depth_per_cut/
  bottom_stock/hole_depth…），两件同采集面。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证 [T]=待实测）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| PRE-C1 | 输入快照非空且结构合法（Ops/Tools/Setups 列表存在、每 op 有 Name） | 定义 | null/缺名 → 明确错误/异常 | [U] |
| POST-C1 | 匹配 op 对：Params 逐键双判据（EpsLen 或 RelTol）→ PASS/FAIL 条目含双侧值与键 | 设计 §2.2 参数维度 | 夹具一致 → PASS；恰 1 处变异 → 恰 1 FAIL | [U] |
| POST-C2 | 模板对失配（Resolve 归一后 pair 不等）显式条目（不静默） | 实证口级纪律 | 替身 TypeFamily 异 → 条目含双侧 pair | [U] |
| POST-C3 | 刀具逐把（序对）：类型键失配 + 数值双判据 → 条目；名差 → diag 非致命 | 设计 §2.2 刀具维度 | 变异直径 → 恰 1 FAIL；类型异 → 类型条目 | [U] |
| POST-C4 | setup 逐名对：MCS origin 欧氏距离 ≤EpsLen；z/x 轴元素差 ≤EpsAxis；fixture 整数等 → 条目 | 设计 §2.2 MCS 维度 | 变异 origin → FAIL；一致 → PASS | [U] |
| POST-C5 | 汇总评分确定性且由条目派生（无独立评分逻辑）：结构一致率（匹配 op/对齐基数）、参数 PASS 率、MCS PASS 率 + 统计 | 设计 §2.2 输出 | 双跑恒同 + 明细手算 == 汇总 | [U] |
| POST-C6 | 单侧独有 op（重建缺/多）、顶层组序差（ProgramOrder）、刀具数差、setup 数差 → 结构条目。op 父组/树形层级**不比对**（§2 口径破绽注记，v1.5 补） | 设计 §2.2 结构维度（修订） | 缺 1 op 夹具 → 恰 1 结构条目 | [U] |
| POST-C7 | op 名序列一致（按各自采集序）——名集相同而序不同 → ORDER_SHIFT diag（不静默） | 结构维度（刀路输出序语义） | 逆序夹具 → diag | [U] |
| INV-C1 | 对齐 1:1：双侧重复名 → DUP 失配条目 + diag，不静默取首 | 工程纪律 | 重复名夹具 | [U] |
| INV-C2 | Compare 不改写输入快照（只读） | 定义 | Compare 前后快照字段断言不变 | [U] |
| INV-C3 | 每条 diff/diag 带可溯 key（op 名/setup 名/刀序），结果与报告一一对应 | 工程纪律 | 遍历断言 | [U] |
| INV-C4 | diagnostics 聚合：同 key 同 code 一次 | 工程纪律（沿导出 INV-6 口径） | 重复场景 → 单条 | [U] |
| MONO-C1 | Compare 无状态幂等：同输入重复调用恒同输出 | 定义 | 双跑相等断言 | [U] |

[I] 集成验证清单（不进单测；本模块全部待首跑点亮）：
- I-1 **双 Part 同会话轮换采集纪律**（[T]）：A/B 各 OpenDisplay 一次 + 轮换 SetWork/SetDisplay，两快照
  ops/tools/setups 数与 Executor I-2 对照数一致（6/6/1）；
- I-2 工具链首跑：test.prt vs test.rebuilt.prt → 报告（出现未解释 FAIL = bug 先查）；
- I-3 NxCollect 重构/修复回归：ExporterAdapter 重导 test.plan.json，与 U-7 版（135344）同形状同 PASS；
- I-4 变异校准（决策④）：[U] 层变异夹具为主（POST-C1/C3/C4 已含恰 1 变异 → 恰 1 FAIL）；
  NX 侧人工变异重建件一处并重跑检出（可选项，不阻塞首跑）。

> **v1.5-① 复跑（2026-09-04，comparer-run-20260904-160817 + executor-run-20260904-160639）**：
> ①（workplan 树形展开 + executor 根语义 + DFS 交错保序）[I] 验证收官——issues 6 → **5**：
> PROGRAM_ORDER_DIFF（顶层组根语义）与 ORDER_SHIFT（组成员序，中态 160101 实证：executor 先组后
> op 创建致 rebuilt 成员序异序 → 修复为 Steps DFS 交错后消失）双双归零；剩余 5 项 = 下方校准清单
> 中非结构项原样（4× PTP OP_PARAM_DIFF + 1× TOOL_TYPE_DIFF@tool#4），无新增未解释差异。
> 树形/父链比对维度待 ② 实现（快照含 ProgramTree 顶层序，op 父链采集与 CompareCore 链比对排队）。

> **首跑校准记录（2026-09-04，comparer-run-20260904-141713 首跑 + 142424 修复后复跑）**：
> ① 采集判据修正：NxCollect 刀具入选由 depth≥1+家族串排除改为 **as Tool 下转判据**（首跑 rebuilt
> tools=0——重建件刀组挂机床根直接层被漏采；修复后 6/6 一致，I-3 同轮重锚 PASS）；
> ② FixtureOffset 补读（NxCollect，P4 实证面）——导出 plan 随之带出 fixture_offset=1（原 null 缺口
> 同源），executor 对照从"未设对照"转真对照；
> ③ **已解释差异清单（复跑 issues=6，非 bug）**：4× OP_PARAM_DIFF = gt PTP 旧模板读 hole_depth vs
> rebuilt 近似 DRILLING 读 bottom_stock（PTP→DRILLING 近似可见面，不静默）；1× TOOL_TYPE_DIFF@tool#4 =
> gt 中心钻 (Mill,MillChamfer) vs rebuilt 默认铣 (Mill,Mill5)——U-7 注册对表未覆盖的已知近似
> （executor diag 同源，spec §5b）；1× PROGRAM_ORDER_DIFF = gt A01 挂树根 vs rebuilt 挂模板默认 PROGRAM
> 组（workplan 根语义，v1.5 对齐）；6× 刀名差 note（gt 直径名 vs rebuilt T-id，非致命按序对）。
> 该清单即决策④首样校准输出：容差判据（0.01mm/5%/1e-6）首跑验证可检出上述全部真实差异且无非预期
> 噪音；评分规格以本清单为基准随 v1.5 维度扩展固化。

## 4. 算法（步骤 → 性质映射）

A1 输入合法性与索引：双侧 op 名 → map（重复名 → DUP 失配 + diag，INV-C1）；setup 名 map；刀具序
    → PRE-C1/INV-C1
A2 配对：B 逐 op 查 A 名；A-only/B-only → 结构条目（POST-C6）；名集同序异 → ORDER_SHIFT（POST-C7）
A3 逐配对对比：模板对归一比对（POST-C2）；Params 双判据逐键（POST-C1）；值单侧有 → 条目归 FAIL 不静默
A4 刀具序对：类型键 + 数值双判据 + 名 diag（POST-C3）
A5 setup 名对：origin 欧氏/轴元素差/fixture（POST-C4）
A6 结构项：顶层组序（ProgramOrder）、单侧 op、刀具/setup 数差 → 结构条目（POST-C6；不含父组，见 §2 口径注记）
A7 汇总：条目派生评分（POST-C5）+ diag 聚合（INV-C4）+ 渲染 txt（INV-C3）
终止性：两侧表遍历有界（沿 MONO-2 口径）；Compare 无状态（MONO-C1）
某性质无算法步保证：无（全表映射齐）

## 5. 范围决策与冲突

- **D-1 对比维度 = A**（已确认 2026-09-04）：结构/刀具数值/MCS/白名单参数四维。几何面级/刀路维度
  v1 不可达（重建空件 + 合同无面锚点，executor D-1 同源）→ 显式缺席，不假装覆盖（§0/§6 差异注记）；
  策略/技术全参数面（cut_pattern 等）依赖导出白名单扩展（v1.5 排队，参数字典键即现有回读面）。
- **D-2 输入形态 = C**（已确认）：单会话双件轮换实态采集（见 §1；[T]=I-1）。否决 B（两次导出 plan
  diff）：分辨率受导出白名单截断 + 语义漂移为"存档 diff"。
- **D-3 采集层 = 共享 NxCollect**（已确认）：采集函数自 ExporterAdapter 提取为 Journal/NxCollect.cs，
  两适配器共用 → 采集口径单一事实源（comparer 可信前提）；重构无损由 [I] I-3 回归锚定。
- **D-4 校准 = 首跑 + 变异**（已确认）：test.prt vs test.rebuilt.prt 工具链首跑（I-2）+ [U] 变异夹具
  （恰 1 变异 → 恰 1 FAIL，POST-C1/C3/C4 已内建）；容差 ComparerOptions 可注入，首批样例校准后
  固化为评分规格文档。
- **与设计文档差异（落档注记）**：设计 §2.2"写回 diagnostics[] 供报告页展示"无宿主（comparer 输入为
  两 prt 非 plan）→ v1 输出独立报告文件（ComparerResult + txt）；"策略/几何/刀路"三维标 v1.5；
  "几何匹配率"（FaceResolver）随 FaceResolver 状态（U-5 负结案）同步缺席。
- **对齐键依赖**：op 名 = plan 名（同一 plan 链成立）；跨源/手改件 → 名失配走结构失配路径，不猜
  （§2）。刀序对齐同源成立（executor 按 plan 序建，I-2 已锚）。

## 6. 实现策略（确认后执行）

复用纯逻辑工程惯例（CompareCore 无 NX 依赖 + csc 临时编译 ComparerAdapter + NX Execute 执行，
纪律见索引 §2.1）；[U] 骨架先行钉 API（每条性质显式红占位 → 实现点亮全绿），红线回归脚本
run-unittests.ps1 纳入 PlanComparer/PlanComparerTests 目录；csproj 加 PlanComparer\*.cs 通配。
落盘资产：samples/comparer-run-<ts>.txt（首跑）；共享采集重构后 ExporterAdapter 行为经 [I] I-3 回归。
计划改动面：新 PlanComparer/{ComparerCore,Result}.cs + PlanComparerTests/* + Journal/NxCollect.cs
（ExporterAdapter 提取改造）+ Journal/ComparerAdapter.cs + scripts/compile-comparer-adapter.ps1
+ scripts/run-unittests.ps1 目录登记 + csproj 通配 + 本文档状态更新。

> 执行记录（2026-09-04）：spec 落档 → [U] 骨架全红（24 条 NotImplementedException 占位）→ CompareCore
> 实现至 78/78 全绿（期间 3 条测试构造按真实语义修正：模板计数=配对 op 数、变异量须超双判据、
> TypeFamily 兜底仅双侧无 NxType 时可比、dup 构造单侧双实例）；NxCollect 提取自 ExporterAdapter 并
> 瘦身（421→214 行）；ComparerAdapter + 三合编脚本。首跑（141713）暴露重建件刀具漏采 → 入选判据改
> as Tool 下转（142424 修复验证）；FixtureOffset 补读入 NxCollect（143344 重导带出 fixture_offset=1、
> 143426 executor ok=17 真对照）；会话残留致 A/B 同件自比（143523 无效）→ ComparerAdapter 加同件护栏
> + 候选名诊断日志；终跑 144237 双件正确轮换、issues=6 与校准清单逐条一致（校准记录见 §3 尾部）。
> 资产：samples/comparer-run-20260904-144237.txt（终跑证据）、test.rebuilt-143432.prt（fixture 链重建件）。
