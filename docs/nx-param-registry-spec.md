# 参数键集注册表（v1.5-④ 收口纪要 = v1.5-③ 参数面扩展实现依据，2026-09-04）

> 状态：**纪要落档（2026-09-04）**——读面键集 + 写面形态矩阵经首跑与收口三跑实证定稿；
> 下游 v1.5-③（导出/重建/对比三侧参数面扩展）按本表为红线依据，实现待用户指令。
> 证据源：samples/camprobe-params-20260904-155341.txt（v1 首跑：读面 S2 + 写面矩阵 S3 六键）、
> samples/camprobe-params2-20260904-{163751,163823,163850}.txt（收口三跑：E1-E7 逐键自动判定）。
> 需求源：docs/nx-plan-comparer-spec.md §5 D-1（策略/技术全参数面 = v1.5 排队，依赖导出白名单扩展）；
> docs/nx-plan-exporter-spec.md §4 A6（形态注册表驱动回读的既有设计）+ §5 U-3/U-6；
> 上游事实：docs/nx2406-install-index.md §2.1（两处写入未保留：stepover + BoundaryInTol）/ §2.2（四形态表）
> / §2.5（stepover 族不可写负结案）。
> 合同：schema/autocam-plan.schema.json v3.0（strategy/technology 键名以 schema 为落点命名；本批不改结构）。
> API 事实源补充：NXOpen.xml（`MultiDepthCut.Toggle/StepMethod` 成员 + `MillCutParameters.BoundaryInTol/OutTol`）
> + UGOPEN\NXOpen\CAM_MultiDepthCut.hxx:57/70/83（Toggle:bool、StepMethod:MultiDepthCut::Types{Increment,Passes}）。

## 0. 一段话结论

本批把「策略/技术全参数面扩展」所需的**键集实态**钉死成注册表：读面键集 16 行（15 键可读——值 +
InheritanceStatus 实测值样例入表；1 键负证 = PTP cycle/循环细分无 builder 面，U-1）全有实据，
导出侧白名单扩展无读面阻塞；写面矩阵 8 键（表 #1-8，CAVITY_MILL 独立 op + PLANAR_MILL 对照）
**可持久 4 键**（cut_pattern/cut_order/cut_direction/finish_passes——#1/#4 三跑锚定、#2/#3 为 v1 单跑
见注 1，形态 = 类+嵌套枚举直赋/直枚举/.Type/int），**整链还原 4 键**（#5-#8 收口三跑全复现：
MultiDepthCut.Toggle bool、MultiDepthCut.StepMethod 嵌套枚举——
同写仍全还原 = 整对象丢弃，U-6 stepover 复合对象同款；BoundaryInTol 直 double 三面复证 2026-09-03
旧疑点、BoundaryOutTol 同族还原 = 容差族级死区、PLANAR_MILL 上亦还原 = 非 CAVITY 模板特化）。
U-6 教训再次坐实并扩界：**形态同类 ≠ 可写**（int 直赋 finish_passes 可写 vs bool 直赋 toggle 不可写；
枚举形态 cut_pattern/cut_order/step_method 中前二可写而 step_method 不可写）→ 注册表按键实证、
不按形态推断。重建侧可写子集 = 4 持久键增量（ParamWhiteList），不可写键一律拒收 + diag（U-6 同款）。
**未测写面的键（rpm/feed_cut 等）标注"未测"**，不得按形态推断后入白名单。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 输入 | 读面 = test.prt 真实 op 代表（腔铣 CAVITY_MILL/COPY、PTP 打点/G83），只读不保存；写面 = 内存空 Part（mill_contour 模板）独立 op，每键一 op 零串扰 |
| 输出 | 注册表（本文 §2）+ samples/camprobe-params(-2)-<ts>.txt（逐键值 + 判定行） |
| 载体纪律 | run_journal.exe 批处理（APP_NONE：NewDisplay 建件 → CreateCamSession() → CreateCamSetup("mill_contour")，反序得坏 CAMSetup，索引 §2.1） |
| 判据 | 写面：**重开（独立 builder 实例）== 写入值 → 持久；还原模板默认 → 不可写**（U-6 口径，不认未 commit 的 in-session 读值） |
| 失败语义 | 单项异常 → 该实验 FAIL 记录继续；组/op 创建失败 → 实验 SKIP（不污染其余） |
| 下游消费 | v1.5-③ 导出侧 WhiteList/NxCollect 键集扩展（读面值样例 + status 语义供校准清单解释）；Executor ParamWhiteList 增量（仅持久键）；Comparer 参数字典随之扩展 |

