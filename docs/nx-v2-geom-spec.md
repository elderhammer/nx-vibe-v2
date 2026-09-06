# v2 几何重建规格（spec-before-code 纪要，2026-09-05；D-1..D-5 定稿 = A 一体交付）

> 状态：**纪要落档 + 实现收官（2026-09-05）**——范围决策 = D-1 STEP 导入 / D-2 签名入 plan
> （additive）/ D-3 腔铣族先行 / D-4 Executor 改动面按 §0 / D-5 Comparer 三维 + **一体交付**（A）。
> [U] 100/100 全绿（2026-09-05 时点：93 回归 + V2 红线七测试 = 八性质名——V2-PRE-1/2/3（PRE-3 折叠
> 于 PRE-1 测试内）+ POST-2/4/5/6 + INV-2 round-trip；V2GATE 门控测试 2530c6d 补入 → V2GeomTests
> 现行 8 条；全仓 2026-09-06 现行 118/118 见 src/NXPlugins/README.md）；
> csc 三适配器编译通过（compile-executor-adapter 补入 NxCollect.cs 合编——PS5.1 无 BOM UTF-8
> 中文注释致插入行不生效的编码坑，注释已 ASCII 化）；sln MSBuild 构建通过。改动面：
> schema operation.cut_area_signatures（可选）+ Model{OperationItem/FaceSignature/刀路区域字段} +
> Doc{FaceSignatureJson} + ExporterCore 映射 + NxCollect{CollectV2/BodyFaceSignatures} +
> ExecutorCore{AppendSignatures/MatchSignatures} + RebuildPlan{OpCommand.Signatures/FaceMatchResult}
> + ExecutorAdapter{v2 导入前置/指派/刀路/原地 Save} + ComparerCore{CompareV2 三维} +
> ComparerAdapter 渲染 + V2GeomTests。**[I] GUI Execute 实录收官（2026-09-05 晚，§3 I-1..I-4
> + §7）：I-1 190859 重导带签名 13/6/3/13；I-2 首跑 191001 两缺陷（组级 part 指派缺失 + comparer
> B 件选错）→ eecc71c/da3fd80 修复 → 191434 ok=19/fail=0（OP-003 刀路 0 待诊，判别读探针
> camprobe-v2op-191955/192013 排除集属性/DPC/feed 假设）；I-3 192158（issues=25，sigfaceset=4/4）
> → ComparerCore CompareV2 维 gate 后 192456（issues=21=预测，toolpath=0/8 region=0/8
> sigfaceset=4/4）验收关闭；I-4 校准清单回填 comparer spec §3（2026-09-05）**。
> 需求源：docs/nx-plugin-design.md §1 步骤②（v2 目标路径"打开原始 STEP 文件"）+ §7 尾注
> （2026-09-05 第二波：STEP 批处理实证闭环、v2 前置齐备）+ §2.2 维度表（几何/刀路 v1.5 缺席注记）。
> 预检实证（2026-09-05 入库 6f4f5fb，索引 §2.1 v2 增补段）：
> G1 几何指派机制（默认集 SetArray 通道）/ G2 带几何刀路（op 级面选区必需，543s）/
> G3 区域读回（CutRegionsData）/ F1 签名对齐（gt 13/13 唯一命中）。
> 合同：schema/autocam-plan.schema.json v3.0（本批 = operations[] 可选 cut_area_signatures，
> additive，contract_version 不变）。事实源：docs/nx2406-install-index.md §2.1（含 v2 增补）。
> 2026-09-06 修正（小漂移收口）：§1/§2 签名字段名 rep_* 系早期稿措辞——实态 = schema/Model/Doc.cs/产物
> 一致为 rx/ry/rz（同 0.01mm 取整语义），normal_axis 为自由串六值，正文两处已改。

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
| 签名字段 | `cut_area_signatures[]`：`{face_type:int, normal_axis:"X+/X-/Y+/Y-/Z+/Z-", rx/ry/rz:double(0.01mm 取整), radius:double(0.001 取整)}`——导出侧从 gt op CutAreaGeometry 面集 AskFaceData 采集；重建侧同 body 面上匹配；匹配容差 = 取整粒度（±0.005/±0.0005） |
| 失败语义 | 签名无匹配（重建件面上找不到 plan 面）→ 该 op error diag（GEOM_SIG_MISMATCH）不入刀路；单面不匹配 → warning diag 继续（部分指派）；其余沿 v1（结构级中止/单项 diag） |
| 只读纪律 | gt 件全程只读（导出/对比侧沿 MONO-1）；重建件为自建 |
| 版本兼容 | schema additive（可选字段）；旧 plan（无签名）→ 重建侧跳过面指派 = v1 空刀路行为 + diag（V2-PRE-3 显式声明）；contract_version 维持 3.0 |

