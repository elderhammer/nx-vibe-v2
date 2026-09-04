# U-6 Stepover 有效写入通道探针规格（spec-before-code 纪要落档，2026-09-04）

> 状态：**探针收官（2026-09-04，三跑 samples/camprobe-stepover-20260904-{152830,153003,153051}.txt，
> 探针源 src/NXPlugins/Journal/CamProbeStepover.cs）→ 收口 = γ 负结案**（§6）。
> 残余"有效写入通道未明"→ 关闭为"公开 .NET 面无有效写入通道（8 通道形态全负，
> commit 后必还原模板默认）"；机制残留注记见 §6。
> 需求源：nx-plan-exporter-spec.md §5 U-6（[T] 残余：Stepover 整链 commit 写入静默还原模板默认，
> 有效写入通道未明）+ nx2406-install-index.md §3 项 3 残余；本批 = 收口该残余。
> 事实源：NXOpen.xml / 反射 NXOpen.dll / UGOPEN C++ 头 + 官方样例
> `UGOPEN\SampleNXOpenApplications\DotNet\CAM\CornerSetRadiusAndLimitCycleAll.vb`
> + samples/camprobe-finalize-20260904-010401.txt（E 系列既有负证据）。

## 0. 一段话结论

E 系列只证明了「主链（`CutParameters.Stepover.StepoverType` + `PercentToolFlatBuilder.Value`）
commit 后静默还原」，但**未区分失效环节**（值写是否入对象层 / dirty 是否记账 / commit dump 语义），
也未试过全部邻接通道。本探针用判别性实验钉死环节并穷举替代通道（StepoverLimit /
Distance+Intent / Planar 对照 / 方法组级 / 直接 int 成员 / 二次 builder），
无论成败都产出正式结案（α 主链正 / β 邻接通道正 / γ 全负），并同步回填
索引 §2.1/§2.5/§3、exporter/executor spec U-6、schema $comment（执行后动作清单见 §6）。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 载体 | `run_journal.exe` 单文件 journal（无 System.Runtime.Serialization 依赖，与 CamProbeFinalize 同款批处理） |
| 会话纪律 | 空会话：`NewDisplay` 建件 → `Session.CreateCamSession()` → `CreateCamSetup("mill_contour")`（反序得坏 CAMSetup，索引 §2.1） |
| 输入 | 无（纯写环境）；实验隔离：每实验独立新建 op/组，op 名 FZ_P0..P8，互不复用 |
| 输出 | `samples/camprobe-stepover-<ts>.txt`；每行即时落盘（硬崩留痕）；每实验记录 写前/写后(未Commit)/Commit 后重开 三态读数 |
| 纪律 | 内存部件不保存；builder 用毕 Destroy；stepover 主链重开判据 = 新 builder 重开读值 |
| 失败语义 | 环境不可用（建件/CAM 会话失败）→ 中止并注记 GUI 补跑；单项异常 → 该实验 FAIL 继续（不污染其他实验） |

## 2. 数据结构要点（本次三路实证新事实，均 2026-09-04 静态实证）

- `StepoverBuilder : NXOpen.TaggedObject`（C++ 头 CAM_StepoverBuilder.hxx:62 + 反射）——**非 Builder，
  无独立 Commit**；唯一提交通道 = 父 op/组 builder.Commit。
- 叶子类型（反射）：`PercentToolFlatBuilder : InheritableDoubleBuilder`（**与 PartStock 同类**——
  Value/ValueIntent/ExpressionString/InheritanceStatus；排除"类级写缺陷"假设）；
  `DistanceBuilder : InheritableToolDepBuilder`（多 `.Intent : ParamValueIntent` = `PartUnits|ToolDep|
  Function|ToolFluteLength|LengthPercent`）；`NumberOfStepovers : Int32`（**直接 int，非 Inheritable**）。
- `MillCutParameters.StepoverLimit : InheritableDoubleBuilder`（同 PartStock 成员深度）。
- 官方唯一 stepover 族写码证据 = CornerSetRadiusAndLimitCycleAll.vb:105
  `operationBuilder.CutParameters.StepoverLimit.Value = 150.0`（后接父 builder.Commit()）。
