# plan tools[].type 词集实证化规格（U-7 收口，spec-before-code 纪要落档，2026-09-04）

> 状态：**纪要落档（2026-09-04）**；范围决策 D-3 = A′（NX 词集收编）；残留清理分两批（本批仅 tool
> 词集 + 注释校准）。探针 CamProbeU7 收官（ok=3/fail=0，源 camprobe-u7-20260904-115251.txt）：P1/P2/P3
> 全点亮——[T] 清空（§5）。**实现待用户指令**。
> 需求源：docs/nx-plan-executor-spec.md §5b U-7（导出侧补 CutterSubtype→schema type 枚举；
> GetNameOfType 语言漂移一并解决）+ §7 D-2 挂出；上游事实：docs/nx2406-install-index.md §2.1（刀具读回）
> / §2.5（不存在项清单）。
> 合同：schema/autocam-plan.schema.json v3.0（本批 = 词集替换 + 可选字段，additive，contract_version 不变）。

## 0. 一段话结论

U-7 正解通道（静态实证）：`NXOpen.CAM.Tool.GetTypeAndSubtype(out Tool.Types, out Tool.Subtypes)`
——`Tool : NCGroup`（.NET 反射 base chain 实证），组级调用、全家族覆盖、**语言无关**、NX7.5 起、
License None。取代 P1 遗留的猜试面（`CreateMillToolBuilder`/`CreateDrillStdToolBuilder` 试错 +
仅铣族有 `MillToolBuilder.CutterSubtype`）+ 语言敏感的 `GetNameOfType` 家族串。
schema `tool.type` 词集从"零出处 14 CAPP 词"（D-2 违例根源）替换为 **NX `Tool.Types` 原文 14 词**
（Mill/Drill/Turn/…，$comment 标注 NX2406 出处），新增可选 `tool.subtype` 收编 NX `Tool.Subtypes`
原文（Mill5/DrillStandard/…），导出直写零损失；执行侧重建注册对表按探针 P2 校准（模板串 ↔ 枚举）。
残留 CAPP 清理（operation_type/feature_type/云端预留等）另行一批，不混入。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 输入 | 导出侧 = NX 会话手编 prj 的刀具 NCGroup；执行侧 = plan.json tools[] |
| 词集来源 | `NXOpen.CAM.Tool.Types`（14 值）与 `Tool.Subtypes`（~50 值）枚举**原文串**（XML/反射三路可复核，见 nx2406-install-index.md §2.3 实证法） |
| schema 改动（additive） | `tool.type` 枚举替换为 NX Types 词；新增可选 `tool.subtype`（NX Subtypes 词，可缺省）；`required` 不变（tool_id/type/diameter）；其余 tool 字段不动 |
| 调用序列 | 导出：机树遍历（不变）→ 每真刀 NCGroup → `as Tool` → `GetTypeAndSubtype` → 直写 type/subtype；执行：schema 枚举命中重建表 → 注册对；未命中回退旧家族串关键词表（旧 plan 兼容） |
| 失败语义 | cast/读回异常 → tool 级 error diag（`TOOL_TYPE_UNREADABLE`）+ 该刀不入 plan；引用它的 op 随之 error（与 TPL_UNKNOWN 同口径，不静默） |
| 版本兼容 | 词集替换不破坏结构；旧 test.plan.json（家族串）经回退分支仍可重建（同 D-2 行为）；contract_version 维持 3.0 |

## 2. 数据结构要点

- `ToolItem`（PlanExporter/Model.cs）增 `NxType`/`NxSubtype`（string，可空）；导出经 ExporterCore 直写
  `ToolJson.type/subtype`——**无归类表**（词集 = NX 原文，INV-U7-1 直写语义）。
- `ToolJson`（Doc.cs）镜像 schema 增 subtype；PlanJson/序列化复用现有字典/数组形态。
- 执行侧重建注册对表 = **(NX Types, NX Subtypes) → 注册对**（如 `(Mill, Mill5) → (mill_planar, MILL)`）；
  细目经探针 P2（新建注册对读回）校准——模板 subtype 串（`STD_DRILL`）与 NX 枚举词（`DrillStandard`）
  属不同词汇表，对应关系以实测为准（§5 表）。