## 2. 数据结构要点

- schema `operations[]` 增可选 `cut_area_signatures[]`（元素 = {face_type int, normal_axis enum?——
  自由串六值 X+/X-/Y+/Y-/Z+/Z-，导出恒产、重建恒匹配、不押词表外；rx/ry/rz number, radius number}；
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
   > 实录：adapter-run-20260905-190859.txt（ExporterAdapter v11，schema 校验+落盘复验 PASS）。
2. **I-2 v2 重建（ExecutorAdapter-v2b.exe，args = plan 路径 [可选 prj 目标]）**：plan.input_ref
   = test.prt → 自动推导 samples\test.step → 导入（验 1 body/26 面）→ 组/op 照 v1 → 签名
   匹配指派 → 刀路 time>0 → 原地 Save（v2.rebuilt-<ts>.prt）。验收：日志含
   "匹配=13 未命中=0"、toolpath time>0、Save(原地持久) ok；重开复核 body 26 面 + 刀路存档。
   > 实录 191001（首跑）：op 面指派全中但刀路 0——缺组级 part 指派 → eecc71c 补
   > （gt 结构 = 组级 set0 Body + op 级面）。实录 191434（修复后）：OP-001 129.8s / OP-002
   > 27.2s / OP-004 220.1s 全出；**OP-003（COPY_COPY，3 面）仍 0**——面签名 3/3、组级/参数
   > 与 gt 全同仍空刀路（gt 8.03s/3 区）→ 待诊校准条目。
   > **判别链七探针收官（2026-09-05 晚，camprobe-v2{regen,bp,surf,fix,flip,sel,body,apiclone} 系列，
   > 源 Journal/CamProbeV2*.cs）→ γ 级定案：OP-003 空刀路 = rebuilt（STEP 回导）体上下文上的
   > NX 引擎区域形成差异，非 executor 复刻缺口**。证据链：
   > ① 新会话单独/按序重生成均确定性 0（camprobe-v2regen，排除会话态/顺序）；
   > ② `Operation.BuilderProperties` 双档实验**失效并修正认知**：同件内 OP-001 与 OP-003 的 JSON
   > 逐字节相同（同 md5）→ BP JSON 非逐 op 生效参数快照（非"已提交态"逐 op 视图），索引 §2.1
   > 表述已随本批修正（camprobe-v2bp）；
   > ③ 深面反射（camprobe-v2surf，builder 树 3 层白名单递归）：OP-003 件间差异 ⊖ OP-001 对照后
   > 仅剩容差 0.02 vs 0.03（兄弟同差）→ 无 OP-003 专属参数差异；gt 件内 OP-003 的显著候选 =
   > `CutLevel.GlobalDepthPerCut`（20 vs 兄弟 0.2/0.3）——plan 现读写的 op 级 `b.DepthPerCut`
   > 恒 0 继承，腔真实 stepdown 在 CutLevel 子树（导出读键缺口候选，见 ④）；
   > ④ 写回判别（camprobe-v2fix C1..C6：DPC 20/DepthPerCut 20/Stepover 65/Ext 2/组合/Blank 0.2）
   > 均不解除零刀路；翻转判别（camprobe-v2flip）：**CutLevel 子树写持久 ✓（可写族）**，但 gt 侧
   > DPC 20→1 后刀路不变（8.03s/3 区）→ DPC 非零化参；
   > ⑤ 面集判别（camprobe-v2sel）：13/10 面选择 → 24 区 220.11s（= OP-004 同参值），自身 3 面
   > → 0 可复现；同会话还原 3 面后刀路不失效（工具路径状态缓存现象，佐证面变不触发重算）；
   > ⑥ 体保真（camprobe-v2body）：gt 与 rebuilt body 全同（area/vol/COF/26 面/68 边；OP-003
   > 3 面均为 4 边平面 Z+ z=100 两侧一致）；
   > ⑦ API 新建判别（camprobe-v2apiclone）：executor 同款 API + 默认参数 + 同 3 面 → **gt 件
   > 14.78s/3 区非零 vs rebuilt 件 0**——同码同面同参仅件不同 → 零化与 op 创建路径/参数无关，
   > rebuilt 体上下文引擎行为（候选机制：STEP 回导体的 cut-region 内部面/loop 引用或 import
   > 特征态，公开面无可调开关；体量/拓扑/签名/边数全等但区域形成不同）。
   > **处置**：executor 不改（已按 plan 正确复刻面与参数）；OP-003 4 条 FAIL 转**永久校准条目**
   > （192456 已知残余之一）；v2.5 区域几何配对须先解释本差异（判别 ⑦ 为复现基线）。
   > 副产品（④）：腔 real stepdown = `CutLevel.GlobalDepthPerCut` 而非 op 级 `b.DepthPerCut`
   > → 导出深度键读成员修正候选（gt 0.3/0.2/20/20 vs rebuilt 模板默认 1 是区域/刀路结构差的
   > 主因候选，v2.5 写面扩展批先验证再入白名单）。