- `MillMethodBuilder.CutParameters : CutParameters` 存在（方法组默认值通道可达），
  工厂 = `NCGroupCollection.CreateMillMethodBuilder(group)`（反射实证）。
- schema 现状：`strategy.stepover = {mode(11 词枚举), value}`；导出侧 NxCollect **不读 stepover**
  （test.plan.json 键集零命中，读面白名单仅 part_stock/floor_stock/depth_per_cut/hole_depth）；
  重建侧 ParamWhiteList 拒收 + diag（executor spec U-6 挂出同源）。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证）

| 编号 | 断言 | 判据 |
|---|---|---|
| S1 | 实验隔离：实验 N 的写入不影响实验 N+1 的写前读数 | 各实验写前 StepoverType/Value 均为模板默认（70/PercentToolFlat）或显式记录 |
| S2 | 判别充分性：α 分支至少一实验重开值 = 写入值（同 tolerance 0.0001） | P2/P5/P8 重开读数 |
| S3 | 结论三态可判定（α/β/γ），无"未明"残留 | 运行输出对照表 + 本文件 §6 收口记录 |
| S4 | 环境可复现：P0（E1 复刻）重开 70 = 基线成立（否则本批无效重跑） | P0 读数 |
| S5 | 收口必含回填：索引 §2.1/§3、executor spec §5 U-6、schema stepover $comment 三处 | 改动 diff 审阅 |

## 4. 算法（实验树 → 性质映射；每实验独立 op，commit→重开为终判）

| 实验 | 写入动作 | 判别意图 | 性质 |
|---|---|---|---|
| P0 | 复刻 E1：PercentToolFlat.Value=50 → commit → 重开 | 环境复现基线（预期 70） | S4 |
| P1 | builder A 写 PartStock=0.3 + Stepover 50 → **不 commit**，destroy A → 扫 op.BuilderProperties JSON 同两键 Value | **快照语义判别**：JSON 反映对象层与否？（执行修正见 §6：对照自证 JSON=已提交态快照 → 不作传播判别，由 P2/P8 承担） | S2 前置 |
| P2 | 同 builder 双写 PartStock=0.3 + Stepover 50 → commit → 重开 | **dirty 记账判别（决定性）**：PartStock 持久 + stepover 还原 = stepover 叶子从不入 dirty 集（stub 定论）；stepover 持久 = E 系列另有原因 → 转 P5/P8 | S2/S3 |
| P3 | `StepoverLimit.Value=75`（越界探值域）→ 200（界内终验）→ commit → 重开（执行修正：150=模板默认无判别力，结果见 §6） | **邻接通道**（官方样例同款写法）：持久 = β 通道候选 | S3 |
| P4 | Constant 型：`DistanceBuilder.Intent=PartUnits` + `Value=1.5` → commit → 重开 type/value/intent | Intent 缺省假设排除 | S3 |
| P5 | mill_planar/PLANAR_MILL op 上复刻 P2 | CAVITY_MILL 特化排除 | S2/S3 |
| P6 | `CreateMillMethodBuilder(方法组)` 写同链 → commit → 重开 | 方法组级默认值通道持久性 | S3 |
| P7 | type=Number + `NumberOfStepovers=4`（直接 int）→ commit → 重开 | 直接成员 vs Inheritable 叶子差异 | S3 |
| P8 | builder A 写 Stepover 50（不 commit，destroy）→ builder B 空 Commit → 重开 | 暂存态 flush 假设（写停在对象层暂存，任意后续 Commit flush） | S2/S3 |

终止性：有限实验集（8），每实验 ≤3 次 builder 会话；单项失败不影响后续（S1）。

## 5. 探针前事实与决策锚点（证据落点）

- 既有负证据：camprobe-finalize-010401 E1-E6（写 50→70、Constant+1.5→PercentToolFlat/15、type 亦还原；
  PartStock 同链对照持久）→ 主链整对象 commit 丢弃。