- 兼容分支：旧 family 关键词表（ToolFamilyMap 现表，中/英文键）保留为回退，顺序 = 枚举表 → 关键词表 → 默认铣+Inferred。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证 [T]=待实测）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| PRE-U7-1 | 导出每把真刀 `as Tool` + `GetTypeAndSubtype` 成功（容器组已排除） | 通道 A 实证（P1，六把 6/6） | cast 失败/异常 → TOOL_TYPE_UNREADABLE error | [U]（替身）+[I]（已实证） |
| POST-U7-1 | 产出 plan `tool.type` ∈ NX Types 词、`subtype` ∈ NX Subtypes 词（可空），schema 校验过 | 词集替换定义 | 校验器对夹具全过；原 D-2 违例夹具反证 | [U] |
| INV-U7-1 | type/subtype 值 = NX 枚举原文直写，无中间归类表 | 词集=出处 | 替身读回 (Mill,Mill5) → 输出恰为 "Mill"/"Mill5" | [U] |
| INV-U7-2 | 重建注册对表覆盖当前注册对集（mill_planar/MILL、hole_making/STD_DRILL），每行经 P2 实测校准 | 创建实证（P2，表见 §5b） | 表断言：行数与实测对一一对应 | [U]（表已校准） |
| INV-U7-3 | 旧家族串回退分支保留：枚举表未命中 → 关键词表 → 默认铣 + Inferred（现状不变） | D-2 兼容 | 家族串夹具仍达 D-2 结果 | [U] |
| INV-U7-4 | 读回失败的刀不入 plan + error diag（不静默缺字段） | 导出纪律 POST-3 | 替身抛错 → 该刀缺 + diag 含 code/scope | [U] |
| —（集成） | test.prt 重导 → schema 落盘复验 PASS；Executor 重跑 I-2 回读对照全 PASS | 回归 | 集成清单（executor-run/reopen 模式复跑） | [I] |

## 4. 算法（步骤 → 性质映射）

- 导出 A：机树遍历（现状 WalkTools）→ 真刀 `as Tool` + `GetTypeAndSubtype` → 直写 → PRE-U7-1/INV-U7-1；
  异常 → TOOL_TYPE_UNREADABLE + 剔除 → INV-U7-4。
- 执行 A：tools[] 逐把解析 → type/subtype 查重建注册对表 → 命中建组；未命中 → family 回退 → 默认 +
  Inferred → INV-U7-2/INV-U7-3（ExecutorCore 现路径仅键源变化，参数写入面不变）。
- 终止性：有限表 + 有限刀具集；序列化/校验路径不变。

## 5. 决策与实证记录

- **D-3（已确认 2026-09-04）= A′**：type 词集替换为 NX `Tool.Types` 原文 + 新增可选 `subtype`（NX
  `Tool.Subtypes`）。依据：仓库无 CAPP 事实资料（PRD/特征→工序映射表属外部模块未挂载，见
  nxopen-research.md 顶部关联文档行）；原 14 词（end_mill…counterbore）全仓文档零出处（schema $comment 自认
  "铣+孔 MVP 集合"），D-2 违例实态坐实。**不押注未挂载外部模块词汇**；外部素材挂载后词集演进另议。
- **D-4（已确认）= 分两批**：本批仅 tool 词集 + 相关注释校准；operation_type/feature_type/云端预留
  字段（face_ids/edge_ids/face_anchors 占位）与文档 CAPP 注记清理 = 紧随其后的独立合同修订批（另开纪要）。
- **探针 CamProbeU7**（源：src/NXPlugins/Journal/CamProbeU7.cs；输出：samples/camprobe-u7-<ts>.txt）：
  - P1 六把库刀具 `as Tool` + GetTypeAndSubtype 实态（下转型可行性，[T] 关）。
  - P2 新建 (mill_planar,MILL)/(hole_making,STD_DRILL) 读回 → **注册对 ↔ NX 枚举对应表**（INV-U7-2 依据）。
  - P3 对照 GetNameOfType 家族串 + builder 类型 + CutterSubtype（旧通道 vs 新通道并录）。
  > 实证结论回填处：见下方 §5a/§5b（2026-09-04 已实测，源 camprobe-u7-20260904-115251.txt，ok=3/fail=0）。

