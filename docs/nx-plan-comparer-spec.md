# PlanComparer 规格（spec-before-code 纪要落档，2026-09-04）

> 状态：**已实现收官（2026-09-04）**——CompareCore [U] 全绿（全量 78/78，含 Comparer 24 条）+ 共享
> 采集 NxCollect（as Tool 入选判据 + FixtureOffset 补读）+ ComparerAdapter 三连 [I] 收官：
> comparer-run-20260904-144237（终跑：issues=6 与校准清单逐条一致、fixture=1/1 闭环）、
> adapter-run-143344/142216（重导回归）、executor-run-143426（ok=17，fixture 真对照）。
> 范围决策 D-1（对比维度）= A（结构/刀具/MCS/白名单参数）与输入形态 C（单会话双件轮换采集）、
> 采集层共享 NxCollect、首跑工具链+变异校准（§5）。设计 §7 步骤 3 完成——三步闭环 v1 收官。
>
> 2026-09-05 增补（v1.5-③ 与 v2 三维收官）：v1.5-③ 终跑 comparer-run-20260904-200339（issues
> 19→5 全校准可解释，technology 维首亮，见 nx-params-v15-spec.md）；v2 带几何重建后对比三维
> （刀路 time/length、区域区数/面积和、签名面集 sigfaceset）接入，实测暴露无几何 B 件噪音与
> PTP 家族 v2 范围缺席 → CompareCore CompareV2 维 gate（三维仅腔铣族，2530c6d）；终跑
> comparer-run-20260905-192456：issues=21 与 gate 预测一致、sigfaceset=4/4、toolpath=0/8
> region=0/8，残余全为已知校准条目（I-3 验收关闭，见 nx-v2-geom-spec.md §7）。
> [U] 时点全量记录：2026-09-05 为 100/100（93 回归 + v2 七测试，V2GATE 门控 2530c6d 补入后 101）；
> 2026-09-06 现行全量 118/118（累计含 PTP 方向化 4 + RegionPairing 12 条 + 审计批 2），见 src/NXPlugins/README.md。
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
（I-2/I-4）→ **2026-09-06 校准池触底后固化（见 §7 评分规格）**。

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
- 结果模型（新，纯逻辑）：`ComparerResult = Issues[]（ComparerIssue：Key/Code/Detail/AbsDiff——key 可溯
  INV-C3、code 稳定可聚合 INV-C4）+ Notes[]（非致命注记）+ 结构统计（OpsMatched/Missing/Extra、Tools、
  Setups）+ 维度计数对（Param/Tool/Mcs/Fixture/Template/Toolpath/Region/Sig × Checks/Pass——POST-C5
  汇总由条目派生）`。（2026-09-06 修正：早期稿"OpDiffs/ToolDiffs/SetupDiffs/StructureIssues/Score/Diags"
  字段形态未实现——实现为上述条目+计数模型，无 Score/Diags 独立成员；汇总评分由适配器渲染层承担。）
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