3. **I-3 对比终跑（ComparerAdapter-v2.exe，无参 → B 防呆自动最新 v2.rebuilt-*.prt）**：
   > 实录 192158（正确 B = v2.rebuilt-191437）：**v2 汇总 sigfaceset=4/4（面复刻维全 PASS，
   > 零 SIG_FACE_DIFF）**；issues 43→25 全可归因（腔刀路/区域差 ×16 = feed_cut 白名单缺口 +
   > 区域分割粒度 + OP-003 待诊；PTP 键错位 ×4 + tool#4 = 200339 清单同源；PTP 刀路单侧缺 ×4 =
   > v2 范围缺席噪音 → 维 gate 修正：v2 三维仅腔铣族比对（ComparerCore CompareV2）。
   > 实录 192456（gate 后终跑）：**issues=21（与预测一致）**，v2 汇总 toolpath=0/8 region=0/8
   > sigfaceset=4/4——残余 21 全为已知校准条目，无新增未解释项 → I-3 验收关闭。
   > 校准清单新增：① 区域维同面复刻仍敏感（gt 80 vs rebuilt 24）——v2.5 区域几何配对而非计数；
   > ② feed_cut（注册表 #15 未测写）为腔刀路时间差主因候选——gt feedCut 2000/500 vs rebuilt
   > 默认 250。**2026-09-05 晚探针三跑持久 ✓（camprobe-feedcut-{200847,200905,200924}）→
   > v1.5-⑤：写面白名单入表 + 采集（腔/孔/PTP 三族）/写适配器贯通，[U] 102/102 全绿；
   > [I] 三连跑（重导/重建/对比）——随后 203400/203514 实录验收关闭（见下块：feed 复刻生效、
   > OP-003 4 FAIL 与区域结构差按预期不受影响）。**