- 官方样例语料：StepoverLimit 150% 是官方 QA 脚本唯一 stepover 族写入（同 Inheritable.Value 形态）。
- 机制解释候选（探针后按结果收口，不预设）：stub setter 不入 dirty 集 / wrapper 缓存不传播 /
  对象层暂存需 flush / CAVITY 特化 / 组级方可写。
- 收口分支与后续动作见 §6；**读侧 stepover 入导出（NxCollect）为独立增强决策，不混入本批**。

## 6. 收口记录（运行后回填；2026-09-04 结论 = γ 负结案）

**γ 结案定稿**：公开 .NET 面不存在 stepover 有效写入通道——8 通道形态全负，
commit 后必还原模板默认（CAVITY_MILL 与 PLANAR_MILL 一致）。三跑证据对照：

| 实验 | 结果（152830 → 153003 → 153051 复现一致） | 判读 |
|---|---|---|
| P0（基线） | 写 50 → 重开 70 | 环境可复现，E 系列复刻 ✓ |
| P1（JSON 语义） | 写 PartStock=0.3/stepover=50（不 commit）→ JSON 中 PartStock 仍 0、PercentToolFlat 仍 70 | **BuilderProperties = 已提交态快照**（对照 PartStock 亦不反映未提交写入）→ 无实时视图，P1 不作传播判别 |
| P2（dirty 记账） | PartStock 0.3 持久 / stepover 还原 70（同一 commit） | 直属 Inheritable 叶子可写；Stepover 复合对象丢弃 |
| P3（StepoverLimit） | 75 → **Commit NXException "must be between 100 and 300 percent"**；200 → Commit OK → 重开仍 150 | 写入可达 NX 校验层（与主链静默丢弃不同），但界内值 commit 后仍回填模板默认 → 不可持久 |
| P4（Distance+Intent） | Intent=PartUnits 设后仍还原（Constant→PercentToolFlat、1.5→15） | Intent 假设排除 |
| P5（Planar 对照） | 50 → 40（mill_planar 模板默认） | 非 CAVITY_MILL 特化 |
| P6（方法组级） | 方法组 `CutParameters` 运行时类型 = 基类 `CutParameters`（铣族方法组**无 Stepover 成员面**） | 组级默认通道不存在 |
| P7（直接 int） | type=Number + NumberOfStepovers=4 → 重开还原 0 | 直接 int 成员同样丢弃 |
| P8（暂存 flush） | A 写 50 → destroy → B 空 Commit → C 重开 70 | 无"暂存 flush"通道 |

**机制残留注记（未决，不影响决策）**：为何 PartStock/FloorStock（同为 CutParameters 直属
InheritableDoubleBuilder）可持久而 stepover 族（含直属的 StepoverLimit）commit 后必回填模板默认，
公开面无法解释（可能 NX 内部对 stepover 族执行"模板默认回填"，无公开开关；StepoverBuilder 非
Builder 无独立 Commit、无 MakeLocal 类成员可查证）。**重建侧维持拒收 + diag（executor U-6 关闭注记
指向本文）；UI 手工/内部通道不在本仓库合同能力内**。

**回填动作（本批完成）**：
- 索引 §2.1 收官批增补（P3 值域 [100,300]% + 校验层可达但不可持久、P1 JSON 快照语义、
  P6 方法组 CutParameters=基类）；§2.5 不存在项清单加 stepover 族可写通道条目；
  §3 项 3 残余 → 划勾关闭（唯一 [T] 清零）。
- exporter spec §5 U-6 条目 → 负结案关闭注记；executor spec §5 stepover 行补收口指向；
  research 附 B 项 5 残余句更新。
- schema `strategy.stepover` $comment 定稿（负结案 + 值域/校验层可达事实）。
- ParamWhiteList 维持拒收（γ 无 α/β 分支动作）；ExecutorPropertyTests PRE-4 拒收断言**不改**。

> 收口旁证资产：samples/camprobe-stepover-20260904-152830.txt（首跑）、-153003.txt（P3 越界 75
> 校验异常；P1 日志按快照语义修正）、-153051.txt（P3 界内 200 终验回填 150）；探针源
> CamProbeStepover.cs 入库。
