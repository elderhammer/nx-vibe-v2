# samples — 测试资产（Plan 双向验证闭环）

> 状态盘点（2026-09-03，按 docs/nx2406-install-index.md §1 检索）：
> NX2406 安装目录**没有**现成的手编 ground truth（含 CAMSetup 工序的 .prt）与 STEP 样例；
> 只有两类可用素材（见下）。决策③：**首件由用户提供（samples/test.prt，2026-09-03 已在
> NX2406 会话确认可作基准，见下方盘点）**，其余自建件由 NX2406 会话手编后入库。

## NX 安装目录可参考素材（**不提交 vendor 文件**，仅引用绝对路径）

| 素材 | 位置 | 用途 |
|---|---|---|
| CAM 模板部件 | `C:\Program Files\Siemens\NX2406\mach\resource\template_part\metric\`（`mill_contour.prt` / `mill_planar.prt` / `drill.prt` / `MillTurn_Exp.prt` / `cam_metric_template.prt`…） | `Part.CreateCamSetup(templateName)` 初始化实验、重建侧"打开+建组"链路冒烟 |
| 几何样件（无 CAM） | `C:\Program Files\Siemens\NX2406\UGOPEN\glass.prt` / `facetted_hood.prt` 等 | 纯几何读取/面属性遍历实验（FaceResolver 前置） |
| 官方 C# 参考实现 | `C:\Program Files\Siemens\NX2406\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport\` | PlanExecutor 建组/导库范式（代码级参考，不拷贝文件） |

## 需要自建的资产（本目录将来入库）

| 文件 | 内容 | 用途 |
|---|---|---|
| `test.prt`（**已入库**） | ground truth 首件（用户提供，2026-09-03；已在 NX2406 会话打开确认：含实体与完整 CAMSetup，6 道工序，见下方盘点） | 步骤①导出、③对比的基准；步骤 0 dump journal 的验证对象 |
| `test.step`（计划） | 同件 STEP 导出 | 步骤②"打开原始 STEP"重建路径（v2 范围） |
| `test.rebuilt-014933.prt`（**已入库**，2026-09-04） | 按旧形状 plan 重建的 prj′（schema v3 时代，136K） | 历史重建基准（对比件参考） |
| `test.rebuilt-132130.prt`（**已入库**，2026-09-04） | D-4 后按新形状 plan 重建的 prj′（PlanExecutor [I] 产物） | 重建闭环回归基准/对比件（Comparer 输入） |
| `test.rebuilt.prt`（**已入库**，2026-09-04，141K） | U-7 词集 plan 重建的 prj′（ExecutorAdapter [I] 产物，13:55） | U-7 重建回归基准/对比件 |
| `test.rebuilt-143432.prt`（**已入库**，2026-09-04） | fixture 补读链重建件（plan 带 fixture_offset=1 后 ExecutorAdapter 复跑产物，14:34） | fixture 对照闭环回归件 |
| `test.plan.json`（**已入库**，2026-09-04，v1.5-③ S1 重导 19:49） | 由 test.prt 导出的 plan（ExporterAdapter-v15 产物，schema 落盘复验 PASS；U-7 形状：tools type/subtype = NX Tool.Types/Subtypes 原文；D-4 形状：无 machines/geometry_ref；**v1.5-③ 形状**：strategy KV Value 为 {N}/{S} 包装——腔 op 9 键（cut_pattern/cut_order/cut_direction NX 原文串 + finish/boundary×2/part/floor/depth）、六 op technology.spindle_rpm） | 合同冒烟/导出回归基线 |

## test.prt 盘点记录（2026-09-03，NX2406 会话 + dump journal 实证）

| 确认项 | 结果 |
|---|---|
| 打开 | NX2406 正常打开，无版本/格式转换提示（创建版本 ≤ 2406） |
| 部件 / CAM | 含实体几何；自带 CAMSetup（加工模块直接加载，无"创建环境"弹窗） |
| 结构盘点 | 见下要点；dump 产物 `samples/test.camdump.txt`，dump journal 源码 `src/NXPlugins/Journal/DumpCamSetup.cs` |
| 工序 | 6 条（4 Cavity Milling + 2 孔类）；**每个操作在四视图树各出现一次**（遍历须按 Tag 去重） |

### dump 结构树要点

- **程序顺序**（根 NC_PROGRAM → PROGRAM）：`A01`（4×Cavity Milling）、`A1-1`（打点）、`A1-3`（钻头G83）
- **几何**：单链 `MCS_MILL → WORKPIECE`，6 工序共用（几何父组均 WORKPIECE）
- **加工方法**（根 METHOD）：`MILL_ROUGH`/`MILL_SEMI_FINISH`/`MILL_FINISH`/`DRILL_METHOD` 组均空，操作全部直接挂根 METHOD
- **机床**（根 GENERIC_MACHINE → CARRIER / HEAD）：铣刀-5 参数 ×3（组名=直径 17.0 / 13.94 / 9.96）、HEAD → 倒斜铣刀（D6.0X90中心钻）、钻刀 ×2（8.5 / 17.5）
- 操作真实名（copy 链长名）：`CAVITY_MILL`、`CAVITY_MILL_COPY`、`CAVITY_MILL_COPY_COPY`、`CAVITY_MILL_COPY_COPY_COPY`、`打点_COPY_COPY_COPY`、`钻头G83_COPY_3_COPY_COPY_COPY_1`

### 实证结论（2026-09-03，均入库索引 §2.1/§2.5）

1. `CAMObject.GetNameOfType()` 返回**模板大类描述串**（`Cavity Milling` / `Point to Point` / `Generic PARAM object`…），**不是** `OperationCollection.Create()` 的 typeName 字面量；打点与钻头G83 均返回 `Point to Point`——**细分模板类型（定心钻 vs G83）无公开读回**。导出侧 `operation_type` 的来源须另找（候选：模板属性/UI 类型名，未实测）。
2. 刀具组 `GetNameOfType` 为中文模板名（铣刀-5 参数 / 倒斜铣刀 / 钻刀），**组名即规格值**（直径）→ 与刀具选型字段的对应已由 Builder 回读实证闭环（camprobe-executor/camprobe-u7：铣族 CutterSubtype 可读、全家族 `Tool.GetTypeAndSubtype` 可读且语言无关；通道与 schema 词集见 docs/nx-tool-type-enum-spec.md）。
3. COPY 链呈现为**独立 Operation 对象**（名带 _COPY 后缀链）→ plan 合同/重建侧"副本"表述口径待决策（schema v3 无副本字段）。
4. 四视图 UI 与四根组（`GetRoot(View)`）一一对应实证通过（含 `MachineMethod` ↔ 加工方法视图）。

**判定**：覆盖「铣 + 孔」最小口径，可作步骤①导出、③对比的 ground truth 首件。

### 2026-09-04 批处理证据档（run_journal 无界面驱动，结论回填索引 §2.1/§3）

| 文件 | 内容 |
|---|---|
| `camprobe-finalize-20260904-010401.txt` | 收官批探针终版（ok=7 fail=0）：Stepover 写链无效（E1-E6）、InheritanceStatus 语义（U-3）、SpindleMode 自由槽、ToolDrivePoint="SYS_CL_TIP"、PTP 可读/不可读面、MCS 扩展回读（U-4）、U-5c face 负结案；批处理 CAM 会话初始化纪律修订痕迹亦在档内（同批失败迭代档未保留） |
| `smoke-open-20260904-005304.txt` | run_journal 无界面批处理首证（ApplicationName=APP_NONE；无 `-nogui` 旗标） |
| `camprobe-executor-20260904-012518.txt` | Executor 预检探针（ok=6 fail=0）：CutterSubtype 读回/新刀具写链/非零 MCS 往返/FixtureOffset/方法父/hole_making 模板 |
| `executor-run-20260904-014930.txt` | ExecutorAdapter [I] 终版跑（ok=16 fail=0）：test.plan.json → test.rebuilt-014933.prt，回读对照全 PASS（6 工序/6 刀具/MCS） |
| `reopen-20260904-015129.txt` | I-3 自证：run_journal 新会话重开 prj′（ops=6） |
| `camprobe-u7-20260904-115251.txt` | U-7 探针（ok=3 fail=0）：六把库刀具 `as Tool`+GetTypeAndSubtype 全实证（6/6 下转；(Mill,Mill5)×3/(Mill,MillChamfer)/(Drill,DrillStandard)×2）+ 新建注册对读回校准（MILL↔Mill5、STD_DRILL↔DrillStandard），结论入索引 §2.1 与 docs/nx-tool-type-enum-spec.md |
| `adapter-run-20260904-132025.txt` | D-4 重导（ExporterAdapter v10，ok 全过）：test.prt → test.plan.json 新形状（无 machines/geometry_ref、features 瘦身），schema 校验（内存）+ 落盘复验 **PASS** |
| `executor-run-20260904-132124.txt` | D-4 最终 I-2 复跑（ok=16 fail=0）：对新 plan 全链重建 → test.rebuilt-132130.prt，回读对照全 PASS |
| `adapter-run-20260904-135344.txt` | **U-7 词集重导**（ExporterAdapter v11）：六刀 GetTypeAndSubtype 直写 NX 原文（Mill/Mill5×3、Mill/MillChamfer、Drill/DrillStandard×2），schema 内存+落盘复验 PASS，无 TOOL_TYPE_UNREADABLE |
| `executor-run-20260904-135456.txt` | **U-7 复跑**（ok=16 fail=0）：新词集 plan 全链重建 → test.rebuilt.prt，回读对照全 PASS；T-004 (Mill,MillChamfer) 注册对表未覆盖 → 分诊 diag + 铣注册对重建 |
| `adapter-run-20260904-141559.txt` | NxCollect 共享提取后重导（ExporterAdapter 瘦身首证，同 135344 形状） |
| `comparer-run-20260904-141713.txt` | **Comparer 首跑**：I-1 双件轮换点亮；暴露重建件刀具漏采 bug（tools B=0，depth 判据）→ 入选改 as Tool 判据 |
| `adapter-run-20260904-142216.txt` | 入选判据修复后重导重锚（gt 6 刀不变，零回归） |
| `comparer-run-20260904-142424.txt` | 修复后复跑：tools B=6；issues=6 校准清单（4 PTP 族差 + tool#4 MillChamfer→Mill5 + 程序根序）；fixture=0/0 缺口暴露 |
| `adapter-run-20260904-143344.txt` | FixtureOffset 补读后重导（plan 带出 fixture_offset=1） |
| `executor-run-20260904-143426.txt` | **fixture 链复跑**（ok=17 fail=0）：PASS fixture=1（plan=1）真对照 → test.rebuilt-143432.prt |
| `comparer-run-20260904-144237.txt` | **Comparer 终跑**（护栏版，干净会话）：双件正确轮换，issues=6 与校准清单逐条一致，fixture=1/1 闭环（Comparer 收口证据） |
| `camprobe-params-20260904-155341.txt` | **v1.5-④ 键集注册表探针**（ok=3 fail=0）：读面键集 16 键实态（15 可读 + PTP cycle 1 负证，腔铣 cut_pattern 等 + PTP 面）+ 写面矩阵六键首跑（c1-c4 持久、c5 multi_depth_cut.toggle 还原、c6 boundary_intol 还原——负结论待收口三跑） |
| `camprobe-params2-20260904-163751.txt`（-163823/-163850 同） | **v1.5-④ 收口三跑**（E1-E7 逐键自动判定，三会话一致）：锚点 E1/E7 持久 ✓；E2/E4 复刻还原 ✗；E3/E5/E6 邻接判别 → MultiDepthCut 整对象 + Boundary 容差族负结案（规格：docs/nx-param-registry-spec.md，源：src/NXPlugins/Journal/CamProbeParams2.cs） |
| `adapter-run-20260904-194935.txt` | **v1.5-③ S1 重导**（ExporterAdapter-v15）：test.plan.json 新形状——腔 op 6 新键（cut_pattern/cut_order/cut_direction NX 原文串 + finish/boundary×2 N）+ 六 op rpm 全到，schema 内存+落盘复验 PASS（I-1） |
| `executor-run-20260904-195159.txt` | **v1.5-③ 复跑**（ok=17 fail=0）：4 持久键 + rpm 真实写入无异常（FollowPeriphery/Profile/DepthFirst/Climb 复刻），boundary 拒收 diag ×4 → test.rebuilt-195208.prt（I-2） |
| `comparer-run-20260904-200339.txt` | **v1.5-③ 终跑**（B=-195208.prt 正确件；issues=5 全校准可解释）：腔对腔 cut_*/finish/boundary/rpm 由"键缺席"转全 PASS = 写入持久终判 + technology 维首亮；残余 = PTP 键错位 4 + tool#4 类型 1（I-3/I-4）。注：195504 首跑为错 B 件（旧主名）→ ComparerAdapter 参数语义改单参 B 覆盖（200022 实证） |

> 注意：西门子安装目录内文件（模板/样例/程序集）受许可约束，**只引用、不复制进 git**；
> 自建件由本仓库维护。
