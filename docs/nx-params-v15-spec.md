# v1.5-③ 参数面扩展规格（spec-before-code 纪要落档，2026-09-04；S1 范围）

> 状态：**已实现收官（2026-09-04）**——[U] 全量 93/93 全绿（V15-* 十条红线 + 83 旧回归）；[I] 三连跑点亮：
> I-1 重导（adapter-run-20260904-194935：新形状落盘复验 PASS，腔 op 6 新键 + 全 op rpm）、
> I-2 复跑（executor-run-20260904-195159：ok=17 fail=0，4 持久键 + rpm 真实写入无异常、boundary 拒收
> diag、test.rebuilt-195208.prt）、I-3 终跑（comparer-run-20260904-200339：issues 19→5 全部校准可解释，
> 腔对腔 cut_*/finish/boundary/rpm 由"键缺席"转全 PASS = 写入持久终判 + technology 维首亮；
> 残余 5 = PTP 族 4（hole_depth↔bottom_stock 键错位已知近似）+ tool#4 类型（U-7 已知））。
> 期间修正：ComparerAdapter 参数语义改单参 B 覆盖（NX Execute 对话框单参实况实证，200022 失败 → 200339 修复验证）。
> D-9 范围 = S1（最小可比集）；P0 技术探针已定案
> （DCJS 联合值字典 round-trip 无损 + 旧形状需归一 shim，探针源 .claude/tmp/DcjsProbe.cs，pass=8/fail=0）。
> 需求源：docs/nx-plan-comparer-spec.md §5 D-1（策略/技术全参数面 = v1.5 排队）+ §2（参数字典键
> 即现有回读面）；依据：docs/nx-param-registry-spec.md（参数键集注册表：读面 15 可读 + 写面 4 持久
> 4 还原，本批红线）。
> 合同：schema/autocam-plan.schema.json v3.0（本批 = strategy 三枚举词集换 NX 原文 + $comment，
> 值形态说明注记；additive 扩展，contract_version 不变）。
> 工程惯例：值联合通道为**结构性新增**（贯通导出/重建/对比三侧），行为与语义均按键实证，不按形态推断。

## 0. 一段话结论

v1.5-③（S1）= 把「可比参数面」从 3 数值键扩展到含枚举的策略键，端到端打通「gt 导出 → plan 存档 →
rebuilt 写入 → 采集回读 → 逐键比对」：**值通道从 double-only 扩为联合值 {N(数值), S(枚举串)}**（P0 已证
DCJS round-trip 无损，EmitDefaultValue=false 下 N=0 与缺值可区分）；**写面表加注册表 4 持久键**
（cut_pattern/cut_order/cut_direction 枚举 + finish_passes 数值，均 v1.5-④ 收口三跑实证持久），
重建侧首次可复刻策略参数 → Comparer 策略维度由"缺席"转真对照（预期腔对腔 PASS，校准清单更新）；
rpm 走既有 tech: 前缀 + 既有白名单（spindle_rpm），首次全链点亮 technology 维度。
**S1 不含**：multi_depth_cut(bool，S2)/stepover 嵌套对象/PTP 细分键/feed_cut（S3）——负结案键或
无重建收益键不为存档押注（D-4/U-7 无消费者纪律）；boundary_intol/outtol **仅导出与比对**（数值零
结构成本；写侧拒收维持 U-6 同族负结案——当前 gt=0=模板默认 → 无 compare 噪音，未来 gt 设非 0 →
必差键显性化，校准清单已知缺口）。
**V15-INV-1 关键事实（P0）**：旧形状 plan（strategy/technology KV 数组 Value=裸 number）在 union
字典类型下**反序列化必抛**（DCJS 状态机错）→ 加载路径必须插**归一 shim**（纯文本改写 `"Value":<n>`
→ `"Value":{"N":<n>}`，P0 已证可行；自产新形状无裸 number → shim 幂等）。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 键集（S1，D-9） | 导出+采集+比对新增（腔铣族）：cut_pattern/cut_order/cut_direction（枚举 kind，值 = NX 枚举原文串，U-7 直写先例）、finish_passes/boundary_intol/boundary_outtol（数值 kind）、tech:spindle_rpm（数值；technology 段）。PTP/Drilling 族不新增键（PTP 独有键重建侧无对应面，延后零损失）。写侧 = 4 持久键 + rpm（白名单既有） |
| 值通道 | `ParamValue { double? N; string S; }`（bool? B 备用字段随 P0 定案带上，本批无 bool 键）——贯通 Model.Params / Doc.OperationJson.strategy/technology / PlanJson 序列化 / ExecutorCore / RebuildPlan.ParamInstruction / NxCollect / ComparerCore。kind 由注册表按键固定，**不按值推断** |
| 词集 | plan 枚举键值 = NX 枚举原文（词名 = .NET ToString，纯逻辑侧用 NxParamWords 静态词集校验，不引用 NX 类型）；schema strategy 三枚举词集替换为 NX 原文（从 NXOpen.xml F: 全词拉取，附 $comment 出处） |
| 序列化 | DataContractJsonSerializer 不变；KV 数组形状不变，Value 变 wrapper 对象（见 §2）。Deserialize 前插归一 shim（仅对旧形状生效，幂等） |
| 兼容 | 旧 plan（无新键、Value 裸 number）经 shim 解析行为不变（V15-INV-1）；contract_version 维持 3.0 |
| 失败语义 | 枚举词未知（不在 NxParamWords）→ 该键 error diag（PARAM_ENUM_UNKNOWN）+ 不入指令（不静默）；写侧未知键 → PARAM_UNSUPPORTED warning（现状机制不新增）；单键失败不中止（沿 POST-2 口径） |
| 下游 | Executor/Comparer 消费同一 ExportSnapshot/PlanDocument union；schema validator 词集随 schema 更新；[I] 三连跑收口（§6） |