4. **I-4 校准清单更新**：终跑 diff 全条目与校准记录对照后回填 comparer spec §3 记录。
   > ——已回填（2026-09-05 收尾，见 nx-plan-comparer-spec.md §3 增补记录；与 comparer spec
   > 双份记录，本 §7 实录 + comparer spec 校准清单为准）。

> **v1.5-⑤/tool#4 [I] 实录收尾（2026-09-05 晚 20:34-20:35，executor-run-203400 / comparer-run-203514，
> 资产 v2.rebuilt-20260905-203402.prt）——v1.5-⑤ 验收关闭**：
> I-2（203400，ok=19/fail=0）：T-004 走 CHAMFER_MILL 注册对 + ChamferLength=D/2 预置修复
> （4bc32fa）→ 直径 6 写入持久、回读 PASS、无 INFERRED；FeedCut 全写入（腔 2000/1200/500/500 +
> PTP 80/35）→ 重开回读对照 PASS（feed 持久终判 = [I] 级）；OP-001/002/004 刀路 16.42/5.79/110.16s
> （长度与 192456 批完全一致 → 同长度下 time 比例 ≈ feed 反比 8×（129.8/16.4），feed 复刻生效的
> 物理级验证）；OP-003 仍 0（γ 永久校准项）。
> I-3（203514，issues=**20**，192456 的 21 -1）：tool#4 条目消除（T-004 两侧均 Mill|MillChamfer、
> 直径 6=6 PASS，tool=6/6）；sigfaceset=4/4；param=48/52（feed 键双侧全 PASS、残余 4 = PTP
> hole_depth↔bottom_stock 键错位）；**腔 16 残余归因升级**：时间差已随 feed 收敛后仍存 = 长度差
> 驱动（gt 118746 vs reb 34591 等，区域 80/36/2 vs 24×3）→ 主因 = **CutLevel.GlobalDepthPerCut
> 未复刻**（gt 0.3/0.2/20/20 vs reb 模板默认 1，OP-003 判别副产品已记）→ v2.5 深度键修正候选
> （导出读面改 CutLevel 子树 + 写面白名单验证）为区域/长度维收敛路径；PTP 4 键错位保持已知。
> **v2.5 深度键修正已实证并实施（2026-09-05 晚）**：写面验证探针 camprobe-v2depth-211011（rebuilt
> 侧三 op 写 gt 值 → commit 持久 ✓ + 刀路长度按深度反比收敛——0.3→×3.32（114682 vs gt 118746 ≈
> 96.6%，区域 79/80）、0.2→×4.85、20→×0.063（区域 2=2 全同）；op 级写对照三 op 零变化 = 惰性坐实；
> γ 写 20 仍 0 稳定）→ 导出读键（NxCollect 腔分支）改读 CutLevel 子树 + 白名单写面目标同改
> （registry #12 行更新）→ [I] 复跑实录（2026-09-05 21:48-21:52，adapter/executor/comparer-run
> 214847/215028/215209）：
> - I-1 重导：plan 四腔 op depth_per_cut = **0.3/0.2/20/20**（不再 0），schema 校验+落盘复验 PASS。
> - I-2 重建：写行 = `CutLevel.GlobalDepthPerCut.DistanceBuilder=0.3/0.2/20/20`，ok=19/fail=0；
>   刀路 time/length 与探针实证值逐位一致（OP-001 54.63s/114682、OP-002 28.37s/39850、OP-004
>   6.63s/3577）；OP-003 仍 0（γ）。
> - I-3 对比：**issues 20→15**。**战果重估（诚实版）**：OP-004 4 条全消（A 6.82s/3761.6/2 区 ≈
>   B 6.63s/3576.5/2 区）；OP-001 长度差 70.9%→3.4%（PASS），余 time 6.3%/区域 80 vs 79/面积 6.6%
>   边缘差 = stepover 65 vs 70（#9 不可写）等已知参数残余；**OP-002 4 条经 gt 侧判别（camprobe-v2depth
>   -215614，gt regen = 存档 0.515s/929/36 区新鲜、CutLevel 0.2 确认）证伪"深度缺口"归因**——同参数
>   rebuilt 产出 39850/118 区（43×）→ **体上下文 γ 类差异（OP-003 判别⑦ 同款机制第二实例，程度 =
>   超密而非零）**，非 executor 复刻缺口；203514"腔 12 条深度归因"对 OP-002 系错归因（当时 B@1.0
>   =8210 已超 gt 8.8×）。残留 15 = OP-003 γ 4 + OP-002 γ 类 4 + OP-001 已知残余 3 + PTP 键错位 4；
>   无新增未解释项 → **深度键修正批验收关闭**（OP-002 条目转永久校准，区域几何配对须覆盖该现象）。
> 无新增未解释项 → **v1.5-⑤ + tool#4 收口批验收关闭**（校准记录见 comparer spec §3 追加）。