> **校准记录追加（2026-09-05；I-4 兑现 = v1.5-③ 与 v2 双承诺回填，源 comparer-run-20260904-200339
> /20260905-192456 + 各批 spec 记录）**：
> ① **v1.5-③**（comparer-run-20260904-200339，残余 issues 19→**5**）：195504 首跑为错 B 件（旧主名
> test.rebuilt.prt）无效、200022 参数语义失败 → ComparerAdapter 改单参 B 覆盖修复；终跑残余 5 =
> PTP 键错位 4（hole_depth↔bottom_stock，重建近似可见面不静默）+ tool#4 类型 1（U-7 已知）；
> 腔对腔 cut_*/finish/boundary/rpm 由"键缺席"转全 PASS = 写入持久终判 + technology 维首亮。
> ② **v2 gate 终跑**（comparer-run-20260905-192456，issues=**21** = 预测）：结构/刀具/MCS/白名单
> 参数面残余与 200339 清单同源（PTP 4 + tool#4 1——**tool#4 项 2026-09-05 注册对扫描收口**：
> (Mill,MillChamfer)→(mill_planar,CHAMFER_MILL) 入重建表，[I] 复跑预期 -1）；v2 三维残余 = 腔 16 = OP-001/002/004 ×4
> （刀路 time/length + 区域数/面积和——归因 = feed_cut 写面缺口（注册表 #15 未测写 → 写面探针
> 候选，gt 2000/500 vs rebuilt 默认 250 为时间差主因）+ 区域分割粒度（gt 80/36/2 vs rebuilt
> 24×3））+ OP-003 ×4（空刀路——判别读探针 camprobe-v2op-191955/192013 + 七探针链 γ 定案：
> rebuilt（STEP 回导）体上下文上的引擎区域形成差异，非复刻缺口 → **永久校准条目**，详见
> nx-v2-geom-spec.md §7；副产品：腔 stepdown 实为 CutLevel.GlobalDepthPerCut，导出深度键
> 读成员修正候选）；PTP 刀路单侧缺 ×4
> 为 v2 范围缺席（PTP 无面指派）→ 由维 gate 排除不计；sigfaceset=4/4 零 SIG_FACE_DIFF。
> ③ **新增校准条目**：区域维同面复刻仍敏感（gt 80 vs rebuilt 24）→ v2.5 区域几何配对（而非
> 计数）；feed_cut 写面探针——**2026-09-05 晚三跑持久 ✓（camprobe-feedcut）→ v1.5-⑤ 已入
> 写面白名单贯通（[U] 102/102）+ [I] 点亮（203400 写入重开持久、203514 腔 feed 键双侧全 PASS）；
> tool#4 注册对收口 + ChamferLength=D/2 修复（203400/203514：tool=6/6、tool#4 条目消除）**。
> **v1.5-⑤/tool#4 复跑记录（203514，issues 21→20，验收关闭）**：残余 20 = 腔 16（OP-003 4 =
> γ 永久校准；OP-001/002/004 ×4 = **长度/区域结构差驱动**——时间已随 feed 复刻收敛（同长度下
> time≈feed 反比 8×），残余主因 = CutLevel.GlobalDepthPerCut 未复刻（gt 0.3/0.2/20/20 vs reb 1，
> v2.5 深度键修正候选））+ PTP 键错位 4。零新增未解释。
> **v2.5 深度键修正复跑记录（comparer-run-20260905-215209，issues 20→15，验收关闭）**：导出读键
> 改 CutLevel 子树 + 写面同改（registry #12，探针 camprobe-v2depth-211011 实证）→ OP-004 4 条全消
> （A 6.82s/3761.6/2 区 ≈ B 6.63s/3576.5/2 区）；OP-001 长度差 70.9%→3.4% PASS，余 time 6.3% +
> 区域 80/79 + 面积 6.6% 边缘差 = stepover 65 vs 70（#9 不可写）等已知残余；**OP-002 4 条判别
> （camprobe-v2depth-215614 gt 侧：regen = 存档 0.515s/929.6/36 区新鲜、CutLevel 0.2 确认；同参
> rebuilt 39850/118 = 43×）→ 203514"深度差驱动"系错归因，实为体上下文 γ 类差异（OP-003 判别⑦
> 同款机制第二实例：超密而非零）→ 转永久校准**。残留 15 = OP-003 γ 4 + OP-002 γ 类 4 + OP-001
> 已知残余 3 + PTP 键错位 4，零新增未解释 → 校准清单：区域几何配对须覆盖 OP-002 超密现象。
> **PTP 键错位收尾复跑记录（comparer-run-20260906-000948，issues 15→11，验收关闭）**：判定探针
> camprobe-ptpkeys（2026-09-06，P1 双会话：HoleDepth/BottomStock 写面持久 ✓；P2 双档：gt PTP
> HoleDepth 0/True=继承 + HoleDepthType=Point vs 重建 DRILLING HoleDepth 0/False + 同 Point——键值
> 0=0、深度语义近似成立）→ 两处小改：① NxCollect 孔族分支补读 hole_depth（基类 HoleDepth，P1A
> 持久实证）→ 键面对称；② 单侧缺失判据方向化（A-only → FAIL 保持；B-only = 近似模板带出的 gt 无
> 概念面参数 → note 不 FAIL，[U] 104/104）→ 复跑：**param=50/50 全 PASS**，hole_depth 双侧配对消 2、
> bottom_stock 降 note 消 2 → 残留 11 = OP-003 γ 4 + OP-002 γ 类 4 + OP-001 已知残余 3
> （stepover 60/65 vs 70 不可写 + 区域 ±1 粒度），**零未解释，校准池触底（纯结构性定案）**。
> **区域配对研究批判别链 + 开发（2026-09-06，issues 11→9 口径细化，区域维诊断化）**：
> ① 判别链（camprobe-v2regionclone-002142/002206 + v2surfdiff-002349 + v2regionfull-002615/002633）：
> **OP-002 归因链最终修订**——gt 侧同参同面克隆 = 27.4s/119 区（≈ rebuilt 118，rebuilt 本体=克隆逐位同 =
> executor 复刻完备）→ 推翻"体上下文 γ"；surfdiff 53 行差全在白名单外（非切削/stepover 60/#9/
> ReferenceTool）；regionfull 定案 = **gt 本体只加工两孔底段 18 层×2 区窄带（z 76.5–79.8）vs rebuilt
> 全程 118 层×1 区（99.8 起）**——面集级几何属性/加工范围差异（UI 集设置通道），非参数键、非体上下文，
> 修复 = 面级写通道（超出 v1 executor 模型，排队面级扩展批）。**γ 家族缩为 OP-003 唯一**。
> ② 区域配对开发（RegionItem 明细采集 + RegionPairing 分层配对纯逻辑 + ComparerCore 明细路径）：
> [U] 116/116（12 条配对新红线：判别基线 36/118、3/0 必须 FAIL + 粒度 note + 2:1 合并 + 漂移哨兵 +
> 层距漂移/端层分级）。③ 校准复跑 003738/004123：层配对按序对齐修复（绝对 z 搜索在层距微差下
> 失效——gt 0.1944 vs reb 0.1991 累积漂移）；阈值定案 NoteAreaRatio=2%（0.1% 微区 note vs 4.3%
> 整层 FAIL 哨兵）。区域维诊断化：报告从"80 vs 79"升级为"差层面积占比 + A-only 位置"。
> 2026-09-05 早两跑（comparer-run-191118/191558，
> B=test.rebuilt.prt v1 空件）为 B 防呆修复（da3fd80）前错选件，issues=43 无效、不构成校准。
> **参考刀具定案批（2026-09-06 晚，OP-002 归因链最终定案 + 键通道实施）**：
> ① 判别（camprobe-v2faceset-005612/005710 + v2dims-010501/010519 + v2reftool-135612/135652）：
> **OP-002 只切一小截 = ReferenceTool 落单键定案**——API 四路实证（hxx 继承链/SetReferenceTool
> NX7.5 + 反射 CanWrite + 样例零范式）；gt 四 op 矩阵仅 OP-002 带参考刀具（Ø17 开粗刀，v2surf-gt
> 195205 表面直读）；faceset 证 gt 集属性 ≈ 模板默认（regionfull"面集级"残余归因收回）；dims 终证
> 几何未变（26 面型参逐面全同，box ε≤0.0025 STEP 容差级）；**reftool 三重写回判别**：P1 gt 件克隆
> 写 17.0 → 读回持久 + regen 36 区 = 本体同数（无键 119）；P2 自刀 9.96 → 0 区（直径残料语义）；
> P3 rebuilt 本体写匹配库刀 → 36 区（修复窗口）。② 实施（键通道 v2.5 参考刀具批）：registry #17
> 入表（Tool 引用 → plan 直径 N 表达）+ NxCollect 腔分支采集（null 不落键）+ ParamWhiteList +
> ExecutorAdapter 写链（按径 0.001 匹配库刀，无匹配拒写 + 日志）+ schema 词典行 + [U] 116/116。
> ③ **[I] 实录（2026-09-06 14:05-14:07，adapter/executor/comparer-run 140542/140636/140734，
> 资产 v2.rebuilt-20260906-140637.prt）**：I-1 重导 plan 落键 = 仅 OP-002 `reference_tool: N 17`
> （其余 5 op 无键，schema+落盘复验 PASS）；I-2 重建写行 = `CAVITY_MILL_COPY 写 ReferenceTool=17`
> （按径匹配 T-001），ok=19/fail=0，OP-002 toolpath **1.1219s/3257.4 = 与 reftool 探针 P3 逐位一致**
> （写回持久 + 引擎消费闭环）；I-3 issues **9→8**、param **51/51 全 PASS**（新键双侧对称采集）——
> **OP-002 分层结构差（84.8%）FAIL 消除**（区域配对 PASS：36 区/18 层 = gt 同构）；OP-002 残余 2 条
> = time 54.1%/length 71.5%（同区域数下连接长度差 3257 vs 929）→ **新校准条目 = 非切削转移族
> （surfdiff 53 行白名单外：TransferWithinLevelsType Direct vs Clearance 等）**，独立缺口不阻塞本批；
> OP-001（time 6.3%/分层 4.3% = stepover #9 不可写）与 OP-003（γ，gt 无参考刀具）保持条目不变。
> **转移族批（2026-09-06，方案 A：OP-002 残余长度差主因落单 + 实施）**：
> ① 判别（camprobe-v2ncm-142120 C0/C1/C3 + v2ncmh-143354）：Ncm 宿主四路实证（hxx SetTransfer
> WithinLevelsType NX5.0.0/cam_base + 反射 CanWrite + **官方样例 PlanarOpsSetNCMCycleAll.vb 命中**
> = NonCuttingBuilder 子树 → op builder Commit 写范）→ C0 基线 36 区 4718.5/1.268、C1 写
> TransferWithinLevelsType=Direct + 层内高度 0.5 → **1379.9/0.934（收敛 71%）**、C3 全键（+6 EngRet
> builder 差键 + 光顺族）→ 1141.9/0.694（再收 17%，残余 19% = 次键，方案 B 排队）→ **主因 =
> 模板默认 Clearance（抬刀模式）vs gt Direct（直接平移）**；② **#19 height 负结案**：v2ncmh 四写序
> 变体（Value 先/Intent 先/ValueIntent/ExpressionString）全组合 commit → 重开 Intent/ValueIntent
> 持久但 **Value 恒回模板 3**（UI 可设 API 不可写，stepover #9 同族新实例）→ 撤采（[I] 143758
> param=55/55 无假差）；③ 实施（registry #18 入表 + NxCollect 腔采集 + ParamWhiteList +
> ExecutorAdapter 写链 + NxParamWords 词集 + schema 词典 + [U] 116/116）。④ **[I] 实录
> （143643/143712/143758，资产 v2.rebuilt-20260906-143713.prt）**：I-2 写行
> `TransferWithinLevelsType=Direct`（height 撤写），ok=19/fail=0，**OP-002 3257→1298.8（length 差
> 71.5%→28.5%）、OP-001 114682→111799**；I-3 issues 8→**9**、param **55/55**——height 假差 4 条消、
> 新浮出 **OP-001 length 5.9% = stepover #9 不可写残余显形**（此前被 Clearance 连接差抵消在 5% 内，
> Direct 修对后现形——校准条目，非本批回归）；OP-002 残余 time 44.4%/length 28.5% = 进刀族未写
> （方案 B 排队）；OP-003 γ 4 条保持。