## 2. 数据结构要点

- `ParamValue`（Model.cs，纯逻辑）：`[DataContract] { [DataMember(EmitDefaultValue=false)] double? N; string S; bool? B; }`。
  P0 实证：仅设字段落盘（{"N":0.1}/{"S":"…"}/{"B":false}）；N=0/B=false 与缺值可区分；round-trip 无损
  （半值 2400.5 精确往返）。**kind 归属 = 注册表**（数值键用 N、枚举键用 S），解析侧校验（V15-PRE-1）。
- `OperationItem.Params`：`Dictionary<string,double>` → `Dictionary<string,ParamValue>`（tech: 前缀机制
  不变，ExporterCore 分流时按键前缀 + 值原样搬）。
- `OperationJson.strategy/technology`（Doc.cs）：同上 union（落盘 KV Value = wrapper）。
- **归一 shim**（PlanJsonSerializer.Deserialize 前置，纯文本）：全文件将 `"Value":<number>` →
  `"Value":{"N":<number>}`。只匹配裸 number（旧形状独有）；新形状 `"Value":{"N":…}` 首字符 `{` 不命中
  → 幂等。实现锚定 V15-INV-1（夹具双跑：旧文本直解 = 抛；shim 后解 = 等价）。
- 写面表（ParamWhiteList）条目从 `键→成员路径` 升级 `键→{成员路径, kind}`；新增 4 键：
  cut_pattern → `CutPattern.CutPattern`（enum）、cut_order → `CutParameters.CutOrder`（enum）、
  cut_direction → `CutParameters.CutDirection.Type`（enum）、finish_passes →
  `CutParameters.FinishPasses.NumberOfFinishPasses`（number）。kind = "enum" 的条目另带 NxParamWords 词集。
- `ParamInstruction`（RebuildPlan.cs）：持 MemberPath + kind + double? N + string S（替代现 double 单值；
  纯逻辑不引 NX 类型）。
- NxParamWords（PlanExporter/NxToolWords.cs 同目录新表或同文件）：cut_pattern 36 词 / cut_order 3 词 /
  cut_direction 5 词 = NXOpen.xml F: 实证词集（task 5 拉取后固化；validator 复用）。