## 2. 键集注册表（读面实态 + 写面形态矩阵定稿；schema 键名为落点命名）

键路径符号：`b.` = 分型 Builder（腔铣 CavityMillingBuilder / PTP PointToPointBuilder）。形态列以索引 §2.2 四形态为纲。

| # | schema 键（落点） | NX 成员路径 | 宿主 op 族 | 形态 | 读面可读 + 值样例（status） | 写面持久性（判据：commit→重开；跑次见格内注） |
|---|---|---|---|---|---|---|
| 1 | strategy.cut_pattern | `b.CutPattern.CutPattern`（宿主 builder 直接成员，非 CutParameters 下） | 腔 | 类嵌套枚举（CutPatternBuilder.Types） | ✓ FollowPeriphery / Profile | **持久 ✓**（Zig，三跑 E1） |
| 2 | strategy.cut_order | `b.CutParameters.CutOrder` | 腔 | 直枚举（CutParametersCutOrderTypes） | ✓ DepthFirst | **持久 ✓**（DepthFirst，v1 c2 单跑；本批未重跑——v1 已含 commit→重开判据） |
| 3 | strategy.cut_direction | `b.CutParameters.CutDirection.Type` | 腔 | 类 + 嵌套枚举 .Type | ✓ Climb | **持久 ✓**（Conventional，v1 c3 单跑同口径） |
| 4 | strategy.finish_passes | `b.CutParameters.FinishPasses.NumberOfFinishPasses` | 腔 | int 直赋 | ✓ 0 | **持久 ✓**（2，三跑 E7） |
| 5 | strategy.multi_depth_cut | `b.CutParameters.MultiDepthCut.Toggle` | 腔 | bool 直赋 | ✓ False | **还原 ✗**（True，三跑 E2 全还原） |
| 6 | strategy.multi_depth_method（schema 现无此键；新增候选） | `b.CutParameters.MultiDepthCut.StepMethod` | 腔 | 嵌套枚举（MultiDepthCut.Types{Increment,Passes}） | ✓ Increment | **还原 ✗**（Passes+Toggle 同写，三跑 E3 双键全还原——整对象丢弃） |
| 7 | （schema 无落点；technology.tolerance 概念近似） | `b.CutParameters.BoundaryInTol`（MillCutParameters） | 腔 + 平面 | 直 double | ✓ 0 | **还原 ✗**（0.02，CAVITY 三跑 E4 + PLANAR_MILL 三跑 E6 全还原——非模板特化） |
| 8 | （同上；schema 无落点） | `b.CutParameters.BoundaryOutTol`（MillCutParameters） | 腔 | 直 double | ✓ 0 | **还原 ✗**（0.03，三跑 E5——容差族级死区，非 InTol 键级） |
| 9 | strategy.stepover.mode/value | `b.CutParameters.Stepover.StepoverType` / `.PercentToolFlatBuilder` | 腔 | 枚举 / Inheritable 叶子 | ✓ PercentToolFlat / 65、60 | **还原 ✗**（U-6 先行负结案，camprobe-stepover 三跑；主链 + StepoverLimit 均不可持久） |
| 10 | strategy.part_stock | `b.CutParameters.PartStock.Value` | 腔 | Inheritable 叶子 | ✓ 0.1（False）/ 0 | **持久 ✓**（U-6 P2 对照 + Executor [I] 实证） |
| 11 | strategy.floor_stock | `b.CutParameters.FloorStock.Value` | 腔 | Inheritable 叶子 | ✓ 0 | **持久 ✓**（Executor [I] 实证） |
| 12 | strategy.depth_per_cut | `b.DepthPerCut`（宿主 builder 直接成员） | 腔 | Inheritable 叶子 | ✓ 0 | **持久 ✓**（Executor [I] 实证） |
| 13 | strategy.hole_depth（导出现状事实键，schema 未收——executor spec §1 偏差记录同源） | `b.HoleDepth.Value`（PTP：PointToPointBuilder） | PTP | Inheritable 叶子 | ✓ 0（True=继承） | 未测写（PTP 重建 v1 不做细分——approximation 见 executor spec） |
| 14 | （schema 现无键；hole_depth_type/hole_axis_type/retract_distance 为候选扩展，不押注） | `b.HoleDepthType` / `b.HoleAxisType` / `b.RetractDistance` | PTP | 直枚举 / 直枚举 / Inheritable | ✓ Point / Vector / 0 | 未测写 |
| 15 | technology.spindle_rpm / feed_cut | `b.FeedsBuilder.SpindleRpmBuilder.Value` / `.FeedCutBuilder.Value` | 腔 + PTP | Inheritable 叶子 | ✓ 腔 2400/2000、3000/1200；PTP 3000/80、500/35（rpm status False=显式；打点 hole_depth True=继承） | 未测写（本批未探；重建侧 v1 不写转速——空件无刀路） |
| 16 | strategy.cycle / tool_drive_point | PTP：无 HoleDrillingBuilder 面（cast 编译非法） | PTP | — | **✗ 不可读**（U-1 负证：builder 公开面/BuilderProperties JSON/用户属性三路零命中） | n/a |

