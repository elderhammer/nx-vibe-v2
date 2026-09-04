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
| `test.plan.json`（**已入库**，2026-09-04，U-7 重导 13:53） | 由 test.prt 导出的 plan（ExporterAdapter v11 产物，schema 落盘复验 PASS；U-7 形状：tools type/subtype = NX Tool.Types/Subtypes 原文，六刀 (Mill,Mill5)×3/(Mill,MillChamfer)/(Drill,DrillStandard)×2；D-4 形状：无 machines/geometry_ref，features={id,type,params}） | 合同冒烟/导出回归基线 |

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

> 注意：西门子安装目录内文件（模板/样例/程序集）受许可约束，**只引用、不复制进 git**；
> 自建件由本仓库维护。