- schema：strategy.cut_pattern/cut_order/cut_direction 枚举词集 → NX 原文（含 $comment 出处与
  "枚举值 = NX ToString 原文，重建侧 Enum.Parse 直用" 注记）；finish_passes 维持 integer；其余不动。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证 [T]=待实测）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| V15-PRE-1 | 解析后每 strategy/technology 键值 kind 符合注册表（数值键 N 有值、枚举键 S 有值）；同键 N 与 S 不同时非空 | kind 归属注册表（§2） | 夹具双 kind → 校验过；冲突夹具 → 明确 error | [U] |
| V15-PRE-2 | 写面表含 4 持久键且 kind/路径与注册表一致；负键（boundary_*/stepover/multi_depth…）不在写面表 | 注册表 D-7（负结案） | 表断言（键/kind/路径逐条） | [U] |
| V15-POST-1 | 导出：S1 键读入 Params 且落盘 strategy KV（枚举值 = NX 原文串；数值原样 round-trip 沿 POST-4） | S1 决策 + U-7 直写先例 | 夹具快照 → Build → 序列化断言值形状与词 | [U] |
| V15-POST-2 | Executor：写面表命中键 → ParamInstruction 含 kind 与值；枚举词 ∉ NxParamWords → PARAM_ENUM_UNKNOWN error + 该键不入指令；表外键 → PARAM_UNSUPPORTED warning（消息含注册表指针） | 实证口级纪律（POST-2 沿革） | 三路夹具（命中/词未知/表外）各恰 1 diag | [U] |
| V15-POST-3 | Comparer：键并集逐键按 kind 判据出条目——number 双判据（EpsLen/RelTol，回归不变）、enum ordinal equality；单侧缺失 → FAIL 不静默（沿 POST-C1） | 设计 §2.2（修订） | 数值变异 → 恰 1 FAIL（回归）；枚举变异 → 恰 1 FAIL；一致 → PASS | [U] |
| V15-INV-1 | 旧形状 plan（Value 裸 number）经归一 shim 解析后与等价新形状解析**键集与值全同**；不经 shim 直解 = 明确异常 | P0 探针（② 必抛 + ②b shim 可行） | 双夹具对比断言 + 直解异常断言 | [U] |
| V15-INV-2 | 序列化幂等：新形状 序列化→解析→再序列化 文本稳定（无逐次变形） | 工程纪律（POST-4 沿革） | 双序列化文本相等 | [U] |
| V15-INV-3 | 导出/重建/对比共享同一 union 值模型，无双轨（无残留 double 通道） | D-3 单一事实源口径 | 编译面 + 全量回归绿 | [U] |
| V15-MONO-1 | Compare 无状态幂等 + 快照只读在 union 键下维持（MONO-C1 回归） | 定义 | 双跑相等 + 快照字段不变（回归夹具扩展） | [U] |

[I] 集成验证清单（不进单测；三连跑收口，见 §6）：
- I-1 ExporterAdapter 重导 test.prt → plan 含腔 6 键 + rpm；schema 内存+落盘复验 PASS（validator 词集
  收紧后无违例）；
- I-2 ExecutorAdapter 复跑新 plan：4 持久键写入 → 回读对照 PASS（cut_pattern 等真值复刻）；rpm 首次
  写入点亮（白名单既有 case）；boundary_intol/outtol 拒收 diag 出现（PARAM_UNSUPPORTED）；reopen 复核；
- I-3 ComparerAdapter 复跑：腔对腔 cut_*/finish/rpm 由"键缺席"转 PASS（校准清单更新落档）；DRILLING
  采集读 rpm 可行点亮（rebuilt 侧 tech 键存在性）；
- I-4 校准清单更新：S1 新增键的预期 PASS/FAIL 条目与终跑逐条一致（沿 comparer spec §3 校准记录口径）。

## 4. 算法（步骤 → 性质映射）

A1 NxCollect 腔分支按注册表扩读（数值键走现 TryParam；枚举键 TryParamS 直读 NX 枚举 ToString）
   → V15-POST-1 读侧