> **方案 B 判别⑨ 关闭注记（2026-09-06，源 samples/camprobe-v2ncmgap-20260906-153101.txt +
> 探针源 CamProbeV2NcmGap.cs；前置审查 = 头文件/样例/XML：Trim 废弃 NX10.0.3 → 活键 =
> MinimumClearance 扩展形态；Withs.UseEngret = "Use engage and retract defs" hxx:91）**：
> B1/B3 哨兵精确复现（1379.9/0.934、1141.9/0.694）；**G1-G7 补漏键（Withs=UseEngret、
> 6×EngRetType 按 gt、6×MinimumClearance 扩展、6×MinRampLength=70、6×HeightFrom=PreviousLevel、
> 6×ExtendBefore/AfterArc、MinimizeNumberOfEngages=False）全部写入持久（读回断言 ✓）但
> regen 逐位零贡献（0.6942/1141.8544 = B3 值）**——Type=Direct 语义下（或模板默认已同）
> EngRet 差键引擎不消费 → B3 = 公开 Ncm 键面复刻上限。**方案 B 实施批关闭（无可写键）**；
> OP-002 残余 213mm（time 44.4%/length 28.5%）归因转移 = 层内转移高度**内部值**差驱动假设
> （gt 0.5 vs 模板 3，#19 API 不可写；2.5mm×~85 转移事件 ≈ 213mm 量级吻合，无 API 通道可证）
> → **转永久校准候选**，残余 2 条维持 FAIL 哨兵（若未来找到高度内部值写入通道 → 复开）。
> 校准池更新：OP-002 由"排队待开发"转"永久候选"；残余 9 条画像不变（2 条性质变更，无数量变更）。

