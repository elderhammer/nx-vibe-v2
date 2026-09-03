# NX2406 安装目录资料索引与 API 核对记录

> 更新时间：2026-09-03（本机 NX2406：`C:\Program Files\Siemens\NX2406`）
> 用途：作为 [nxopen-research.md](./nxopen-research.md)（NX Open API 调研）与
> [nx-plugin-design.md](./nx-plugin-design.md)（插件设计）的**本地事实源索引**。
> 两份设计文档已按本文记录核对修正（2026-09-03）。
>
> 核对方法三路交叉：① `NXBIN\managed\NXOpen.xml`（.NET XML 文档注释）成员清单；
> ② PowerShell 反射 `NXOpen.dll` 真实签名；③ `UGOPEN\NXOpen\*.hxx`（C++ 头文件）声明。
> 反射脚本注意：`AssemblyResolve` 处理器内再 `LoadFrom` 会栈溢出，需加"解析中集合名"去重；
> 环境可能拦截 `powershell -ExecutionPolicy Bypass`，直接 `powershell -NoProfile -File` 即可。

---

## 1. 安装目录资源索引

| # | 资源 | 路径（相对 NX 根） | 内容 / 价值 | 用法 |
|:--|:--|:--|:--|:--|
| 1 | **C++ 头文件（含公开接口 doc comment）** | `UGOPEN\NXOpen\` | 11,406 个 `.hxx`，其中 CAM 类 902 个（`CAM_*.hxx`）；类级/方法级注释含 "Created in NXxxxx" | 查 C++ 精确类型与参数；`ls \| grep '^CAM_'` |
| 2 | **.NET XML 文档注释** | `NXBIN\managed\NXOpen.xml`（57 MB）等 | .NET API 完整公开成员清单；成员 remarks 含 **`License requirements: xxx`** 与 **`Created in NXxxxx`** 注记 | `grep -oE 'name="[MPT]:NXOpen\.CAM\.CAMSetup\.[^"]*"'` |
| 3 | .NET 程序集 | `NXBIN\managed\NXOpen.dll` 等 | 反射拿真实签名（属性类型/枚举值/方法返回），XML 不给类型 | 见文首反射脚本注意 |
| 4 | **CAM 官方样例（VB 脚本）** | `UGOPEN\SampleNXOpenApplications\DotNet\CAM\` | 28 个参数设置脚本（MCS 夹具偏置 `MCSSetFixtOffset1CycleAll.vb`、NCM `PlanarOpsSetNCMCycleAll.vb`、刀具刃长、孔加工碰撞检查等）；`OperationTypes.txt` 为 **UF 子类型号非官方清单**（非 NXOpen typeName 依据） | 抄参数设置范式 |
| 5 | **CAMSetup 导入参考实现（C#）** | `UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport\` | 按 XML 建 CAMSetup：机床（库）、Part/Blank/Fixtures、刀具（库）、主/子程序组。目标框架 v4.5 | 设计文档"导入侧"的现成起点 |
| 6 | 其他官方样例 | `UGOPEN\NXOpenExamples\CS\`、`UGOPEN\SampleNXOpenApplications\{C++,DotNet,Python,Java}\` | C++ CAM 仅 `OntSelectionBoilerPlate.cpp`（选择样板）；Python 无 CAM 例 | — |
| 7 | 无 GUI 运行入口 | `NXBIN\run_journal.exe` | 批处理入口存在；`-nogui` 参数需实测 | 插件 CI 入口 |
| 8 | **CAM 模板部件** | `mach\resource\template_part\{metric,english}\` | `mill_contour.prt`、`drill.prt`、`mill_feature.prt`、`MillTurn_Exp.prt`…；`cam_general_mill.prt` **2406 不存在** | `Part.CreateCamSetup(templateName)` 的 templateName 来源 |
| 9 | 模板集配置 | `mach\resource\template_set\cam_general.opt` | 默认配置引用 mill_contour.prt 等 | 建 CAMSetup 初始化 |
| 10 | 其他 SDK 素材 | `UGOPEN\`（Open C `uf_*.h`、`.lib`）、`mach\resource\`（post/tool 库/wizard） | UFUN 接口与 CAM 资源 | 底层兜底 |

---

## 2. NX2406 API 事实速查（核对产出，文档引用以此为准）

### 2.1 组树 / 对象模型（相对"旧四视图对象"形态有重大变化）

- `CAMSetup` 只暴露两个集合：`CAMGroupCollection`（→`NCGroupCollection`，**四视图组对象统一仓库**）、`CAMOperationCollection`（→`OperationCollection`，操作 + **全部操作 Builder 工厂**）。
- **`ProgramOrderView / MachineToolView / GeometryView / MethodView` 类及 `.Root` 在 NX2406 已不存在**（XML/头文件零命中，属旧版形态）。
- 视图根组：`camSetup.GetRoot(CAMSetup.View)`，`CAMSetup.View` 枚举 = `ProgramOrder | MachineMethod | Geometry | MachineTool`（2026-09-03 实证：四根组与 UI 四个导航标签一一对应，见下）。
- 组创建（`NCGroupCollection`，全部 cam_base）：`CreateProgram/CreateTool/CreateMethod/CreateGeometry(parentGroup, typeName, subtypeName, useDefaultName, newGroupName)` —— **必须传父组**（顶层传 `GetRoot(...)` 根组）。**typeName/subtypeName 语义（2026-09-03 实证）**：typeName=**模板部件名**（如 `mill_contour`/`mill_planar`），subtypeName=**对象模板类型**（如 `PROGRAM`/`MILL`）；空 subtype 非法。实测配对：Program=(mill_contour, PROGRAM)、Method=(mill_contour, MILL_METHOD)、Tool=(mill_planar, MILL)、Geometry=(mill_contour, WORKPIECE)。
- 树遍历：`NCGroup.GetMembers() / GetParent()`；操作挂四父链：`Operation.ParentProgramOrder / ParentMachineTool / ParentGeometry / ParentMachineMethod`（`GetParent(CAMSetup.View)` 亦可）。
- 类型名读回（2026-09-03 反射 + dump 实测）：`CAMObject.GetNameOfType()` → string（CAMSetup/NCGroup/Operation 通用；**XML remarks 标注 "internal API, can be changed at any time"**，Created in NX1899，cam_base/insp_programming）。**实测返回模板大类描述串**（test.prt dump：`Cavity Milling` / `Point to Point` / `Generic PARAM object` / `Tool Carrier` / `Head`），**不是 `Create()` 的 typeName 字面量**；打点与钻头G83 均返回 `Point to Point` → 细分模板类型无公开读回，导出侧 `operation_type` 来源另找（候选：模板属性/UI 类型名，未实测）。不得固化为导出侧正式接口；subtypeName **无公开读回成员**（全 CAM 命名空间零命中，见 §2.5）。
- 四视图 UI 标签与 `GetRoot(View)` 四根组一一对应（2026-09-03 test.prt dump 实证：内容语义吻合，MachineMethod 根下为方法组）。
- 打开既有 prt（无只读重载）：`PartCollection.Open(string, out PartLoadStatus)` → Part（另见 `OpenDisplay`/`OpenActiveDisplay`）；只读纪律靠"不保存"。空会话取件纪律（2026-09-03 实测）：**只能用 `OpenDisplay` 再 `SetWork`**——空会话下 `Open` 仅装载、`SetWork`/`SetDisplay` 均报"无显示部件"；文件已被会话打开后 `Open`/`OpenDisplay` 均报"文件已存在"（探针须在干净会话运行）。
- 取件完整纪律增补（2026-09-04 适配器实战）：**NX Execute 给 Main 传 1 个空参数**（args[0]=""——路径参数必须判 IsNullOrEmpty）；**`Part.Name` 不含扩展名**（"test" 而非 "test.prt"）；943006="文件已存在"，**所有 `Open*` 对已装载文件一律拒绝**；隐藏装载（非显示/非工作）部件不在 `Work`/`GetDisplayedParts` —— 枚举装载部件用 `UFPart.AskNumParts()/AskNthPart(i)` → `NXObjectManager.Get(tag) as Part`，提升显示用 `uf.Part.SetDisplayPart(tag)`。
- PTP 旧模板操作（打点/钻头G83，`GetNameOfType`="Point to Point"）的 Builder = **`PointToPointBuilder`**（工厂 `CreatePointToPointBuilder`）；`CreateHoleDrillingBuilder` 对其**强转失败**——仅新模板 `DRILLING` 家族使用 HoleDrillingBuilder（2026-09-04 适配器实证）。
- MCS 回读（U-4 首证，源 samples/adapter-run-20260904-003906.txt）：`CreateMillOrientGeomBuilder(mcsGroup).Mcs` → `CartesianCoordinateSystem.Origin`（Point3d）+ `Orientation.Element`（Matrix3x3 行，Xx…/Zx…）；实测 test.prt origin=(75,0,100)、z=(0,0,1)。
- 模板注册表枚举（2026-09-03 实证，源：samples/camprobe-types.txt）：`Session.CAMSession`（`IsCamSessionInitialized()`/`CreateCamSession()`）；`GetTemplateTypes()` → string[]；`GetTemplateSubtypes(typeName, CAMSession.ObjectSubtype{Setup|Tool|Method|Geometry|Operation|Program})` → string[]。17 个模板部件（mill_planar/hole_making/mill_contour/…）的完整子类型注册表 = 重建侧 Create 参数权威来源；导出侧 `Tool.GetTypeAndSubtype(out Types, out Subtypes)` 可读回刀具类型。
- 生效值回读与两处写入未保留（2026-09-03 实测，源：samples/camprobe-op.txt）：未显式设置的 Inheritable 参数**回读生效值可行**（FloorStock → 1，设计 2.1"导出回读生效值"方案成立）；但 `BoundaryInTol` 写 0.01 回读 0、Stepover `PercentToolFlatBuilder.Value` 写 50 回读 70——写入未保留，语义待查（勿固化映射，见 §3 项 3）。
- 孔加工与刀路（2026-09-03 实测，源：samples/camprobe-drill.txt、camprobe-toolpath.txt）：模板对 `(hole_making, DRILLING)`/`(hole_making, SPOT_DRILLING)`、刀具 `(hole_making, STD_DRILL)`、方法组 `DRILL_METHOD` 均创建成功；`HoleDrillingBuilder.CuttingParameters.BottomStock` 写读一致（Inheritable 形态）；`CycleTable` 类型实证 = `NXOpen.CAM.Cycle`（§2.5"无 Cycle 属性"正向确认）；`CAMSetup.GenerateToolPath(CAMObject[])` 运行成功（当前许可覆盖刀路生成），`Operation.GetToolpathTime()/GetToolpathLength()` 回读真实数值；新建 DRILLING 的 `GetNameOfType`="Drilling"（test.prt 旧工序="Point to Point"——随模板来源变，佐证其不可作 typeName 依据）。
- 操作创建：`OperationCollection.Create(programG, methodG, toolG, geomG, typeName, subtypeName, UseDefaultName, newName)`；第 7 参是**枚举** `OperationCollection.UseDefaultName.{False,True}` 不是 bool。**typeName/subtypeName 与组创建同族语义（2026-09-03 实证）**：模板部件名/操作子类型对，如 CAVITY_MILL=(mill_contour, CAVITY_MILL)，空 subtype 报"需要的模板不存在"（旧文献把 typeName 直接当字面量传已修正，见 nxopen-research §3.2）。
- 操作 Builder 工厂：`OperationCollection.CreatePlanarMillingBuilder(operation)` 等约 75 个 + 通用 `CreateBuilder(CAMObject)`（**不在 CAMSetup 上**；CAMSetup 只有 18 个非操作类 Builder 工厂）。类名注意：`ZLevelMillingBuilder`（工厂 `CreateZlevelMillingBuilder`，L 大小写不一致）。
- `CAMSetupBuilder` 类**不存在**；初始化 CAMSetup = `Part.CreateCamSetup(templateName)`（单参，cam_base）。

### 2.2 属性取值形态（四类混合——Mapper 必须按类型分支）

| 形态 | 特征 | 实例（NX2406 .NET 实测） |
|:--|:--|:--|
| `Inheritable*Builder` | 需 `.Value = …` | `CutParameters.PartStock/FloorStock/WallStock`、`FeedsBuilder.SpindleRpmBuilder`、`builder.DepthPerCut`、`HoleMachiningCutParameters.BottomStock/BottomClearance`、工具 `TlDiameterBuilder` 等 |
| 直接 double | 直赋 | `NcmClearanceBuilder.SafeDistance/Radius/BoundingBoxClearance`、`MillCutParameters.BoundaryInTol/OutTol`、`NcmClearanceBuilder` 之外如 `FinishPassesBuilder.NumberOfFinishPasses`（int） |
| 直接枚举 | 直赋 | `MillCutParameters.CutOrder`（类型 `CutParametersCutOrderTypes`）、`NcmClearanceBuilder.ClearanceType`（嵌套 `ClearanceTypes`）、`HoleDrillingBuilder.ControlPointOffset/RetractOutputMode/IntersectionStrategy`、`StepoverBuilder.StepoverType` |
| 类 + 嵌套枚举 | `xxx.Type = Class.Types.值` | `CutDirection.Type = CutDirection.Types.Climb`、`CutPatternBuilder.CutPattern = CutPatternBuilder.Types.FollowPart`、`MultiDepthCut.Toggle/StepMethod` |

> 参数面更细的取值形态修正以两文档为准；本节列举关键差异，完整清单见 [nxopen-research.md](./nxopen-research.md) 附 A。

### 2.3 关键枚举宿主与取值（均 .NET 反射实证）

| 概念 | 类型（宿主） | 取值 |
|:--|:--|:--|
| 切削顺序 | `CutParametersCutOrderTypes`（**顶层类型**） | `LevelFirst\|DepthFirst\|DepthFirstAlways`（**无 AreaFirst**） |
| 顺逆铣 | `CutDirection`（类）→ 嵌套 `Types` | `Climb\|Conventional\|Forward\|Reverse\|Mixed`（**无 Up**，逆铣=Conventional） |
| 切削模式 | `CutPatternBuilder.Types` | `FollowPart\|FollowPeriphery\|Helical\|Spiral\|…\|Zig\|ZigZag\|Profile\|…` 36 值（**无 HILBERT/PARALLEL_LINES**） |
| 步距 | `StepoverBuilder.StepoverTypes` | `Constant\|Scallop\|PercentToolFlat\|Multiple\|Number\|Maximum\|…`（**StepoverBuilder 无 Percent 属性**） |
| 安全几何 | `NcmClearanceBuilder.ClearanceTypes` | `UseCommon\|None\|Automatic\|Plane\|Point\|Cylinder\|Sphere\|BoundingBox\|BoundingCylinder\|Body\|MachineBased` |
| 铣刀子类型 | `MillToolBuilder.CutterSubtypes` | `Mill5\|Mill7\|Mill10\|MillBall\|ChamferTool\|SphericalMill\|DovetailMill` |
| 多刀深 | `MultiDepthCut.Types` | `Increment\|Passes` |
| 精加工刀数 | `FinishPassesBuilder` | `NumberOfFinishPasses`（int）|
| 操作默认命名 | `OperationCollection.UseDefaultName` | `False\|True` |
| 视图 | `CAMSetup.View` | `ProgramOrder\|MachineMethod\|Geometry\|MachineTool` |
| 孔：控制点偏置 | `HoleDrillingBuilder.ControlPointOffsetType` | `None\|Feature\|Initial`（文档旧描述"孔顶/孔底/自动"需对账 UI） |
| 孔：退刀输出 | `HoleDrillingBuilder.RetractOutputModeType` | `ClearanceOnly\|ClearanceInitial\|Always` |
| 孔：相交策略 | `HoleDrillingBuilder.IntersectionStrategyType` | `None\|Part\|Ipw\|IpwAndPart` |

### 2.4 版本 / 许可注记实证

- `HoleMachiningCutParameters.BottomClearance`：remarks = **"Created in NX2312.0.0"** → 两文档关于"NX2312 新增"的说法成立。
- 许可注记样例：`OperationCollection.Create`/组创建/`CreatePlanarMillingBuilder` = `cam_base`；`CAMSetup.CreateFeatureProcessBuilder` = **`ug_holemaking`** → 许可检查应按 XML remarks 程序化探测，而非手写许可表。
- 许可探测配方（2026-09-03 实证，源：samples/camprobe-license.txt）：运行时宿主 = `Session.LicenseManager`（成员实证：`Reserve(license, context)/Release`、`CheckPresence`、`IsCheckedOut`）；做法 = 从 NXOpen.xml 提取成员 `License requirements:` 注记（截到 `</para>`；**部分 member 名无参数括**，前缀匹配须容错）→ 对每个许可 token 走 `Reserve` 预占试错。本机 `cam_base` 与 `ug_holemaking` 均可用（Reserve=OK）→ 风险 #5 缓解方案落地（许可缺失时可前置报错而非等调用失败）。注：注记含 `("UG Holemaking")` 等显示名，token 提取注意大小写噪音（如误切出 "olemaking"）。

### 2.5 易错"不存在项"清单（NX2406）

`CAMSetupBuilder`；`CAMSetup.ProgramOrderView/MachineToolView/GeometryView/MethodView`；`camSetup.CreatePlanarMillingBuilder(...)`（应在 OperationCollection）；`MillCutParameters.DepthPerCut`（应在 `PlanarOperationBuilder/CavityMillingBuilder`）；`Stepover.Percent`；`MillCutParameters.CutOrder` 用顶层 `CutOrder` 枚举（类型是 `CutParametersCutOrderTypes`）；`HoleMachiningBuilder.Cycle`（**只有 `CycleTable`**，类型 `CAM.Cycle`）；`Operation.gougeCheck / getCuttingTime / getCuttingLength`（gouge 在 `CAMSetup.GougeCheck/CreateGougeCheckBuilder` 与 `Operation.GougeCheckStatus/Results`）；`MillingToolBuilder.holderSectionBuilder`（有 `ShankSectionBuilder`）；`setMcs/setRcs` 方法（`Mcs/Rcs` 是可写属性）；`cam_general_mill.prt`（2406 用 `mill_contour.prt` 等）；`CAMObject` 的 subtypeName/子类型读回成员（XML/反射零命中，仅有 `GetNameOfType()` 且为内部 API）；`PartCollection.OpenReadOnly`（无只读打开重载）。

---

## 3. 待运行时验证清单（本地资料无法证实的项；★=2026-09-03 NX 会话已实证）

1. ~~`OperationCollection.Create` 的 typeName 字面量~~ ★ 已实证：typeName=模板部件名 + subtypeName=子类型（如 `(mill_contour, CAVITY_MILL)` 创建成功，§2.1）；`GetNameOfType()` 返回模板大类描述串、不能替代字面量（§2.1）。
2. ~~组创建 `typeName/subtypeName` 实际取值~~ ★ 已实证：配对见 §2.1（组与操作同族语义）；完整注册表 = CAMSession 枚举（samples/camprobe-types.txt）。
3. `StepoverBuilder` 常量百分比链路：**已实测写入未保留（50→70，见 §2.1/源 camprobe-op.txt），生效机制待查**；`ToolDrivePoint` string 取值集合、`SpindleModeBuilder` 数值编码仍未测。
4. ~~`CreateCamSetup("mill_contour")` 空 Part 初始化流程~~ ★ 已实证：创建成功且模板自带默认组（MILL_* 方法组/MCS_MILL/PROGRAM/GENERIC_MACHINE，见 CamWriteProbe 输出）；`run_journal.exe -nogui` 批处理参数**仍未测**。
5. ~~`CAMSetup.View.MachineMethod` 与 UI"加工方法视图"标签的对应关系~~ ★ 已实证（2026-09-03 test.prt dump：四根组与 UI 四导航标签一一对应，见 §2.1）。

---

## 4. 审查结论摘要（2026-09-03 核对修正对照）

| 文档 | 结论 | 主要修正点（详见各文档） |
|:--|:--|:--|
| [nxopen-research.md](./nxopen-research.md) | 能力全景总体成立；**§3.1-3.2 示例与多处枚举/参数面需按 NX2406 修正** | 对象模型（GetRoot/View 枚举/集合仓库）；Builder 工厂宿主；属性取值四形态与枚举宿主；孔/刀路 API 细节；许可与版本注记机制；typeName 待实测项 |
| [nx-plugin-design.md](./nx-plugin-design.md) | 三步闭环架构成立 | `CAMSetupBuilder` 引用删除；组树/回读/工厂按新模型表述；模板引用更新；新增"属性形态表先行"与"枚举按工厂校准"实施步骤 |
