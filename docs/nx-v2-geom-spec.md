# v2 几何重建规格（spec-before-code 纪要，2026-09-05；D-1..D-5 定稿 = A 一体交付）

> 状态：**纪要落档 + 实现收官（2026-09-05）**——范围决策 = D-1 STEP 导入 / D-2 签名入 plan
> （additive）/ D-3 腔铣族先行 / D-4 Executor 改动面按 §0 / D-5 Comparer 三维 + **一体交付**（A）。
> [U] 100/100 全绿（93 回归 + V2 七条红线：V2-PRE-1/2/3 + POST-2/4/5/6 + INV-2 round-trip）；
> csc 三适配器编译通过（compile-executor-adapter 补入 NxCollect.cs 合编——PS5.1 无 BOM UTF-8
> 中文注释致插入行不生效的编码坑，注释已 ASCII 化）；sln MSBuild 构建通过。改动面：
> schema operation.cut_area_signatures（可选）+ Model{OperationItem/FaceSignature/刀路区域字段} +
> Doc{FaceSignatureJson} + ExporterCore 映射 + NxCollect{CollectV2/BodyFaceSignatures} +
> ExecutorCore{AppendSignatures/MatchSignatures} + RebuildPlan{OpCommand.Signatures/FaceMatchResult}
> + ExecutorAdapter{v2 导入前置/指派/刀路/原地 Save} + ComparerCore{CompareV2 三维} +
> ComparerAdapter 渲染 + V2GeomTests。**残余 = [I] GUI Execute 三连跑实录（§3 I-1..I-4），
> 清单见 §7**。
> 需求源：docs/nx-plugin-design.md §1 步骤②（v2 目标路径"打开原始 STEP 文件"）+ §7 尾注
> （2026-09-05 第二波：STEP 批处理实证闭环、v2 前置齐备）+ §2.2 维度表（几何/刀路 v1.5 缺席注记）。
> 预检实证（2026-09-05 入库 6f4f5fb，索引 §2.1 v2 增补段）：
> G1 几何指派机制（默认集 SetArray 通道）/ G2 带几何刀路（op 级面选区必需，543s）/
> G3 区域读回（CutRegionsData）/ F1 签名对齐（gt 13/13 唯一命中）。
> 合同：schema/autocam-plan.schema.json v3.0（本批 = operations[] 可选 cut_area_signatures，
> additive，contract_version 不变）。事实源：docs/nx2406-install-index.md §2.1（含 v2 增补）。

## 0. 一段话结论

v2 = 把 v1 的空件重建升级为**带几何重建 + 带刀路**：Executor 前置 STEP 导入（test.step → 1 body/
26 面）→ 组/op 骨架照 v1 → 按 plan 携带的 **op 级 cut-area 面签名**在回导 body 上匹配选中相同
加工面（F1：AskFaceData 类型|法向轴|代表点 0.01mm|半径 唯一匹配）→ 指派 op 级 CutAreaGeometry
默认集（G1 机制）→ 生成刀路（G2）→ 原地 Save 持久；Comparer 新增三维：刀路 time/length、
区域级（CutRegionsData 区数/面积和/质心）、**签名面集匹配率**，使 543s-vs-58s 类差异可归因
（面差 → 刀路差 → 区域差 全链可解释）。**一体交付**：重建链与对比三维同一批完成（D-5=A）。
PTP/孔族不在本批（D-3）；签名通道是 U-5 质心/面积禁令的替代面身份（不翻案）。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 输入 | plan.json（含可选 cut_area_signatures）+ STEP 资产（samples/test.step，自产） |
| 调用序列（[I]） | ExecutorAdapter-v2（csc exe，NX Execute）：开新件 → **导入 test.step**（DexManager 配方，索引 §2.1）→ CreateCamSession → CreateCamSetup → 四父组/刀具/MCS 照 v1 → op 级 CutAreaGeometry 签名面指派 → GenerateToolPath → 读 time/length → 原地 Save → 回读对照报告。ComparerAdapter-v2：两件轮换采集 → CompareCore 新三维比对。ExporterAdapter：重导 plan 带签名 |
| 签名字段 | `cut_area_signatures[]`：`{face_type:int, normal_axis:"X+\|X-\|…", rep_x/rep_y/rep_z:double(0.01mm 取整), radius:double(0.001 取整)}`——导出侧从 gt op CutAreaGeometry 面集 AskFaceData 采集；重建侧同 body 面上匹配；匹配容差 = 取整粒度（±0.005/±0.0005） |
| 失败语义 | 签名无匹配（重建件面上找不到 plan 面）→ 该 op error diag（GEOM_SIG_MISMATCH）不入刀路；单面不匹配 → warning diag 继续（部分指派）；其余沿 v1（结构级中止/单项 diag） |
| 只读纪律 | gt 件全程只读（导出/对比侧沿 MONO-1）；重建件为自建 |
| 版本兼容 | schema additive（可选字段）；旧 plan（无签名）→ 重建侧跳过面指派 = v1 空刀路行为 + diag（V2-PRE-3 显式声明）；contract_version 维持 3.0 |