> 注 1（判定口径分层）：#1-4、9-12 的持久结论覆盖"commit→重开"判据；#2/#3 为 v1 首跑单跑、
> 其余持久键为 U-6/Executor 批次多跑或 [I] 级实证——**#2/#3 若进重建白名单前建议随 v1.5-③ [I] 复跑一次
> 顺带点亮**（本批范围刻意不重跑非负键，节省会话）。
> 注 2（负键结案范围）：#5-#8 结案经 2026-09-04 收口三跑（独立会话 ×3 逐条一致）——负键判定齐 U-6
> 三跑纪律；E3/E5/E6 判别实验同时排除"toggle 键级""InTol 键级""CAVITY 模板特化"三个替代假设，
> 结论范围 = **MultiDepthCut 整对象丢弃** 与 **Boundary 容差族级死区**（含 PLANAR_MILL）。
> 注 3（形态 → 可写无单调性，本批新增坐实）：直赋类内 int（#4）可写而 bool（#5）不可写；枚举类内
> #1/#2 可写而 #6 不可写——四形态表（索引 §2.2）只描述读写代码分支形态，**不描述可写性**；可写性
> 一律以本注册表按键实证列为准（U-6 教训重申）。

## 3. 性质（红线；本批全为文档/实证层，无 [U] 代码改动）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| R-1 | 读面键集无假阳性：每个候选键有实测值或明确负证（值样例/不可读异常入表） | 实证口级纪律 | §2 表 #1-16 每行"读面可读"非空（值或 ✗） | [实证] |
| R-2 | 写面判据统一：持久 = 重开（独立 builder）== 写入值；还原 = 重开回模板默认 | U-6 口径 | 探针日志判定行与 §2 表一致 | [实证] |
| R-3 | 负键多跑齐备：每个还原键 ≥2 独立会话复现（本批三跑逐条一致） | U-6 三跑纪律 | 163751/163823/163850 三 txt 判定行相同 | [实证] |
| R-4 | 形态归并禁区：不可写性不按形态推断、不合并行（#4 vs #5、#1/#2 vs #6） | U-6 教训（本批扩界） | §2 表按键分列 + 注 3 | [doc] |
| R-5 | 下游红线：v1.5-③ 重建白名单只含持久键（#1-4 增量 + 既有 #10-12）；#5-9 拒收 + diag 不静默 | 实证口级纪律（executor spec PRE-4 维持） | v1.5-③ 实现 diff 审阅 | [doc] |
| R-6 | 回填完整：索引 §2.1/§2.5、schema $comment、exporter/comparer spec v1.5 注记同步 | CLAUDE.md 回填规则 | 改动 diff 审阅 | [doc] |