> 实现侧执行记录（2026-09-05）：spec 落档 → schema/Model/Doc/ExporterCore/NxCollect 扩展 →
> ExecutorCore 解析+匹配器 → ExecutorAdapter v2 链 → ComparerCore 三维 + 渲染 → V2GeomTests
> 七条红线入测试（100/100）→ 三适配器 csc 编译通过 → sln 构建通过。[I] 实录已随 §7 收官
> （I-1..I-4，2026-09-05 晚：190859 / 191001 / 191434 / 192158 / 192456）。

> **判别⑧ 五候选开关闭合（2026-09-06，源 samples/camprobe-v2gammacands-20260906-{151742,151900}.txt +
> 探针源 CamProbeV2GammaCands.cs；静态审查前置 = 头文件/样例/XML 三语料，结论入索引 §2.1/§2.5）**：
> 为把 γ"候选开关缺检"升级为闭合，按静态审查挑出的公开面低投入候选逐一双会话实测
> （B0 克隆基线复现 0 哨兵 ✓）：
> - E1 `RegionSequencing=Optimize/RegionPoints`（MillCutParameters，NX6.0.0）→ 写持久、regen **0**；
> - E2 `SmallAreaAvoidance`（SmallAreaStatus=Cut + AreaSize=0/PartUnits）→ 写持久、regen **0**；
> - E3 `GeometrySet.Reversed=true`（NX2007）→ **写不持久**（写后新 builder 读回 False，双会话一致——
>   "形态可写 ≠ 持久"新实例，入索引 §2.1/§2.5）；
> - E4 `CAMSetup.ExtractCutArea(γ op)`（NX2306）→ **非空**：产物 FeatureGeometry
>   `MILL_AREA_CAVITY_MILL_COPY_COPY`（引擎判 op 有 cut area 且可复制成区组——"引擎无 cut area"
>   假设排除）；产物直生成未抛；
> - E5v2 op 几何父挂 E4 产物区组（3 面引擎复制、op 级不指派）→ **0**（"op 级默认集 SetArray
>   指派上下文"假设排除；"AREA" 组字面量 CreateGeometry 不存在 = The desired template does not
>   exist，入 §2.5）。
> **定档闭合**：区域形成为 0 与参数/执行层旋钮（排序×2/小面积滤除/法向反转）/几何引用通道
> （op 直接集 vs 引擎 ExtractCutArea 区组）均无关 → γ = rebuilt（STEP 回导）体上下文上的引擎
> 区域形成内部行为，**公开面无开关、通道变体已穷尽**，维持永久校准条目（comparer OP-003 ×4
> 不变）。候选清单 = 静态审查输出（区域词族命名级扫描零命中 + GeometrySet seed/traverse 族
> 无腔铣消费文档 + 全语料无区域形成条件注记 + 官方样例无"导入体+腔铣刀路"先例——CAMSetupImport
> 的 sim_final2.stp 仅挂 KIM 装夹）。