## 2. 数据结构要点

- schema `operations[]` 增可选 `cut_area_signatures[]`（元素 = {face_type int, normal_axis enum?——
  自由串四值 X+/X-/Y+/Y-/Z+/Z-，导出恒产、重建恒匹配、不押词表外；rep_x/y/z number, radius number}；
  $comment 注 F1 实证出处与 U-5 替代语义）。
- Model.cs：`OperationItem` 增 `CutAreaSignatures`（List<FaceSignature>，纯逻辑值对象
  FaceSignature{int FaceType; string NormalAxis; double Rx, Ry, Rz; double Radius}——导出/重建/比对
  共用，无 NX 依赖）。
- Doc.cs OperationJson 镜像增可选字段；序列化 KV/数组形状沿现 DataContract 惯例。
- ExportSnapshot.Operations[].新增：CutAreaSignatures + ToolpathTime/ToolpathLength（double，可缺
  ——重建件无刀路时为缺省）+ CutRegions{Count, AreaSum, CentroidX/Y/Z?}（可缺）——NxCollect 采集
  面，纯逻辑只透传。
- Executor RebuildPlan：op 指令增 Signatures（匹配输入）；ParamInstruction 机制不变。
- Comparer 结果模型增三维条目类型（TOOLPATH_DIFF / REGION_DIFF / SIG_MATCH），沿 INV-C3 可溯 key。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证 [T]=待实测）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| V2-PRE-1 | plan 解析：cut_area_signatures 可选且形状合法（元素字段齐、rep 有限数）；缺省 = 空列表 | §1 协议 | 无签名字段夹具解析成功且为空；坏形状 → 明确 error | [U] |
| V2-PRE-2 | 签名值域：normal_axis ∈ {X+,X-,Y+,Y-,Z+,Z-}；face_type ∈ 非负；radius ≥ 0 | 采集规范 | 越界夹具 → 该 op error diag（不静默） | [U] |
| V2-PRE-3 | 无签名 plan（v1 旧形状）→ 重建侧不指派面、不生成刀路，行为 = v1 + diag（GEOM_SIG_ABSENT） | 兼容声明 | 空签名夹具 → diag 且指令无指派步 | [U] |
| V2-POST-1 | 导出侧：gt 腔 op 的 CutAreaGeometry 面 → NxCollect 签名与实测 13 面一致（033810 A 档逐条） | F1 | 快照含 13 签名 == 固定 13 值夹具 | [U] |
| V2-POST-2 | Executor 匹配器：plan 签名集在给定 body 面集上唯一命中（1:1 无歧义）→ 指派指令完整 | F1（13/13 零歧义） | 全命中夹具 → 指派 13；缺面夹具 → 部分 + diag | [U] |
| V2-POST-3 | 指派写入 = 默认集 SetArray + Commit（非新建集）后新 builder 回读 items 数一致 | G1 | 夹具（替身）断言通道选择 | [U]（替身）+[I] |
| V2-POST-4 | Comparer 刀路维：双侧 time/length 双判据（EpsLen/RelTol 沿 v1）→ PASS/FAIL 条目含双侧值 | 设计 §2.2 | 变异夹具 → 恰 1 FAIL | [U] |
| V2-POST-5 | Comparer 区域维：双侧区数/面积和 双判据；单侧缺（未生成刀路）→ FAIL 不静默 | G3 | 区数变异 → FAIL；rebuilt 无 → FAIL 条目 | [U] |
| V2-POST-6 | Comparer 签名面集匹配率：双侧签名集交集/差集计数条目（gt-only/reb-only/匹配数） | F1 | 一致 → 13/13；删 1 → 12 匹配 + 1 gt-only | [U] |
| V2-INV-1 | 签名通道三侧单一来源（导出采集/NX 匹配/比对用同一 FaceSignature 语义，无双轨） | 工程纪律（D-3 沿革） | 编译面 + 键值断言 | [U] |
| V2-INV-2 | 数值沿采集取整语义透传（0.01mm/0.001 取整 = 匹配与导出同粒度），无二次取整 | F1 | round-trip 断言 | [U] |
| V2-MONO-1 | 采集/比对无状态幂等（沿 MONO-C1） | 定义 | 双跑相等 | [U] |