## 4. 算法/改动面（步骤 → 性质映射）

1. v1 首跑（读面 S2 + 写面矩阵 S3 六键）→ 候选键集初表 → R-1
2. 收口三跑（E1-E7 复刻复跑 + 邻接判别，判定行自动分写）→ 负键定稿 → R-2/R-3
3. 本纪要落档（§2 表 + 注 1-3）→ R-4
4. 索引/schema/spec 回填（任务 3，见 §5 回填清单）→ R-6
5. v1.5-③ 实现（**不在本批**）：导出 WhiteList/NxCollect 键集扩展 → Executor ParamWhiteList 增量
   → Comparer 参数字典扩展 → [U] + [I] 三连跑 → R-5
终止性：有限键集 + 有限实验（7/会话）；无循环新增。

## 5. 决策与证据（2026-09-04 收口）

- **D-7（#5-#8 处置）= γ 负结案**（U-6 同款）：MultiDepthCut 整对象与 Boundary 容差族公开 .NET 面
  无有效写入通道，重建侧拒收 + diag。证据：E2/E4 三跑复现（复刻 v1 c5/c6）+ E3/E5/E6 邻接判别
  （排除键级/模板特化替代假设，D-8）；锚点 E1/E7 三跑持久 = 会话写路径健康，负结论为参数族专属。
- **D-8（结论范围声明）**：替代假设排除链——E3 = StepMethod+Toggle 同写双还原 → 非 toggle 键级死区，
  MultiDepthCut **整对象**丢弃（与 U-6 stepover 复合对象机制同族）；E5 = OutTol 同还原 → 非 InTol
  键级，Boundary **容差族**死区；E6 = PLANAR_MILL 同还原 → 非 CAVITY_MILL 模板特化。
- **机制残留注记（未决，不影响决策）**：与 U-6 同款——为何同族部分键（#10-12 Inheritable 直属）
  可持久而复合对象（stepover/MultiDepthCut）与容差族直 double commit 后必回填模板默认，公开面不可
  解释；共性观察 = 回填对象含"复合对象 + 校验类直赋面"，均无公开开关。重建侧维持拒收策略。
- **导出侧立场（读面不受影响）**：#7/#8 读面可读（真实值可采），若未来 schema 增 tolerance 键可由
  导出直写 + 重建侧拒收 diag（本批不新增 schema 键，避免无消费者结构——comparer D-4 口径）。
- **必做回填清单（任务 3）**：① 索引 §2.1「两处写入未保留」BoundaryInTol 项 → 本批正式负结案注记
  （链接本文）；② 索引 §2.5 不存在项清单 → 加 MultiDepthCut 整对象 + Boundary 容差族可写通道条目
  （U-6 同款句法）；③ schema strategy.multi_depth_cut $comment → 负结案注记（值域/读可写不可事实）；
  ④ exporter spec §4 A6 / comparer spec §5 D-1 → v1.5-③ 依据指针（本文档）。

## 6. 不在本批范围

- **v1.5-③ 三侧实现**（本纪要即其键集/持久性依据，含 #2/#3 入白名单前 [I] 复跑、comparer 参数字典、
  校准清单更新）；
- #15（rpm/feed_cut）等未测键的写面探针（如 v1.5 重建要写转速进给再开批，本批不探）；
- PTP 细分重建（cycle 细分参数 U-1 负证，#16）与 PTP approximation（executor spec 已声明口径）；
- schema 结构改动（strategy 无 tolerance/multi_depth_method 键——缺省保持，不押注无消费者字段）。