> **2026-09-06 静态审查 + γ 定档补记（头文件/样例/XML 三语料审查 + 判别⑧ 双会话，证据详见
> nx-v2-geom-spec.md §7 判别⑧ 与索引 §2.1/§2.5）**：
> ① **OP-003 γ 从"候选开关缺检"闭合**：五候选（RegionSequencing Optimize/RegionPoints、
> SmallAreaAvoidance Cut+0、GeometrySet.Reversed、ExtractCutArea 区组通道 E5v2）全零——
> 定档 = rebuilt 体上下文引擎区域形成内部行为，公开面无开关、通道变体穷尽 → 4 条维持永久校准。
> ② **stepover（#9）负结案静态复核证实**：主链全为 NX6-9 老成员；2406 唯一 stepover 新成员
> StepoverConnection（NX2406.0.0）零宿主零工厂零样例 = 公开面不可达；全库 372 官方源文件唯一
> stepover 写面（CornerSetRadiusAndLimitCycleAll.vb:105）写的是模板默认值 150 = 与 U-6 P3
> "界内写回填"自洽，样例存在不构成反例 → OP-001 残余归因维持。
> ③ **方案 B（进刀族）键集静态面清单化（2026-09-06 头文件/样例审查修正版）**：腔铣进刀族 =
> NcmPlanarBuilder 子树 6 EngRet builder（NcmPlanarEngRetBuilder 型，CanWrite=True）+ 光顺族；
> 官方样例写范仅两处 = PlanarOpsSetNCMCycleAll.vb:100-114（MinClearanceBuilder.Value/Intent → op
> builder Commit）与 MillingOpsSetAngleAnglePlaneCycleAll.vb:185-192（EngRetType+角度）——其余
> 补漏键（TransferWithinLevelsWith/MinRampLength/HeightFrom/ExtendBeforeArc/…）全库零样例，仅
> 静态面。**hxx 注释级关键事实**：`Trim` 自 **NX10.0.3 起废弃**（CAM_NcmPlanarEngRetBuilder.hxx:331-344
> "Use MinimumClearance instead"）→ 早前"C3 漏写 Trim"改判 = Trim 是遗留别名、活通道 =
> `MinimumClearance` 扩展形态（ExtendAndTrim/ExtendOnly…）；`TransferWithinLevelsWith`=
> **UseEngret = "Use engage and retract defs"**（hxx:91）= 层内转移启用进/退刀动作定义的总开关，
> C1/C3 均未写过 → 第一号判别候选。C3 之上候选集 = Withs/5×EngRetType(按 gt 读值)/MinimumClearance
> 扩展/MinRampLength/HeightFrom/ExtendBeforeArc/ExtendAfterArc/MinimizeNumberOfEngages(bool)。
> **【判别⑨ 已关闭本候选集：上述键全写持久但引擎逐位零消费（2026-09-06 camprobe-v2ncmgap），
> 方案 B 实施批取消，见上段判别⑨ 关闭注记。】**