### 5a. P1 实测（库刀具 GetTypeAndSubtype，六把全成功）

| test.prt 库刀 | `as Tool` | GetTypeAndSubtype | 对照：GetNameOfType（语言敏感） | 对照：CutterSubtype（旧通道） |
|---|---|---|---|---|
| 17.0 / 13.94 / 9.96 | ✓×3 | (Mill, Mill5) ×3 | Milling Tool-5 Parameters | Mill5（builder 可转 MillToolBuilder） |
| D6.0X90中心钻 | ✓ | (Mill, MillChamfer) | Chamfer Mill | ChamferTool（**命名与 MillChamfer 异**，见 §6 注记） |
| 8.5 / 17.5 | ✓×2 | (Drill, DrillStandard) ×2 | Drilling Tool | 无（builder 运行时 DrillStdToolBuilder，`as MillToolBuilder`=null——旧通道缺口） |

> 结论：NCGroup → `Tool` 下转型 6/6 成功；`GetTypeAndSubtype` 与语言无关（本会话家族串为英文，
> 枚举值不变）；钻刀族由通道 A 补齐旧通道盲区。**PRE-U7-1 [T] 关闭**。

### 5b. P2 实测（新建注册对读回校准表）

| 注册对（Create 字面量） | 读回 (Types, Subtypes) | 重建表行（INV-U7-2） |
|---|---|---|
| (mill_planar, MILL) | (Mill, **Mill5**) | (Mill, Mill5) → (mill_planar, MILL) |
| (hole_making, STD_DRILL) | (Drill, **DrillStandard**) | (Drill, DrillStandard) → (hole_making, STD_DRILL) |

> 注：注册对 `MILL`/`STD_DRILL` 建出的默认刀型读回为 Mill5/DrillStandard——执行侧"枚举→注册对"
> 实为**按默认刀型建通用组 + 数值直填**（D-2 现状延续）；将来按 subtype 精准建刀（如 MillChamfer
> 型）需另探注册对/参数通道，超出 v1，不阻塞本批。

## 6. 冲突与文档回填点

- **nx2406-install-index.md §2.5 表述修正**："CAMObject 的 subtypeName/子类型读回成员零命中"不准确——
  限定为 **NCGroup/CAMObject 层零命中；`CAM.Tool.GetTypeAndSubtype` 例外**（NX7.5 起、License None、
  工具专用，Tool : NCGroup 子类）。
- **同型双命名观察**（camprobe-u7 P1 实证）：中心钻（Chamfer Mill 家族）CutterSubtype=`ChamferTool`
  而 GetTypeAndSubtype Subtypes=`MillChamfer`——同一刀型两个枚举命名不同 → 回填索引 §2.3，执行/导出
  两侧映射一律以 `Tool.Types/Subtypes` 为基准，不再混用 CutterSubtype 命名。
- nx-plan-executor-spec.md §5b U-7 挂出项 → 关闭注记（指向本文档）。
- executor spec §5 "GetNameOfType 语言敏感"风险 → 词集通道落地后仅影响回退分支（旧 plan）。
- schema tool 定义 $comment 更新为 NX 出处标注（原"MVP 集合"注记删除）。

## 7. 不在本批范围（第二批备查）

operation_type 33 词（产出实态 milling/drilling/other 违例、无消费者）、feature_type 17 词（AP224 口径、
恒 geometry_group）、geometry_ref face_ids/edge_ids/face_anchors 占位与 anchor_point required-null 矛盾、
machines[]、全文档 CAPP/云端注记校准、PlanValidator 枚举收紧口径——各占位的删除/收窄影响已在对话纪要
列清单，另开批逐项过。

> **D-4 已执行（2026-09-04，docs/nx-plan-contract-cleanup-spec.md）**：operation_type/feature_type → 自由串
> 两档（X）；geometry_ref 字段族/machines/machine_ref/blank_ref → 删除（i）；索引回填三项已落。PlanValidator
> 枚举收紧仍挂起——统一在 A′ 实现批收尾开启。