A2 ExporterCore 分流照旧（tech: 前缀）→ 落盘 union（wrapper 序列化）→ V15-POST-1/V15-INV-2
A3 PlanJsonSerializer.Deserialize 前置归一 shim（幂等改写）→ V15-INV-1
A4 ExecutorCore：解析 union → 词集校验（NxParamWords）→ 写面表（kind）→ ParamInstruction(kind, N/S)
   → V15-PRE-1/V15-PRE-2/V15-POST-2
A5 ExecutorAdapter WriteParam switch 增 4 case（enum: NxParamWords 校验后 Enum.Parse；number: 现路径）
   → V15-POST-2 写侧（[I] I-2 点亮）
A6 ComparerCore：Params 并集逐键按 kind 分判据（N: 双判据回归；S: ordinal）→ V15-POST-3/V15-MONO-1
A7 schema 词集替换 + validator 同步 → V15-PRE-1 合同面
终止性：有限键集（7）+ 有限词集；无循环新增。P0 探针结论已回填本纪要与注册表 §5。

## 5. 决策与冲突

- **D-9 范围 = S1**（已确认 2026-09-04）：腔 6 键 + rpm。S2（multi_depth_cut bool，P0 已证 B 通道无损）
  无当期产出（恒 False）→ 不投基建；S3（stepover 嵌套对象 + PTP 细分 + feed）→ 负结案键导出入档 =
  常年必差键 + 新值形状（嵌套对象再探针），无消费者（CAPP 未挂载）→ 均延后。**升档无结构成本**：
  注册表驱动，后续按需加行。
- **P0 技术结论（已实测，.claude/tmp/DcjsProbe.cs，pass=8/fail=0）**：① wrapper {N,S,B} + EmitDefaultValue=false
  序列化形状/round-trip 无损（含 N=0、B=false、半值 2400.5）；② 旧形状（Value 裸 number）对新字典类型
  直解**必抛**（DCJS 状态机 "应为状态...Element" 异常）→ 归一 shim 必要且可行（朴素改写后 part_stock.N==0.1、
  hole_depth.N==0 双断言过）。V15-INV-1 从"类型升级兼容"修正为"shim 保证兼容"。
- **冲突（已决）**：schema 现 cut_pattern/cut_order/cut_direction 自造大写词 vs NX 原文词 → 换 NX 原文
  （U-7 INV-U7-1 直写先例，无历史违约文件——这些键 v1 无生产者）。
- 遗留冲突：无（性质全表有算法落点）。

## 6. 实现策略与验证顺序

骨架先行（每条 [U] 性质显式红占位，按 V15-* 编号命名）→ 生产类型签名先行（union 通道）→ 实现点亮
全绿（scripts/run-unittests.ps1 回归）。改动面：Model.cs / Doc.cs / PlanJson.cs / NxCollect.cs /
ParamWhiteList.cs / RebuildPlan.cs / ExecutorCore.cs / ExecutorAdapter.cs / ComparerCore.cs + ComparerModel.cs? +
三测试目录 + schema + NxParamWords（新表）+ NxToolWords.cs 邻近。落盘资产：重导 test.plan.json +
executor-run-<ts>.txt + comparer-run-<ts>.txt + 校准清单更新（I-1..I-4 逐条点亮记录）。

> 执行记录（2026-09-04）：纪要落档 → union 通道/词集/写面/判据生产改造（编译 0 错）→ V15 十条红线
> 追加入测试（93/93 全绿，含 2 条旧断言按 V15-PRE-2/POST 修订：cut_pattern 由"不可写"转可写断言、
> INV-C2 union 字段断言）→ 适配器编译（三脚本 exit=0）→ [I] 三连跑点亮（见状态行）。
> I-3 首跑（comparer-run-20260904-195504，issues=19）为**错 B 件**（B=旧 test.rebuilt.prt，13:55 U-7 资产
> 而非本批 195208 产物——executor 主名占用走时间戳兜底所致）→ 暴露 ComparerAdapter 参数语义与 NX
> Execute 单参实况不符（200022 引号整体入参异常）→ 改单参 B 覆盖 + 引号清洗 → 200339 终跑正确。
> 资产：samples/test.plan.json（v1.5-③ 形状 19:49）、test.rebuilt-195208.prt、executor-run-195159.txt、
> comparer-run-200339.txt（终跑证据）。