## 4. 算法（步骤 → 性质映射）

A1 输入合法性与索引：双侧 op 名 → map（重复名 → DUP 失配 + diag，INV-C1）；setup 名 map；刀具序
    → PRE-C1/INV-C1
A2 配对：B 逐 op 查 A 名；A-only/B-only → 结构条目（POST-C6）；名集同序异 → ORDER_SHIFT（POST-C7）
A3 逐配对对比：模板对归一比对（POST-C2）；Params 双判据逐键（POST-C1）；单侧缺失方向性（2026-09-06
    口径修订，PTP 收尾）：A-only（重建漏写导出键）→ FAIL 不静默；B-only（近似模板带出的 gt 无概念面
    参数）→ note 不 FAIL
A4 刀具序对：类型键 + 数值双判据 + 名 diag（POST-C3）
A5 setup 名对：origin 欧氏/轴元素差/fixture（POST-C4）
A6 结构项：顶层组序（ProgramOrder）、单侧 op、刀具/setup 数差 → 结构条目（POST-C6；不含父组，见 §2 口径注记）
A7 汇总：条目派生评分（POST-C5）+ diag 聚合（INV-C4）+ 渲染 txt（INV-C3）
终止性：两侧表遍历有界（沿 MONO-2 口径）；Compare 无状态（MONO-C1）
某性质无算法步保证：无（全表映射齐）