[I] 集成验证清单（不进单测；GUI Execute 三连跑交付用户实录）：
- I-1 ExporterAdapter 重导 test.prt → plan 含腔 op cut_area_signatures（13 条）+ schema 落盘复验 PASS；
- I-2 ExecutorAdapter-v2 重建：test.step + plan → prj′ 含几何 + op 面指派 13/13 + 刀路 time>0 +
  Save 持久（重开复核 body 26 面/刀路存档）→ 回读对照 PASS；
- I-3 ComparerAdapter-v2 终跑：gt vs prj′ → 签名面集 13/13、刀路/区域维差异可解释（预期首次即
  收敛——同面同参数 → time 接近；若仍差 → 校准清单新增条目并落档）；
- I-4 校准清单更新（沿 comparer spec §3 记录口径）。

## 4. 算法（步骤 → 性质映射）

A1 解析：optional 字段 → V2-PRE-1/PRE-2
A2 导出采集：NxCollect 腔 op CutAreaGeometry 面 → AskFaceData 签名（取整粒度）→ 快照 → V2-POST-1/INV-2
A3 ExecutorCore：签名列表 → 匹配器（body 面集签名索引 → 1:1）→ 指派指令 → V2-POST-2/PRE-3
A4 ExecutorAdapter：导入（P0 配方）→ 默认集 SetArray(匹配面) + Commit → GenerateToolPath → Save →
  回读 → V2-POST-3（[I] I-2）
A5 NxCollect 增补采集：刀路 time/length + CutRegionsData → V2-POST-4/5 输入面
A6 ComparerCore：三维判据（刀路双判据/区域双判据/签名集 diff）→ V2-POST-4/5/6/INV-C3 沿革
A7 结果渲染 + 校准清单 → [I] I-3/I-4
终止性：有限面集/签名集；匹配 = 字典 O(n)；无循环新增。

## 5. 决策与冲突

- D-1 = STEP 导入（§0）；D-2 = 签名入 plan additive（§1）——**F1 实证是本决策的充分条件**
  （签名 13/13 唯一命中零歧义，0.01mm 粒度 round-trip 稳定）；备选 B（比对时现取、plan 不带）
  因 Executor 单会话无 gt 参照而残缺，否决。
- D-3 = 腔铣族先行（PTP 面几何无生产源，U-1；近似维持 v1 口径 + diag）。
- D-4 = Executor 改动面按 §0 四条（导入/指派/刀路/持久），组树与参数白名单链不动
  （v1 PRE-4 维持）。
- D-5 = Comparer 三维 + **一体交付**（已确认 A）：面匹配率是刀路/区域差异的归因前提，
  拆批会留下"重建有几何但对比无法解释"的中间态。
- 冲突（已决）：v1 校准清单预期 PTP 4 键/tool#4 残余与本批三维无交互（PTP 面几何本批不做）→
  校准清单沿独立分支记录。
- 签名值域 normal_axis 自由串 vs 枚举：与 operation_type 自由串两档同原则（不押词表外
  未来形态），validator 不断言枚举（V2-PRE-2 校验为运行时 diag 而非 schema enum）。

## 6. 不在本批范围