## 5. 范围决策与冲突

- **D-1 对比维度 = A**（已确认 2026-09-04）：结构/刀具数值/MCS/白名单参数四维。几何面级/刀路维度
  v1 不可达（重建空件 + 合同无面锚点，executor D-1 同源）→ 显式缺席，不假装覆盖（§0/§6 差异注记）；
  策略/技术全参数面（cut_pattern 等）依赖导出白名单扩展（v1.5 排队，参数字典键即现有回读面；
  键集读/写实态已由 docs/nx-param-registry-spec.md 落档（2026-09-04）——读面全可、写面仅 4 键持久，
  即 v1.5-③ 实现依据）。
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

## 7. 评分规格（2026-09-06 决策④固化）

决策④（2026-09-04）预留"首批样例校准后固化为评分规格文档"。经 7 轮 [I] 终跑校准（144237 → 160817 →
200339 → 192456 → 203514 → 215209 → 000948：issues 逐轮全部可解释/零未解释，000948 校准池触底 = 纯
结构性定案）后固化如下：

| 维度 | 容差 | 判据 | 校准证据 |
|---|---|---|---|
| 参数数值 | `EpsLen=0.01mm` + `RelTol=5%` | `\|a-b\| ≤ EpsLen` **或** 相对偏差 ≤ RelTol → PASS | 000948 param=50/50 零噪音；哨兵 = OP-001 time 6.8% / length 5.9% FAIL（末轮 143758——length 5.9% 系转移族批 Direct 修对后自 3.4% PASS 显形，见 §3 记录）——阈值不掩盖已知不可写残余（stepover 60/65 vs 70，#9），调整须保哨兵 FAIL |
| 参数枚举 | ordinal 相等 | 词集同源（采集侧按键固定，两侧同形） | v1.5-③ 起枚举键全 PASS |
| MCS/fixture | origin 欧氏 ≤ EpsLen；z/x 轴元素差 ≤ `EpsAxis=1e-6`；fixture 整数等 | — | 192456 起 mcs=1/1、fixture=1/1 |
| 刀路三维（v2 gate，仅腔铣族） | time/length = 同 RelTol=5%；**区域 = 明细分层配对判据（v2.5，RegionPairing）**：层数差分级（差层面积占比 ≤ NoteAreaRatio=2% → note；> 2% → FailStructure）+ 层内 1:1/2:1 合并 + 配对面积漂移 ≤ RelTol（哨兵）；签名面集 = 集合等 | CompareV2 维 | OP-004 深度复刻后 2 区=2 区全同；OP-001 80 vs 79 层 = **1 真差层（面积 4.3% > 2%，FailStructure 哨兵案例——端层范围取整差，非纯粒度）**；OP-002 18 vs 118 层（84.8%）→ 机理级报告；阈值 004123 定案（0.1% 微区 → note / 4.3% 整层 → FAIL），调值按 §7 变更纪律留痕 |
| 单侧缺失（2026-09-06 方向化） | A-only → FAIL；B-only（近似模板带出，gt 无概念面）→ note | 不静默（note 含键与值） | PTP 收尾 000948：bottom_stock ×2 降 note 后 param=50/50 |

**变更纪律**：本表值 = 2026-09-06 定稿。任何容差调整须过全量 [U] + 校准回归——已知 FAIL 哨兵（OP-001
time 6.8% / length 5.9%（143758 末轮）、γ 类条目）必须仍 FAIL 不静默，已知 PASS 不得转 FAIL；调整在此节留痕（日期 + 理由 + 回归结果）。