PTP/孔族面几何与签名；blank 几何指派（gt 无 blank，刀路 543s 实证无需）；面级区域级
（CutRegionsData 区域——本批只到区数/面积和/质心计数级，区域几何不配对）；多 setup/多体
STEP 资产；评分规格固化（决策④遗留，随本批校准记录后另行）。

## 7. [I] GUI Execute 三连跑实录清单（2026-09-05 交付用户；沿用历次 adapter-run 模式）

编译产物：`.claude/tmp/ExporterAdapter.exe`（重导）、`.claude/tmp/ExecutorAdapter-v2.exe`
（v2 重建）、`.claude/tmp/ComparerAdapter.exe`（对比）——NX File → Execute 依次实录，
日志文件名自动 `samples/{adapter,executor,comparer}-run-<ts>.txt`：

1. **I-1 导出重导（ExporterAdapter.exe，args = 输出 test.plan.json 路径）**：test.prt → plan
   含腔 op `cut_area_signatures`（13 条，与 camprobe-v2face-A-033810 档签名一致）+ schema
   落盘复验 PASS（validator 词集无违例）。验收 grep：`"cut_area_signatures"` 出现且含 13 元素。
2. **I-2 v2 重建（ExecutorAdapter-v2b.exe，args = plan 路径 [可选 prj 目标]）**：plan.input_ref
   = test.prt → 自动推导 samples\test.step → 导入（验 1 body/26 面）→ 组/op 照 v1 → 签名
   匹配指派 → 刀路 time>0 → 原地 Save（v2.rebuilt-<ts>.prt）。验收：日志含
   "匹配=13 未命中=0"、toolpath time>0、Save(原地持久) ok；重开复核 body 26 面 + 刀路存档。
   > 实录 191001（首跑）：op 面指派全中但刀路 0——缺组级 part 指派 → eecc71c 补
   > （gt 结构 = 组级 set0 Body + op 级面）。实录 191434（修复后）：OP-001 129.8s / OP-002
   > 27.2s / OP-004 220.1s 全出；**OP-003（COPY_COPY，3 面）仍 0**——面签名 3/3、组级/参数
   > 与 gt 全同仍空刀路（gt 8.03s/3 区）→ 待诊校准条目：候选判别 = 几何集属性
   > （GeometrySet.MaterialSide/Stock 等，不在复刻面）或 op 级 CutLevel/区域设置——需一次
   > gt vs rebuilt 同 op builder 只读对照探针（探针待批）。
3. **I-3 对比终跑（ComparerAdapter-v2.exe，无参 → B 防呆自动最新 v2.rebuilt-*.prt）**：
   > 实录 192158（正确 B = v2.rebuilt-191437）：**v2 汇总 sigfaceset=4/4（面复刻维全 PASS，
   > 零 SIG_FACE_DIFF）**；issues 43→25 全可归因（腔刀路/区域差 ×16 = feed_cut 白名单缺口 +
   > 区域分割粒度 + OP-003 待诊；PTP 键错位 ×4 + tool#4 = 200339 清单同源；PTP 刀路单侧缺 ×4 =
   > v2 范围缺席噪音 → 维 gate 修正：v2 三维仅腔铣族比对（ComparerCore CompareV2）。
   > 实录 192456（gate 后终跑）：**issues=21（与预测一致）**，v2 汇总 toolpath=0/8 region=0/8
   > sigfaceset=4/4——残余 21 全为已知校准条目，无新增未解释项 → I-3 验收关闭。
   > 校准清单新增：① 区域维同面复刻仍敏感（gt 80 vs rebuilt 24）——v2.5 区域几何配对而非计数；
   > ② feed_cut（注册表 #15 未测写）成为写面探针候选——gt feedCut 2000/500 与 rebuilt 默认 250
   > 是腔刀路时间差主因。
4. **I-4 校准清单更新**：终跑 diff 全条目与校准记录对照后回填 comparer spec §3 记录。

> 实现侧执行记录（2026-09-05）：spec 落档 → schema/Model/Doc/ExporterCore/NxCollect 扩展 →
> ExecutorCore 解析+匹配器 → ExecutorAdapter v2 链 → ComparerCore 三维 + 渲染 → V2GeomTests
> 七条红线入测试（100/100）→ 三适配器 csc 编译通过 → sln 构建通过。本清单待 NX GUI 实录。
