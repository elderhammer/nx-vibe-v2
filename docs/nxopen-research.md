# NX Open API 调研：CAM 编程能力全景与 CAPP Plan 对接

更新时间：2026-09-03（按本机 NX2406 安装资料核对修正；资源索引/事实速查见 [nx2406-install-index.md](./nx2406-install-index.md)，属性取值形态速查见附 A）\
适用范围：Siemens NX 2406+ / NX X（API 以 NXOpen .NET 为准，C++/Python/Java 同名；§3-4 的代码与表格已按 NX2406 实际 API 面修订；插件工程仅支持 2406，见 nx-plugin-design.md 头部决策②）\
关联文档：[autocam-plan.schema.json](../schema/autocam-plan.schema.json)（本仓库 schema/；PRD 与特征→工序映射表属外部模块，未挂载，2026-09 起以本仓库为合同唯一持有方）

---

## 1. 阅读指南

- 了解 NX Open 整体能力：读第 2 章
- **了解 NX CAM 编程 API（创建工序/刀具/几何/MCS/非切削/刀路生成）**：读第 3 章
- **指导 CAPP Plan 输出字段设计（按 NX Builder 参数面反推）**：读第 4 章
- 评估对接落地风险：读第 5 章
- 特征识别与交叉验证（外部模块文档，未挂载；当前 NX 闭环以 face_anchors 属性快照替代，见 schema geometry_ref 注记）

> 本文档第 3-4 章是核心新增内容：回答"NX Open 能否让 CAPP 输出 Plan 后直接创建工序、包含完整参数"以及"Plan 应该输出什么才能无歧义映射到 NX"。

---

## 2. NX Open API 全景

### 2.1 什么是 NX Open

NX Open 是 Siemens NX 的官方应用编程接口（API）框架，支持 **C++、C#、Python、Java** 四种语言。它包含两套子系统：

| API 体系                  | 风格        | 语言                          | 特点                                       |
| ----------------------- | --------- | --------------------------- | ---------------------------------------- |
| **NXOpen (Common API)** | 面向对象      | C# / C++ / Python / Java    | 封装了 NX 绝大多数功能，使用 NXOpen 命名空间             |
| **Open C (UFUN)**       | 过程式 C API | C / C++ / C# (NXOpen.UF 封装) | 底层几何操作，效率高，函数前缀 `UF_MODL_` / `UF_OBJ_` 等 |

在 .NET 环境下通过 `NXOpen.UF.UFSession` 可无缝调用 Open C 函数。**CAM 编程全部走 NXOpen 面向对象 API**（`NXOpen.CAM` 命名空间），无需 UFUN。

### 2.2 NXOpen.Features 设计特征类层次

`NXOpen.Features.Feature` 是抽象基类，派生 200+ 具体特征子类：

```
NXOpen.Features.Feature (抽象基类)
├── NXOpen.Features.Block            ← 方块
├── NXOpen.Features.HolePackage      ← 孔包
├── NXOpen.Features.Pocket           ← 腔体
├── NXOpen.Features.Slot             ← 槽
├── NXOpen.Features.Boss             ← 凸台
├── NXOpen.Features.Chamfer          ← 倒角
├── NXOpen.Features.Blend            ← 圆角
├── NXOpen.Features.Rib              ← 筋
├── NXOpen.Features.Shell            ← 壳
├── NXOpen.Features.Thread           ← 螺纹
├── NXOpen.Features.Sweep            ← 扫掠
├── NXOpen.Features.Revolve          ← 旋转
├── NXOpen.Features.BooleanFeature   ← 布尔
├── NXOpen.Features.Pattern          ← 阵列
├── NXOpen.Features.MirrorFeature    ← 镜像
├── NXOpen.Features.DatumCSYS        ← 基准坐标系
├── NXOpen.Features.Sketch           ← 草图
├── ... 200+ 个特征子类
```

> 注意：这是**设计特征**（怎么建模的），与加工特征（怎么加工的）是两套体系。第 3 章的 CAM 操作才是加工侧。

### 2.3 核心 API 能力矩阵

| 能力维度          | API 函数                                                     | 精度                     | 是否可用 |
| ------------- | ---------------------------------------------------------- | ---------------------- | ---- |
| B-Rep 拓扑访问    | `Body.GetFaces()`, `Face.GetEdges()`, `Edge.GetVertices()` | 精确值，无三角化误差             | ✅    |
| 面类型识别         | `UF_MODL_ask_face_data()` / `Face.SolidFaceType`           | 11 种标准面类型枚举            | ✅    |
| 面面积           | `UF_MODL_ask_face_area()`                                  | 精确值                    | ✅    |
| 边凸凹性          | `UF_MODL_ask_edge_convexity()`                             | CONVEX/CONCAVE/TANGENT | ✅    |
| 边长度           | `Edge.GetLength()`                                         | 精确值                    | ✅    |
| Body BBox     | `UF_MODL_ask_bounding_box()`                               | 精确值                    | ✅    |
| 体积/表面积        | `UF_MODL_ask_mass_props_3d()`                              | 精确值                    | ✅    |
| 面法向           | `AskFaceNormals()`                                         | 精确向量                   | ✅    |
| 面 UV 参数域      | `UF_MODL_ask_face_uv_minmax()`                             | 精确值                    | ✅    |
| 设计特征 ↔ 面映射    | `Feature.GetEntities()`, `BodyFeature.GetFaces()`          | 直接关联                   | ✅    |
| 特征参数读取        | `GetExpressions()`, 具体 Builder 的 Get 方法                    | 设计参数原值                 | ✅    |
| 无 GUI 运行      | `run_journal.exe -nogui`                                   | 支持批处理                  | ✅    |
| Remoting 长驻服务 | NXOpen Remoting                                            | 支持服务化                  | ✅    |

### 2.4 NX Open API 编程示例

```csharp
// 遍历设计特征树
Part workPart = theSession.Parts.Work;
foreach (Features.Feature feat in workPart.Features)
{
    string typeName = feat.FeatureType();    // "HolePackage", "Block"...

    // 获取特征产生的面
    if (feat is Features.BodyFeature bodyFeat)
    {
        Face[] faces = bodyFeat.GetFaces();
        foreach (Face face in faces)
        {
            double area = theUfSession.Modl.AskFaceArea(face.Tag);
        }
    }
}
```

---

## 3. NX CAM 编程 API 全景（核心）

> 本章回答：**NX Open 提供了完整的 CAM 编程 API，可以在一个 .prt 会话里从零创建"程序组 → 刀具 → 几何 → MCS → 工序 → 刀路"完整链条，参数面非常完整**。CAPP 输出 Plan 后由 NX 插件（或 NX Open Remoting 服务）映射执行是可行的。

### 3.1 CAM 对象模型（NX2406 实证口径）

一个 Part 对应一个 CAM 配置（`CAMSetup`）。UI 上 CAMSetup 以**四个导航视图**组织
（程序顺序 / 机床 / 几何 / 加工方法），每个 Operation 挂在一个 Program / Method /
Tool / Geometry 组之下（四视图交集定位）——**该概念模型不变**。

> ⚠️ **NX2406 API 形态变化**（本机 XML 文档 + 反射实证）：旧文献常见的
> `camSetup.ProgramOrderView.Root` 等**四视图对象类在 NX2406 已移除**（本地零命中）。
> 2406 把四视图组对象统一收进 `CAMGroupCollection`（`NCGroupCollection`），各视图根组
> 用 `GetRoot(CAMSetup.View)` 取；操作集合为 `CAMOperationCollection`（`OperationCollection`，
> 且**操作 Builder 工厂全部挂在它上面**，见 3.2/3.3）。

```
CAMSetup (Part.CAMSetup；空 Part 先 Part.CreateCamSetup(templateName))
├── CAMGroupCollection : NCGroupCollection        ← 四视图组对象统一仓库
│     ├── GetRoot(CAMSetup.View.ProgramOrder)     ← 程序顺序树根（刀路输出顺序）
│     ├── GetRoot(CAMSetup.View.MachineTool)      ← 机床/刀具树根
│     ├── GetRoot(CAMSetup.View.Geometry)         ← 几何树根（MCS / WORKPIECE / PART / BLANK…）
│     └── GetRoot(CAMSetup.View.MachineMethod)    ← 加工方法树根（粗/精/半精/钻孔…）
├── CAMOperationCollection : OperationCollection  ← 所有 Operation + CreateXxxBuilder 工厂
└── 组树遍历：NCGroup.GetMembers() / NCGroup.GetParent()
     Operation 四父链：ParentProgramOrder / ParentMachineTool / ParentGeometry / ParentMachineMethod
```

**API 入口**：

```csharp
Session theSession = Session.GetSession();
Part workPart = theSession.Parts.Work;

CAM.CAMSetup camSetup = workPart.CAMSetup;              // 已存在
// 空 Part：workPart.CreateCamSetup("mill_contour");    // 参数=模板类型名（2406 模板见附 B）

// 组集合 + 操作集合
CAM.NCGroupCollection groups = camSetup.CAMGroupCollection;
CAM.OperationCollection ops  = camSetup.CAMOperationCollection;

// 各视图根组（建组时作父组；按 CAMSetup.View 枚举取）
CAM.NCGroup progRoot   = camSetup.GetRoot(CAM.CAMSetup.View.ProgramOrder);
CAM.NCGroup toolRoot   = camSetup.GetRoot(CAM.CAMSetup.View.MachineTool);
CAM.NCGroup geomRoot   = camSetup.GetRoot(CAM.CAMSetup.View.Geometry);
CAM.NCGroup methodRoot = camSetup.GetRoot(CAM.CAMSetup.View.MachineMethod);
```

> `CAMSetup.View` 取值：`ProgramOrder | MachineMethod | Geometry | MachineTool`。
> 组创建必须显式传父组（顶层传根组），且 (typeName, subtypeName) 为组模板类型串，见 3.2。

### 3.2 创建一条工序的完整链路（C# 示例，NX2406 已核对签名）

标准流程五步：**取根组建四类组 → Create 操作 → 取 Builder 设参 → Commit → Destroy**。

```csharp
// ---- 1. 建组：父组 = 各视图根组（见 3.1）；(typeName, subtypeName)=模板部件名/对象模板类型
//        （2026-09-03 NX 会话实证，见 nx2406-install-index.md §2.1）；第 4 参是
//         NCGroupCollection.UseDefaultName 枚举 ----
CAM.NCGroup programGroup = groups.CreateProgram(progRoot,
    "mill_contour", "PROGRAM", CAM.NCGroupCollection.UseDefaultName.True, "PROGRAM_MAIN");
CAM.NCGroup methodGroup  = groups.CreateMethod(methodRoot,
    "mill_contour", "MILL_METHOD", CAM.NCGroupCollection.UseDefaultName.True, "MILL_ROUGH");
CAM.NCGroup toolGroup    = groups.CreateTool(toolRoot,
    "mill_planar", "MILL", CAM.NCGroupCollection.UseDefaultName.True, "T1_D10");
CAM.NCGroup geomGroup    = groups.CreateGeometry(geomRoot,
    "mill_contour", "MCS", CAM.NCGroupCollection.UseDefaultName.True, "MCS_1");

// ---- 2. 创建操作：四个父组 + (typeName=模板部件名, subtypeName=操作子类型) + 命名枚举 + 新名
//        （2026-09-03 实证：CAVITY_MILL 注册于 mill_contour 部件下；空 subtype 非法）----
CAM.Operation operation = ops.Create(
    programGroup, methodGroup, toolGroup, geomGroup,
    "mill_contour",                             // typeName（模板部件名）
    "CAVITY_MILL",                              // subtypeName（操作模板类型，见 3.3 注）
    CAM.OperationCollection.UseDefaultName.True,// 注意：是枚举，不是 bool
    "CAVITY_1");

// ---- 3. Builder 设参：工厂在 OperationCollection 上（不在 CAMSetup 上）----
CAM.PlanarMillingBuilder builder = ops.CreatePlanarMillingBuilder(operation);
builder.CutParameters.PartStock.Value     = 0.3;  // 侧壁余量（InheritableDoubleBuilder → .Value）
builder.CutParameters.FloorStock.Value    = 0.3;  // 底面余量
builder.CutParameters.Stepover.StepoverType = CAM.StepoverBuilder.StepoverTypes.PercentToolFlat;
builder.CutParameters.Stepover.PercentToolFlatBuilder.Value = 50.0;  // 50% 刀径（取值链路待实测）
builder.DepthPerCut.Value                  = 2.0;  // 每刀深度：在 PlanarOperationBuilder 上（非 CutParameters）
builder.CutParameters.CutOrder             = CAM.CutParametersCutOrderTypes.LevelFirst; // 直接枚举，无 .Value
builder.CutParameters.CutDirection.Type    = CAM.CutDirection.Types.Climb; // 类+嵌套枚举 .Type（无 Up，逆铣=Conventional）
// 非切削移动（安全平面 + 进刀/退刀），见 3.10（铣削=NcmPlanarBuilder；孔加工为 NcmHoleMachining）
CAM.NcmPlanarBuilder ncm = builder.NonCuttingBuilder;
ncm.ClearanceBuilder.ClearanceType = CAM.NcmClearanceBuilder.ClearanceTypes.Plane; // 直接枚举赋值
ncm.ClearanceBuilder.SafeDistance  = 10.0;                                          // 直接 double
// 进给与转速，见 3.4 FeedsBuilder
builder.FeedsBuilder.SpindleRpmBuilder.Value = 6000;
builder.FeedsBuilder.FeedCutBuilder.Value    = 1200;

// ---- 4. 提交 / 销毁 ----
NXOpen.NXObject nXObject = builder.Commit();
builder.Destroy();
```

关键约定（NX2406 实证）：

- `Create()` 的 **typeName / subtypeName** 语义 = **模板部件名 / 对象模板类型**（2026-09-03 NX 会话实证：`(mill_contour, CAVITY_MILL)` 创建成功、空 subtype 报"需要的模板不存在"；组创建同族，见索引 §2.1）。完整合法配对用 `Session.CAMSession.GetTemplateTypes()/GetTemplateSubtypes()` 枚举（重建侧参数来源，勿手写表）；Plan 的 `nx_template` 字段建议存这对字面量。
- 每个操作类型对应一个 **Builder 类**；工厂（约 75 个 `CreateXxxBuilder(operation)` + 通用 `CreateBuilder`）**全部挂在 `OperationCollection` 上**；Builder 暴露的参数即 NX 工序对话框参数。
- Builder 是**增量修改**模型：`builder` 属性读当前值、属性写值、`Commit()` 生效、`Destroy()` 释放。
- **参数取值是四种形态混合**（速查见附 A），不是统一的 `xxx.Value = …`：
  1. `Inheritable*Builder` → `.Value = x`（余量/转速/进给/深度/刀具尺寸等绝大多数）；
  2. 直接 `double` / `int` → 直赋（`NcmClearanceBuilder.SafeDistance`、`MillCutParameters.BoundaryInTol`、`FinishPassesBuilder.NumberOfFinishPasses`）；
  3. 直接枚举 → 直赋（`CutOrder = CutParametersCutOrderTypes.LevelFirst`、`ClearanceType`、`StepoverType`…）；
  4. 类 + 嵌套枚举 → `.Type = 类.Types.值`（`CutDirection.Type = CutDirection.Types.Climb`、`CutPatternBuilder.CutPattern`…）。
- 所有参数面均可**不设置**——不设置时工序继承所在组的默认值（Inheritable 语义，见 §5 风险 #4），这也是"最小 Plan 也能出刀路"的基础。

### 3.3 操作类型全景（不限于 AP224 15 类）

`OperationCollection.Create()` 的 typeName 覆盖 NX CAM 全部加工域，按大类列举如下（**全部 cam_base 许可**）：

| 操作大类 | typeName 示例（非穷举） | 对应 Builder | 用途 |
| :--- | :--- | :--- | :--- |
| **铣削-2.5轴** | `CAVITY_MILL` / `PLANAR_MILL` / `FACE_MILLING` / `PLUNGE_MILL` / `GROOVE_MILL` | `CavityMillingBuilder` / `PlanarMillingBuilder` / `FaceMillingBuilder` / `PlungeMillingBuilder` / `GrooveMillingBuilder` | 挖槽/平面轮廓/面铣/插铣/槽铣 |
| **铣削-3轴** | `ZLEVEL_PROFILE` / `ZLEVEL_FOLLOW_PARTS` / `SURFACE_CONTOUR` / `FLOWCUT` / `CHAMFER_MILL` / `ENGRAVE` / `CYLINDER_MILL` | `ZLevelMillingBuilder` / `SurfaceContourBuilder` / `FlowcutBuilder` / `ChamferMillingBuilder` / `EngravingBuilder` / `CylinderMillingBuilder` | 等高/曲面轮廓/清根/倒角/雕刻/圆柱铣 |
| **孔加工** | `DRILL` / `SPOT_DRILLING` / `PECK_DRILLING` / `BREAK_CHIP_DRILLING` / `TAPPING` / `THREAD_MILLING` / `REAMING` / `BORING` / `COUNTERBORE` / `COUNTERSINK` / `BACK_BORING` / `DRILL_DEEP` | `HoleDrillingBuilder`（继承 `HoleMachiningBuilder`）/ `PointToPointBuilder` / `ThreadMillingBuilder` | 定心/钻孔/啄钻/断屑/攻丝/螺纹铣/铰/镗/沉头 |
| **特征驱动** | `FEATURE_MILLING` | `FeatureMillingBuilder` | 按 CAM 特征（AFR）生成工序，见 3.12 |
| **车削** | `ROUGH_TURNING` / `FINISH_TURNING` / `THREAD_TURNING` / `CENTERLINE_DRILL_TURNING` / `MULTI_AXIS_TURN_MILL` | `RoughTurningBuilder` / `FinishTurningBuilder` / `ThreadTurningBuilder` / `CenterlineDrillTurningBuilder` / `MultiAxisTurnMillingBuilder` | 粗车/精车/车螺纹/轴向钻孔/多轴车铣 |
| **多轴** | `MULTI_AXIS_ROUGHING` / `MULTI_AXIS_WALL_FINISHING` / `MULTI_AXIS_DEBURRING` | `MultiAxisRoughingBuilder` / `MultiAxisWallFinishingBuilder` / `MultiAxisDeburringBuilder` | 多轴粗/侧壁精/去毛刺 |
| **WEDM** | `WEDM_OPERATION` | `WedmOperationBuilder` | 线切割 |
| **增材** | `PLANAR_ADDITIVE_DEPOSIT` / `ROTARY_ADDITIVE_DEPOSIT` | `PlanarAdditiveDepositBuilder` / `RotaryAdditiveDepositBuilder` | 增材沉积（平面/旋转） |
| **探测** | `ON_MACHINE_PROBING` / `MILL_TOOL_PROBING` | `OnMachineProbingBuilder` / `MillToolProbingBuilder` | 机内测量/对刀 |
| **机床控制** | `MILL_MACHINE_CONTROL` | `MillMachineControlBuilder` | 换刀、冷却开关、用户事件等 |
| **用户定义** | `MILL_USER_DEFINED` | `MillUserDefinedBuilder` | 调用用户自定义后处理事件 |
| **文档** | `DOCUMENTATION` | `DocumentationBuilder` | 工序内嵌入文档 |

> 结论：**NX CAM 的操作类型远超 AP224 15 类特征**。CAPP 的 `operation_type` 不应只含 milling/drilling，而应对齐 NX 的 typeName 体系（见 4.2）。

> 注（NX2406 实证）：① 表中"对应 Builder"类的工厂（`CreateXxxBuilder(operation)`）均在
> `OperationCollection` 上，且 **Builder 与 typeName 并非一一对应**（如 `ZLEVEL_PROFILE` /
> `ZLEVEL_FOLLOW_PARTS` 共用 `CreateZlevelMillingBuilder`，注意工厂名小写 l、类名 `ZLevelMillingBuilder`）；
> ② typeName/subtypeName 语义 = 模板部件名/对象模板类型（2026-09-03 实证，见索引 §2.1），
> 完整配对表用 CAMSession 模板枚举获取；
> ③ 表中大类均有对应类型/工厂（含探测、机床控制、用户定义、文档类），覆盖广度可信。

### 3.4 Builder 参数面：铣削

Builder 继承链：`OperationBuilder → MillOperationBuilder → PlanarOperationBuilder / CavityMillingBuilder / ZLevelMillingBuilder / ...`

**OperationBuilder（所有操作通用基类）**：

| 参数 | 说明 |
| :--- | :--- |
| `Description` | 工序描述 |
| `Geometry` | 几何引用（组/面/边界） |
| `HoleAxisType` / `HoleDepth` | 孔轴类型 / 孔深（对孔类操作） |
| `RetractDistance` / `SafeClearance` | 退刀距离 / 安全间隙 |
| `StartOfPath` / `EndOfPath` | 路径起点/终点（如避让点） |
| `Notes` / `ChannelName` | 备注 / 输出通道名 |

**MillOperationBuilder（所有铣削通用）**：

| 参数 | 说明 |
| :--- | :--- |
| `CutParameters`（`MillCutParameters`） | 切削策略与余量 |
| `FeedsBuilder` | 进给/转速 |
| `ReferenceTool` | 参考刀具（用于残余加工） |
| `WallCleanupType` | 侧壁清根类型 |

**MillCutParameters（铣削切削参数面，与 CAPP strategy 直接对应；NX2406 已核对类型）**：

| 参数 | 说明 | NX2406 取值形态（实证） |
| :--- | :--- | :--- |
| `PartStock` / `FloorStock` / `WallStock` | 侧壁/底面/壁余量 (mm)。`PartStock` 在基类 `CutParameters` 上 | `InheritableDoubleBuilder` → `.Value` |
| `Stepover` | 步距。类型 `StepoverBuilder`，**无 `Percent` 属性** | `StepoverType`（如 `StepoverTypes.PercentToolFlat`）+ 对应子 Builder（`PercentToolFlatBuilder.Value`），链路待实测（附 B） |
| `CutOrder` | 切削顺序。**直接枚举赋值** | `CutOrder = CAM.CutParametersCutOrderTypes.LevelFirst`（取值 `LevelFirst`/`DepthFirst`/`DepthFirstAlways`，**无 `AreaFirst`**） |
| `CutDirection` | 顺逆铣。**类 + 嵌套枚举** | `CutDirection.Type = CAM.CutDirection.Types.Climb`（无 `Up`；逆铣=`Conventional`） |
| `FinishPasses` | 精加工刀数。类型 `FinishPassesBuilder` | `.NumberOfFinishPasses`（int 直赋）+ `.FinishStepoverBuilder` |
| `MultiDepthCut` | 多层切削。类型 `MultiDepthCut` | `.Toggle`（bool）+ `.StepMethod`（`Increment`/`Passes`）+ `.Increment` / `.NumberOfPasses` |
| `DepthPerCut` | 每刀深度 (mm)。**不在 `MillCutParameters` 上** | 铣削在 `PlanarOperationBuilder.DepthPerCut`，腔铣在 `CavityMillingBuilder.DepthPerCut`（均 `InheritableDoubleBuilder`，直接用 `builder.DepthPerCut.Value`） |
| `BoundaryInTol` / `BoundaryOutTol` | 边界内/外公差（注意属性名是 `BoundaryOutTol`，且为**直接 double**） | 直赋 0.01 |

**PlanarOperationBuilder（平面铣追加）**：`CutPattern`（类型 `CutPatternBuilder`，写法 `CutPattern = CutPatternBuilder.Types.FollowPart`；真实 36 值见附 A，无 HILBERT/PARALLEL_LINES）、`CutAreaGeometry`（切削区域）、`PartGeometry`（部件几何）、`NonCuttingBuilder`（返回 `NcmPlanarBuilder`，见 3.10）、`DepthPerCut`（直接成员）、`ToolAxisFix` 等。

**FeedsBuilder（进给/转速面）**：

| 参数 | 说明 |
| :--- | :--- |
| `SpindleRpmBuilder` | 主轴转速 (rpm) |
| `SurfaceSpeedBuilder` | 表面速度 (m/min)（与 rpm 二选一） |
| `SpindleModeBuilder` | 主轴模式（RPM / SFM / MMPM）。**2406 实测返回 `InheritableIntBuilder`（非枚举）**，模式以数值编码，映射待实测 |
| `FeedCutBuilder` | 切削进给 (mm/min 或 mm/rev) |
| `FeedApproachBuilder` / `FeedEngageBuilder` / `FeedDepartureBuilder` | 逼近/进刀/退刀进给 |
| `RetractSpeed` | 退刀速度 |
| `RecalculateData()` | 按公式重算进给数据 |

### 3.5 Builder 参数面：孔加工（NX2406 已核对）

继承链：`HoleDrillingBuilder → HoleMachiningBuilder → …`。孔加工是 CAPP 孔特征直连价值最高的域。

**HoleMachiningCutParameters（孔加工切削参数，NX2406 属性名与形态已核对）**：

| 属性 | 说明 | 取值形态 |
| :--- | :--- | :--- |
| `BottomStock` | 底部余量 (mm) | `InheritableDoubleBuilder` → `.Value` |
| `TopOffset` | 顶部偏置（从孔口/面起算）。2406 类型为 `VerticalPosition` | 类赋值 |
| `CornerControl` | 转角控制（如倒角停留）。类型 `CornerControlBuilder` | 子 Builder |
| `MinimalClearance` | 最小间隙 | **直接 double** |
| `BottomClearance` | 底部间隙（XML 注记 **"Created in NX2312.0.0"**，NX2312 新增成立） | `InheritableDoubleBuilder` → `.Value` |

**HoleDrillingBuilder（继承 HoleMachiningBuilder；NX2406 属性名与取值已核对）**：

| 属性 | 说明 | NX2406 实测 |
| :--- | :--- | :--- |
| `ControlPointOffset` | 控制点偏置方式 | 嵌套枚举 `ControlPointOffsetType`：`None\|Feature\|Initial`（文档旧描述"孔顶/孔底/自动"需与 UI 对账） |
| `RetractOutputMode` | 退刀输出模式 | 嵌套枚举 `RetractOutputModeType`：`ClearanceOnly\|ClearanceInitial\|Always` |
| `IntersectionStrategy` | 与已加工特征相交策略 | 嵌套枚举 `IntersectionStrategyType`：`None\|Part\|Ipw\|IpwAndPart` |
| `CrossOverDistance` | 越程距离 | `InheritableToolDepBuilder`（.NET 属性为 `CrossOverDistance`） |
| `ToolDrivePoint` | 刀具驱动点 | **方法对 `GetToolDrivePoint()/SetToolDrivePoint(string)`，参数为 String**（非枚举属性），取值集合待实测 |
| `CutParameters` / `CuttingParameters` | **两属性并存**：`CutParameters` → `HoleDrillingCutParameters`；`CuttingParameters` → `HoleMachiningCutParameters`（后者即文档所说"cuttingParameters"） | 只读属性 |

**HoleMachiningBuilder 完整参数面（NX2406）**：`CuttingParameters`（→`HoleMachiningCutParameters`）/ `PredefinedDepth`（类型 `DimensionRule`，配合 `HoleDepthType`、`HoleDepth`）/ `CycleTable`（类型 `CAM.Cycle`；**无 `Cycle` 属性**）/ `CollisionCheck`（bool 直赋）/ `NonCuttingBuilder`（孔加工为 `NcmHoleMachining`，非 `NcmPlanarBuilder`）/ `Geometry`（`GeometryCiBuilder`）/ `FeedsBuilder`。

### 3.6 Builder 参数面：车削 / 多轴 / WEDM / 探测（概要）

- **车削**：`RoughTurningBuilder`（粗车：切削区域 cut region、深度、进给分层）、`FinishTurningBuilder`（精车）、`ThreadTurningBuilder`（螺纹车削：螺距/牙型/多次进刀）、`CenterlineDrillTurningBuilder`（车床中心线钻孔）、`MultiAxisTurnMillingBuilder`（多轴车铣）。车削切削参数面为 `TurnCutParameters`（`TurCutRegion`、`Stock`、`Depth`、`FeedRate` 等）。
- **多轴**：`MultiAxisRoughingBuilder` / `MultiAxisWallFinishingBuilder` / `MultiAxisDeburringBuilder`，参数面含刀具轴（`ToolAxis`）、驱动方式（`DriveMode`）、切削深度等。
- **WEDM**：`WedmOperationBuilder`（线切割：轮廓、锥度、多次切割、张丝参数）。
- **探测**：`OnMachineProbingBuilder`（探测点/矢量/触测速度/保护移动）、`MillToolProbingBuilder`（刀具长度/直径测量）。

### 3.7 几何组与刀具 Builder

**组创建（NCGroupCollection；NX2406 签名：须传父组）**：

| 方法 | 说明 |
| :--- | :--- |
| `CreateProgram(parent, typeName, subtypeName, useDefaultName, newName)` | 程序组；父组 = `GetRoot(CAMSetup.View.ProgramOrder)`（顶层）或其子组 |
| `CreateMethod(parent, typeName, subtypeName, useDefaultName, newName)` | 方法组；父组 = `GetRoot(CAMSetup.View.MachineMethod)` |
| `CreateTool(parent, typeName, subtypeName, useDefaultName, newName)` | 刀具组；父组 = `GetRoot(CAMSetup.View.MachineTool)` |
| `CreateGeometry(parent, typeName, subtypeName, useDefaultName, newName)` | 几何组（MCS/WORKPIECE…）；父组 = `GetRoot(CAMSetup.View.Geometry)` |

> NX2406 注：`typeName/subtypeName` 为组模板类型串（运行时验证，附 B）；`useDefaultName` 是
> `NCGroupCollection.UseDefaultName` 枚举。全部组创建均 cam_base 许可。

**组级 Builder（设组参数，操作未显式设置时继承）**：

| Builder | 关键参数 |
| :--- | :--- |
| `MillGeomBuilder` | part / blank / checkGeometry（部件/毛坯/检查几何） |
| `MillBoundaryGeomBuilder` | part / blank / check / trimBoundary（部件边界/毛坯/检查/修剪边界） |
| `MillAreaGeomBuilder` | 切削区域（面集） |
| `MillVolumeGeomBuilder` | 切削体积 |
| `DrillGeomBuilder` | topSurface / bottomSurface / toolAxis（顶面/底面/刀轴） |
| `FeatureBasedGeomBuilder` | 特征几何（AFR 特征集） |
| `MillOrientGeomBuilder` / `TurnOrientGeomBuilder` | **MCS 装夹坐标系**（见 3.8） |
| `MillMethodBuilder` / `TurnMethodBuilder` / `DrillMethodBuilder` | 方法组默认参数（余量/公差/进给继承源） |
| `MillToolBuilder` / `DrillStdToolBuilder` 等 | 刀具参数（见下表） |
| `MachineGroupBuilder` | 机床 + 控制器定义 |

**刀具 Builder 参数面（与 CAPP 刀具选型直接对应）**：

| 参数 | 所属 Builder | 说明 |
| :--- | :--- | :--- |
| `tlDiameterBuilder` | MillingToolBuilder | 刀具直径 (mm) |
| `tlHeightBuilder` | MillingToolBuilder | 刀具总长 |
| `tlFluteLnBuilder` | MillingToolBuilder | 刃长 |
| `tlNumFlutesBuilder` | MillingToolBuilder | 刃数 |
| `tlTaperAngBuilder` | MillingToolBuilder | 锥角 |
| `tlLowCorRadBuilder` | MillingToolBuilder | 底部圆角半径 |
| `tlUpCorRadBuilder` | MillingToolBuilder | 顶部圆角半径 |
| `tlShankDiaBuilder` | MillingToolBuilder | 柄径 |
| `tlZMountBuilder` / `tlZOffsetBuilder` | MillingToolBuilder | Z 向安装偏置 |
| `shankSectionBuilder`（刀柄段） | MillingToolBuilder | **NX2406 无 `holderSectionBuilder` 属性**（夹持段见 `TlHolder*` 字符串/库引用 + `ShankSectionBuilder`） |
| `coolantThrough` | MillingToolBuilder / DrillToolBuilder | 中心出水（2406 为 bool 属性 `CoolantThrough`） |
| `CutterSubtype`（属性） | MillToolBuilder | 2406 嵌套枚举 `CutterSubtypes`：`Mill5`/`Mill7`/`Mill10`/`MillBall`/`ChamferTool`/`SphericalMill`/`DovetailMill`（T 型刀→`DovetailMill`）；无 `setCutterSubtype` 方法 |
| `tlCor1RadBuilder` / `tlCor2RadBuilder` | MillToolBuilder | 下/上切削刃半径 |
| `tlTipAngBuilder` | MillToolBuilder | 刀尖角（倒角刀/燕尾刀） |
| `chamferLengthBuilder` | MillToolBuilder | 倒角长度 |
| `tlPointAngBuilder` | DrillToolBuilder | 钻头顶角（118°/135°） |
| `tlCor1RadBuilder` | DrillToolBuilder | 钻尖横刃/转角半径 |
| `tlPilotDiaBuilder` / `tlPilotLengthBuilder` | DrillToolBuilder | 导向径 / 导向长 |
| `tlPitchBuilder` | DrillToolBuilder / DrillTapToolBuilder | 螺距（丝锥/螺纹铣刀） |
| `tlTipDiameterBuilder` / `tlTipLengthBuilder` | DrillToolBuilder | 钻尖直径 / 钻尖长度 |
| `tlIncludedAngBuilder` | DrillToolBuilder | 沉孔锥角（锪钻） |
| `tlDesignation` | DrillToolBuilder | 刀具 ISO 编号 |
| `tlCoolantThro` / `tlToleranceClass` | DrillToolBuilder | 中心出水 / 公差等级 |
| `DrillStdToolBuilder` | — | 空实现，参数继承 MillingToolBuilder + DrillToolBuilder |

> 说明：`DrillStdToolBuilder` 本身不暴露独立参数，钻头/丝锥/铰刀/锪钻等标准刀具参数由 `MillingToolBuilder`（几何通用部分）+ `DrillToolBuilder`（钻头特有部分）共同提供；另有 `DrillTapToolBuilder` / `DrillReamerToolBuilder` / `DrillThreadMillToolBuilder` / `DrillBoreToolBuilder` / `DrillCtskToolBuilder` / `DrillCounterboreToolBuilder` / `FormToolBuilder` / `GenericToolBuilder` 等类型化 Builder。

> NX2406 注：上表属性名在 .NET 中与 C++ accessor 同名（`TlDiameterBuilder`、`TlFluteLnBuilder`、
> `TlPitchBuilder`…），多返回 `InheritableDoubleBuilder`/`InheritableIntBuilder`（.Value 写入）；
> `TlDesignation`/`TlToleranceClass` 为 string。对刀点（track point）在 2406 `MillingToolBuilder`
> 反射中未露面（原表 `millingTrackpointBuilder` 行已删），使用前需实测。

### 3.8 MCS 与装夹（OrientGeomBuilder；NX2406 实证：属性形态为主）

`MillOrientGeomBuilder`（铣）/ `TurnOrientGeomBuilder`（车）负责装夹坐标系与安全几何。
**NX2406 上 `Mcs`/`Rcs`/`ToolAxisVector`/`LowerLimitPlane` 等均为可写属性，没有 `setMcs`/`setRcs` 等 set 方法**：

| 属性 / 方法 | 说明 |
| :--- | :--- |
| `Mcs`（可写属性，类型 `CartesianCoordinateSystem`） | 加工坐标系（原点 + Z 轴 + X 轴） |
| `Rcs`（可写属性） | 参考坐标系 |
| `LinkRcsToMcs`（bool） | RCS 跟随 MCS 联动 |
| `ToolAxisVector`（可写属性，`NXObject`）+ `ToolAxisFilterBuilder` | 刀轴矢量（2406 无 `getToolAxisMode/setToolAxisMode`/`toolAxisVector()` 方法形态） |
| `McsLocationMode`（`OrientGeomBuilder.McsLocationModes`）+ `McsWorkpiece` | MCS 定位模式（`Specify`/`OnBlank`/`OnPartBox`）/ 工件关联（2406 无 `setBlockMcsOrigin`/`setCylinderMcsOrigin`） |
| `FixtureOffsetBuilder` | 夹具偏置号（G54/G55…，2406 返回 `InheritableIntBuilder`，int 写入） |
| `TransferClearanceBuilder` | 安全平面（`NcmClearanceBuilder`，见 3.10） |
| `TransferAvoidanceFromBuilder` / `TransferAvoidanceStartBuilder` / `TransferAvoidanceReturnBuilder` / `TransferAvoidanceGohomeBuilder` | FROM / START / RETURN / GOHOME 避让点（`NcmAvoidancePointBuilder`） |
| `GetLowerLimitMode()`/`SetLowerLimitMode()` + `LowerLimitPlane` | 最低加工限制 |

### 3.9 几何选择（GeometryCiBuilder + HoleBossGeom）

**GeometryCiBuilder（几何选择器，cam_base）**：

| 参数 | 说明 |
| :--- | :--- |
| `setAutoWallSelection` | 自动侧壁选择（腔体类工序） |
| `setFloorPlane` | 指定底面平面 |
| `holeBossGeom()` | 孔/凸台几何定义（返回 `HoleBossGeom`） |
| `cutVolumeGeom()` | 切削体积几何 |
| `blade` / `hub` / `shroud` / `splitters` / `bladeBlendGeometry` | 叶片/轮毂/轮缘/分流/叶根过渡几何（叶轮加工） |
| `partSpunOutline` / `blankSpunOutline` | 车削零件/毛坯轮廓 |

**HoleBossGeom（孔/凸台特征几何，与 CAPP 孔特征参数一一对应）**：

| 方法 / 属性（.NET 名） | 说明 |
| :--- | :--- |
| `CreateHoleBossBuilder(…)` → `HoleBossSet` | 定义孔（面集 + 直径 + 深度 + 刀轴 + 深度限制） |
| `CreateThreadedHoleBuilder(…)` → `ThreadedHoleSet` | 定义螺纹孔（tapDrillSize / depth / majorDiameter / minorDiameter / size / pitch / rotation / form / tableStandard） |
| `CreateThreadedBossBuilder(…)` → `ThreadedBossSet` | 定义螺纹凸台 |
| `HoleList` / `BossList` / `ThreadedHoleList` / `ThreadedBossList`（只读属性） | 批量孔/凸台列表 |
| `GetCenterHoleGeometry()` / `GetChamferHoleGeometry()` → `FBM.MachiningFeatureGeometry` | 中心孔 / 倒角孔几何 |

> NX2406 注：`HoleBossGeomType/SetHoleBossGeomType`、`SetFormAndPitch/SetPitch/SetRotation`、
> `SetOptimization` 在本类反射中未露面（旧版口径），落地前勿依赖。

### 3.10 非切削移动与安全平面（NcmPlanarBuilder）

`NcmPlanarBuilder`（平面铣非切削移动，通过 `PlanarOperationBuilder.NonCuttingBuilder` 获取）是 CAPP 非切削策略的落点。**2406 成员均为 .NET 属性（只读返回子 Builder/值类型），无 C++ 风格 `xxxBuilder()` 方法**：

**嵌套枚举（NX2406 实证）**：`CutcomTypes`（刀具补偿）、`PredrillPointsOutputOptions`（预钻孔点输出）、`TransferBetweenLevelsTypes` / `TransferBetweenRegionsTypes` / `TransferWithinLevelsTypes` / `TransferWithinLevelsWiths`（层间/区域间/层内转移；取值见 nx2406-install-index.md §2.3）。进刀/退刀的具体策略枚举位于 `NcmPlanarEngRetBuilder` 内（未逐项核对）。

| 成员（.NET 属性） | 返回类型 | 说明 |
| :--- | :--- | :--- |
| `ClearanceBuilder` | `NcmClearanceBuilder` | 安全平面/点（见下） |
| `EngageClosedAreaBuilder` / `EngageOpenAreaBuilder` / `EngageInitialClosedBuilder` / `EngageInitialOpenBuilder` | `NcmPlanarEngRetBuilder` | 进刀策略（封闭区/开放区/初始） |
| `RetractAreaBuilder` / `RetractFinalBuilder` | `NcmPlanarEngRetBuilder` | 退刀策略 |
| `TransferAvoidanceFromBuilder` / `TransferAvoidanceStartBuilder` / `TransferAvoidanceReturnBuilder` / `TransferAvoidanceGohomeBuilder` | `NcmAvoidancePointBuilder` | FROM/START/RETURN/GOHOME 避让点 |
| `SmoothingBuilder` | `NcmSmoothingBuilder` | 拐角光顺 |
| `CollisionCheck` | bool | 碰撞检查开关 |
| `CutcomType`（+ `CutcomMinimumMoveBuilder` 等） | 嵌套枚举 | 刀具补偿（无 `suppressCutcom`） |
| `InitialSafeDistanceBuilder` / `FinalSafeDistanceBuilder` | `InheritableToolDepBuilder` | 初始/最终安全距离 |
| `TransferWithinLevelsHeightBuilder` / `TransferBetweenLevelsSafeDistanceBuilder` | `InheritableToolDepBuilder` | 层内抬刀高度 / 层间安全距离 |
| `PredrillPointsOutput`（+ `PredrillPointsUseEffectDist`/`PredrillPointsEffectDistBuilder`） | 嵌套枚举 | 预钻孔点输出（**无 `predrillPoints(Point[])` 方法**） |

**NcmClearanceBuilder（安全平面；NX2406 实证：属性 + 直接赋值形态）**：

| 属性 | 说明 |
| :--- | :--- |
| `ClearanceType`（直接枚举 `ClearanceTypes`） | `UseCommon`/`None`/`Automatic`/`Plane`/`Point`/`Cylinder`/`Sphere`/`BoundingBox`/`BoundingCylinder`/`Body`/`MachineBased` |
| `SafeDistance` | 安全距离 (mm)，**直接 double 赋值** |
| `PlaneXform`（`NXOpen.Plane`） | 安全平面定义 |
| `PointObject`（`NXOpen.Point`） | 安全点 |
| `AxisObject`（`NXOpen.Direction`） | 圆柱/旋转轴 |
| `Radius` | 圆柱/球半径，**直接 double 赋值** |
| `Body`（`NXObject`） | 体安全几何 |
| `BoundingBoxClearance` | 包围盒安全几何，**直接 double 赋值** |

### 3.11 刀路生成与后处理（CAMSetup / Operation）

| API（.NET 名） | 说明 |
| :--- | :--- |
| `Operation.CreateToolPathEditorBuilder()` | 打开刀路编辑器（查看/编辑刀位点） |
| `Operation.GenerateIpw(bool, bool)` | 生成 IPW（中间工件，供残余加工） |
| `Operation.GetToolpathTime()` / `GetToolpathLength()` → double | 刀路时间 / 长度 |
| ~~`getCuttingTime` / `getCuttingLength` / `gougeCheck`~~ | **NX2406 `Operation` 上不存在**：切削时间统计可另算或用 `CAMSetup.CalculateMachiningTimes`；过切检查见 setup 级两行 |
| `Operation.GougeCheckStatus` / `GougeCheckResults`（只读） | 过切结果回读（`ResetGougeChecking()` 复位） |
| `Operation.InsertFeature()` / `RemoveFeature()` | 增删关联加工特征（`InsertFeature` 返回 `CAM.FBM.Feature`） |
| `Operation.GetParent(CAMSetup.View)`；四父链 `ParentProgramOrder/ParentMachineTool/ParentGeometry/ParentMachineMethod` | 取操作在各视图下的父组 |
| `CAMSetup.GenerateToolPath(CAMObject[])` | 生成选中操作的刀路（批处理） |
| `CAMSetup.ParallelGenerate(CAMObject[])` | 并行生成（多核） |
| `CAMSetup.OutputClsf(CAMObject[], …)` | 输出 CLSF（刀位源文件） |
| `CAMSetup.Postprocess(...)` / `PostprocessWithPostprocessor(...)` | 后处理为 NC 代码 |
| `CAMSetup.CalculateMachiningTimes()` | 统计加工时间 |
| `CAMSetup.GougeCheck(CAMObject[], bool)` / `CreateGougeCheckBuilder(CAMObject[])` | 批量过切检查 / 过切检查 Builder |
| `CAMSetup.DeleteMachineCode()` | 删除已生成 NC |
| `CAMSetup.CreateWorkInstructionBuilder(CAMObject)` | 生成作业指导书（工单卡片） |
| `CAMSetup.CreatePostProcessManagerBuilder()` | 后处理管理器 |

> 这意味着 CAPP 关心的**周期时间估算、刀路长度、过切风险**都可直接从 NX 拿真实数据回传，形成"Plan → NX → 实际参数 → 反馈校准"闭环。

### 3.12 特征驱动加工（NX 侧的特征→工序）

NX CAM 不仅接受"手动选几何建工序"，还支持**特征驱动**链路，与 Autocam 的"特征识别 → CAPP"范式同构：

- `FeatureRecognitionBuilder`：程序化触发特征识别（AFR 的编程接口，识别孔/槽/腔体/台阶/凸台/倒角/圆角）。
- `CAMSetup.CreateFeatureProcessBuilder(...)`（需 `ug_holemaking` 许可）：按特征自动生成加工工序（feature process）。
- `Operation.InsertFeature()` / `RemoveFeature()`：把 CAM 特征挂到已有工序上，实现"同一工序加工一组同参数特征"（对应 CAPP 的 merge_group 合并逻辑）。

> 结论：NX 既能**按参数手工建工序**（本文 3.2-3.10 的路径），也能**按特征自动生成**。CAPP Plan 走前者更可控、参数完整；后者可作为"Plan 缺参数时的兜底/NX 原生重规划"。

---

## 4. CAPP Plan 输出对接建议（核心交付）

### 4.1 总体映射架构

原则：**Plan 是意图层（工艺决策），NX Builder 是参数层（CAM 执行）**。Plan 字段设计要以"能否无歧义映射到 Builder 参数"为准，而不是以 AP224 特征分类为准。

```
CAPP Plan (JSON, autocam-plan.schema.json)
  └─▶ NXPlugin Adapter（Mapper）
       ├─ 1:1 直填：tool 直径/刃长/螺距/刃数、深度、rpm/进给、余量
       ├─ 枚举映射：cut_pattern / cut_order / cut_direction / cycle → NX 枚举
       ├─ 派生计算：stepover% → StepoverBuilder（StepoverType=PercentToolFlat + PercentToolFlatBuilder.Value，链路待实测）；安全平面 → ClearanceType=Plane + SafeDistance
       └─ 几何解析：geometry_ref（face_anchors 属性快照 / 面/边/孔心）→ NX Face/Edge/Point（面映射算法见本文件附 A 注记与 nx2406-install-index.md §2）
```

映射分层：

| 层级 | Plan 来源 | NX 落点 | 方式 |
| :--- | :--- | :--- | :--- |
| 操作类型 | `operation_type` + `nx_template` | `OperationCollection.Create(typeName, subtypeName)` | 枚举直填 |
| 四类组 | `setups[]` / `workingsteps[]` / `resources.tools[]` / method | Program / Geometry / Tool / Method 组 | 层级映射（4.8） |
| 策略 | `operation.strategy` | 对应 Builder 的 CutParameters / NonCutting / Cycle | 直填+映射 |
| 技术参数 | `operation.technology` | `FeedsBuilder` / 冷却 / 公差 | 直填 |
| 刀具 | `resources.tools[]` | 刀具组 Builder | 直填 |
| 几何 | `feature.geometry_ref` | GeometryCiBuilder / HoleBossGeom / 边界 | 面映射 |
| 装夹 | `setup` | MillOrientGeomBuilder（MCS/安全平面/避让点） | 直填+派生 |

### 4.2 operation_type 建议枚举（对齐 NX 大类）

当前 schema 中 `operation_type` 只有 `milling / drilling / other`，粒度不足以映射 NX。建议扩展为**分层枚举 + nx_template 字段**：

```json
{
  "operation_id": "OP-001",
  "operation_type": "mill_cavity",
  "nx_template": { "type": "CAVITY_MILL", "subtype": "" },
  "tool_ref": "T-001",
  "strategy": { "...": "..." },
  "technology": { "...": "..." }
}
```

**建议 operation_type 枚举（覆盖 NX 全大类）**：

| 大类 | 建议枚举值（operation_type） | 对应 nx_template typeName |
| :--- | :--- | :--- |
| 铣削 2.5 轴 | `mill_cavity` / `mill_planar` / `mill_face` / `mill_plunge` / `mill_groove` | `CAVITY_MILL` / `PLANAR_MILL` / `FACE_MILLING` / `PLUNGE_MILL` / `GROOVE_MILL` |
| 铣削 3 轴 | `mill_zlevel` / `mill_surface` / `mill_flowcut` / `mill_chamfer` / `mill_engrave` / `mill_cylinder` | `ZLEVEL_PROFILE` / `SURFACE_CONTOUR` / `FLOWCUT` / `CHAMFER_MILL` / `ENGRAVE` / `CYLINDER_MILL` |
| 孔加工 | `drill_center` / `drill` / `drill_peck` / `drill_break_chip` / `tap` / `thread_mill` / `ream` / `bore` / `counterbore` / `countersink` | `SPOT_DRILLING` / `DRILL` / `PECK_DRILLING` / `BREAK_CHIP_DRILLING` / `TAPPING` / `THREAD_MILLING` / `REAMING` / `BORING` / `COUNTERBORE` / `COUNTERSINK` |
| 车削 | `turn_rough` / `turn_finish` / `turn_thread` / `turn_drill` / `turn_mill` | `ROUGH_TURNING` / `FINISH_TURNING` / `THREAD_TURNING` / `CENTERLINE_DRILL_TURNING` / `MULTI_AXIS_TURN_MILL` |
| 多轴 | `multi_axis_rough` / `multi_axis_wall_finish` / `multi_axis_deburr` | `MULTI_AXIS_ROUGHING` / `MULTI_AXIS_WALL_FINISHING` / `MULTI_AXIS_DEBURRING` |
| 其他 | `wedm` / `additive_planar` / `additive_rotary` / `probe` / `machine_control` / `user_defined` | `WEDM_OPERATION` / `PLANAR_ADDITIVE_DEPOSIT` / `ROTARY_ADDITIVE_DEPOSIT` / `ON_MACHINE_PROBING` / `MILL_MACHINE_CONTROL` / `MILL_USER_DEFINED` |

> 保留 `feature_type`（AP224 15 类 + `geometry_group`）作为**特征层分类**不变；`operation_type` 升格为**工序层分类**，两者通过 workingstep 关联。特征→工序映射规则见 [autocam-plan.schema.json](../schema/autocam-plan.schema.json) workingstep/feature 注记（原映射表属外部模块，未挂载）。

### 4.3 strategy 结构化字段（按 NX Builder 参数面反推）

所有字段都能在 3.4-3.10 找到直接落点：

| Plan.strategy 字段 | 类型 | NX Builder 落点（NX2406 实证，属性形态见附 A） | 必填 |
| :--- | :--- | :--- | :--- |
| `cut_pattern` | 枚举 | `PlanarOperationBuilder.CutPattern`（`CutPatternBuilder`，`CutPattern = CutPatternBuilder.Types.…`；真实值含 FollowPart/FollowPeriphery/Zig/ZigZag/Profile 等 36 个，**无 HILBERT/PARALLEL_LINES**） | 粗铣必填 |
| `cut_order` | 枚举 | `MillCutParameters.CutOrder` = **`CutParametersCutOrderTypes`**（`LevelFirst`/`DepthFirst`/`DepthFirstAlways`，无 AreaFirst；直接枚举赋值） | 建议 |
| `cut_direction` | 枚举 | `MillCutParameters.CutDirection.Type` = `CutDirection.Types`（`Climb`/`Conventional`/`Forward`/`Reverse`/`Mixed`；无 Up） | 建议 |
| `depth_per_cut` | number(mm) | `PlanarOperationBuilder.DepthPerCut.Value` / `CavityMillingBuilder.DepthPerCut.Value`（**不在 MillCutParameters 上**） | 粗铣必填 |
| `stepover` | number | `MillCutParameters.Stepover`（`StepoverBuilder`：`StepoverType` + 对应子 Builder，无 Percent；`50% 刀径` → `StepoverTypes.PercentToolFlat` + `PercentToolFlatBuilder.Value`，链路待实测） | 粗铣必填 |
| `finish_passes` | int | `MillCutParameters.FinishPasses.NumberOfFinishPasses`（int 直赋） | 建议 |
| `multi_depth_cut` | bool | `MillCutParameters.MultiDepthCut.Toggle`（bool）+ `StepMethod`/`Increment`/`NumberOfPasses` | 粗铣建议 |
| `part_stock` / `floor_stock` / `wall_stock` | number(mm) | `MillCutParameters.PartStock/FloorStock/WallStock`（`InheritableDoubleBuilder`→.Value；PartStock 在基类 `CutParameters`） | 建议 |
| `wall_cleanup` | 枚举 | `MillOperationBuilder.WallCleanupType`（嵌套枚举 `WallCleanupTypes`：`None`/`AtStart`/`AtEnd`/`Automatic`） | 可选 |
| `tool_axis` | vector | 铣 `MillOrientGeomBuilder.ToolAxisVector`（rw 属性）；孔加工刀轴通常由 MCS/孔几何决定（2406 未逐项核对） | 建议 |
| `cycle` | 枚举 | **2406 无 `Cycle` 属性**：钻孔循环在 `HoleDrillingBuilder.CycleTable`（类型 `CAM.Cycle`） | 钻孔必填 |
| `depth` / `depth_limit` | number/枚举 | `HoleMachiningBuilder.PredefinedDepth`（类型 `DimensionRule`）+ `HoleDepthType`/`HoleDepth` | 钻孔必填 |
| `bottom_stock` / `bottom_clearance` | number(mm) | `HoleMachiningCutParameters.BottomStock / BottomClearance`（`InheritableDoubleBuilder`→.Value） | 建议 |
| `top_offset` | number(mm) | `HoleMachiningCutParameters.TopOffset`（类型 `VerticalPosition`） | 可选 |
| `control_point_offset` | 枚举 | `HoleDrillingBuilder.ControlPointOffset`（`ControlPointOffsetType`：`None`/`Feature`/`Initial`；与 UI 术语对账） | 可选 |
| `retract_output_mode` | 枚举 | `HoleDrillingBuilder.RetractOutputMode`（`RetractOutputModeType`：`ClearanceOnly`/`ClearanceInitial`/`Always`） | 可选 |
| `tool_drive_point` | 枚举 | **方法对** `HoleDrillingBuilder.GetToolDrivePoint()/SetToolDrivePoint(string)`（取值集合待实测） | 可选 |
| `cross_over_distance` | number(mm) | `HoleDrillingBuilder.CrossOverDistance`（`InheritableToolDepBuilder`） | 可选 |
| `turn_cut_region` | 枚举 | `RoughTurningBuilder` 切削区域（未逐项核对，落地前实测） | 车削必填 |
| `non_cutting.approach` / `engage` / `retract` / `final` | 对象 | `NcmPlanarBuilder` EngageClosed/EngageOpen/RetractArea/RetractFinal（`NcmPlanarEngRetBuilder`：策略+距离+角度） | 可选 |
| `non_cutting.clearance` | 对象 | `NcmClearanceBuilder`：`{ type: "plane"|"point"|"cylinder"|"sphere"|"box"|"body", safe_distance, origin, normal, radius }`（ClearanceType 枚举直赋、SafeDistance 为 double） | 建议必填 |
| `non_cutting.from/start/return/gohome` | 点对象 | `NcmPlanarBuilder.TransferAvoidanceFrom/Start/Return/GohomeBuilder`（`NcmAvoidancePointBuilder`） | 可选 |
| `non_cutting.transfer_within_levels_height` | number | `NcmPlanarBuilder.TransferWithinLevelsHeightBuilder`（`InheritableToolDepBuilder`） | 可选 |
| `non_cutting.predrill_points` | 点数组 | 2406 无 Point[] 入参方法：`PredrillPointsOutput`（`PredrillPointsOutputOptions`）+ 效应距离；JSON 侧先按输出选项设计 | 可选 |

**strategy JSON 示例（铣腔体）**：

```json
{
  "strategy": {
    "cut_pattern": "FOLLOW_PART",
    "cut_order": "LEVEL_FIRST",
    "cut_direction": "CLIMB",
    "depth_per_cut": 2.0,
    "stepover": { "mode": "percent", "value": 50 },
    "finish_passes": 1,
    "multi_depth_cut": true,
    "floor_stock": 0.3,
    "wall_stock": 0.3,
    "non_cutting": {
      "clearance": { "type": "plane", "safe_distance": 10.0, "origin": [0, 0, 50], "normal": [0, 0, 1] },
      "engage": { "type": "HELICAL", "diameter_percent": 50, "angle": 30, "distance": 5.0 },
      "retract": { "type": "CLEARANCE_PLANE" }
    }
  }
}
```

**strategy JSON 示例（钻孔）**：

```json
{
  "strategy": {
    "cycle": "PECK",
    "depth": { "mode": "through", "value": 25.0 },
    "bottom_stock": 0.0,
    "bottom_clearance": 2.0,
    "control_point_offset": "TOP",
    "retract_output_mode": "CLEARANCE_PLANE",
    "tool_drive_point": "TIP"
  }
}
```

### 4.4 technology 结构化字段

| Plan.technology 字段 | NX Builder 落点 | 说明 |
| :--- | :--- | :--- |
| `spindle_rpm` | `FeedsBuilder.SpindleRpmBuilder.Value` | rpm 数值 |
| `surface_speed` | `FeedsBuilder.SurfaceSpeedBuilder.Value` | m/min，与 rpm 二选一 |
| `spindle_mode` | `FeedsBuilder.SpindleModeBuilder` | RPM / SFM / MMPM（2406 返回 `InheritableIntBuilder`，数值编码映射待实测） |
| `feed_cut` | `FeedsBuilder.FeedCutBuilder.Value` | 切削进给（mm/min 或 mm/rev，由单位字段决定） |
| `feed_approach` / `feed_engage` / `feed_departure` | `FeedApproachBuilder` / `FeedEngageBuilder` / `FeedDepartureBuilder` | 细分进给 |
| `retract_speed` | `FeedsBuilder.RetractSpeed` | 退刀速度 |
| `coolant` | 操作/刀具 coolant 参数（含 `coolantThrough`） | 冷却类型枚举（on/mist/flood/through） |
| `tolerance.intol` / `tolerance.outtol` | `MillCutParameters.BoundaryInTol` / `BoundaryOutTol`（注意属性名；**直接 double 直赋**，非 builder） | 轮廓内外公差 |
| `minimal_clearance` | `HoleMachiningCutParameters.MinimalClearance`（**直接 double**） | 孔加工最小间隙 |

**technology JSON 示例**：

```json
{
  "technology": {
    "spindle_mode": "RPM",
    "spindle_rpm": 6000,
    "feed_cut": { "value": 1200, "unit": "MMPM" },
    "retract_speed": 3000,
    "coolant": "FLOOD",
    "tolerance": { "intol": 0.01, "outtol": 0.01 }
  }
}
```

### 4.5 tool 完整几何参数（resources.tools[]）

| Plan 字段 | NX Builder 落点 | 说明 |
| :--- | :--- | :--- |
| `type` | `MillToolBuilder.CutterSubtype`（嵌套枚举 `CutterSubtypes`：`Mill5`/`Mill7`/`Mill10`/`MillBall`/`ChamferTool`/`SphericalMill`/`DovetailMill`）或钻孔子类型 | 平底/球头/牛鼻/T 型(Dovetail)/倒角/钻头/丝锥… |
| `diameter` | `tlDiameterBuilder.Value` | 刀具直径 |
| `height` | `tlHeightBuilder.Value` | 总长 |
| `flute_length` | `tlFluteLnBuilder.Value` | 刃长 |
| `num_flutes` | `tlNumFlutesBuilder.Value` | 刃数 |
| `taper_angle` | `tlTaperAngBuilder.Value` | 锥角 |
| `lower_corner_radius` | `tlLowCorRadBuilder` / `tlCor1RadBuilder` | 底部圆角（牛鼻刀 R） |
| `upper_corner_radius` | `tlUpCorRadBuilder` / `tlCor2RadBuilder` | 顶部圆角 |
| `shank_diameter` | `tlShankDiaBuilder.Value` | 柄径 |
| `tip_angle` | `DrillToolBuilder.tlPointAngBuilder` | 钻头顶角 |
| `point_diameter` | `DrillToolBuilder.tlTipDiameterBuilder` | 钻尖直径 |
| `point_length` | `DrillToolBuilder.tlTipLengthBuilder` | 钻尖长度 |
| `pilot_diameter` / `pilot_length` | `tlPilotDiaBuilder` / `tlPilotLengthBuilder` | 导向径/长 |
| `pitch` | `tlPitchBuilder`（DrillTap/DrillThreadMill） | 螺距 |
| `included_angle` | `tlIncludedAngBuilder` | 沉孔锥角 |
| `designation` | `DrillToolBuilder.tlDesignation` | ISO 编号（如 `DIN 338`） |
| `tolerance_class` | `tlToleranceClass` | 公差等级 |
| `holder` | `ShankSectionBuilder`（2406 `MillingToolBuilder` 无 `holderSectionBuilder`） | 夹持段参数 |
| `coolant_through` | `coolantThrough` | 中心出水 |
| `track_point` | 2406 `MillingToolBuilder` 反射未暴露（原 `millingTrackpointBuilder` 名失效），落地前实测 | 对刀点 |
| `material` / `coating` | 刀具材料/涂层参数 | 建议输出 |

> 刀具是所有参数中最"直填"的部分——CAPP 刀具选型结果（直径/刃数/圆角/螺距）与 NX 刀具 Builder 字段基本一一对应，不需要派生计算。

### 4.6 geometry_ref 稳定几何锚点

对齐 schema v3 与 [nx-plugin-design.md](./nx-plugin-design.md) 的需求：`geometry_ref`
**直接引用原始 STEP 文件中的 B-Rep 信息**（OCCT 遍历 ID + 锚点），不做按特征类型的派生结构：

```
geometry_ref: {
  face_ids:     string[]   // OCCT 遍历 ID（原始 STEP B-Rep 面，来自云端 geometry.json faces[]）
  edge_ids:     string[]   // OCCT 遍历 ID（原始 STEP B-Rep 边，来自云端 geometry.json edges[]）
  anchor_point: [x, y, z]  // 特征位置（孔心/质心），mm，模型局部坐标；无 face 映射能力时兜底
}
```

- `face_ids` / `edge_ids` 与 STEP 导入件一一对应；NX 侧经 FaceResolver 属性匹配解析成 NX Tag
- `anchor_point` 为兜底锚点：测试 Journal 等无 face 映射能力的消费端据此近似定位
- 特征参数（直径/深度/螺距…）走 `feature.params`，不重复进 `geometry_ref`

**几何解析链路**：当前 NX 闭环（面属性快照路径）中，Plan 的 `face_anchors`（质心+面积+类型+法向）由 NX 侧 **FaceResolver** 按属性匹配解析成 NX Tag（容差 0.01mm，命中不唯一写 `GEOM_AMBIGUOUS_MATCH` diagnostic）；云端 OCCT 路径的 `face_id / edge_id` 为预留表示，接入时走同一属性匹配；`anchor_point` 为无映射能力时的近似兜底。这是整个对接中唯一需要算法的环节（算法规格随 PlanComparer 首版校准后固化）。

### 4.7 setup 补 MCS 与机床

| Plan.setup 字段 | NX Builder 落点 | 建议 |
| :--- | :--- | :--- |
| `mcs.origin` / `mcs.z_axis` / `mcs.x_axis` | `MillOrientGeomBuilder.Mcs`（2406 为**可写属性**，赋值 `CartesianCoordinateSystem`） | 每个 setup 必填 |
| `fixture_offset` | `MillOrientGeomBuilder.FixtureOffsetBuilder`（`InheritableIntBuilder`，int 写入） | G54=1…，建议必填 |
| `safe_plane` | `transferClearanceBuilder`（复用 4.3 的 clearance 结构） | 建议必填 |
| `from_point` / `start_point` / `return_point` / `gohome_point` | `transferAvoidanceFromBuilder` 等 | 可选 |
| `lower_limit` | `setLowerLimitMode` / `LowerLimitPlane` | 可选 |
| `blank_ref` | `MillGeomBuilder.Blank` | 首道工序前建议给出 |
| `machine_ref` | `MachineGroupBuilder`（机床型号+控制器） | 建议 |

### 4.8 层级映射：workingsteps → Program 组树

```
Plan.workplan(root, elements)
├── setup_1 ──▶ Program 组 "PROGRAM_1"（Geometry 组 "MCS_1"）
│   └── workingstep(WS-01, feature_ref=F-01, operation_ref=OP-001) ──▶ Operation "CAVITY_1"
│   └── workingstep(WS-02, ...) ──▶ Operation "DRILL_1"
└── setup_2 ──▶ Program 组 "PROGRAM_2"（Geometry 组 "MCS_2"）
    └── ...
```

| Plan 元素 | NX 落点 | 说明 |
| :--- | :--- | :--- |
| `setup` | Program 组 + Geometry 组（MCS） | 一个 setup 对应一个装夹方向 |
| `workingstep` | Operation | 挂在四个父组下 |
| `operation_ref` | `Create(typeName)` | 操作创建 |
| `method`（粗/半精/精） | Method 组 | 由 `operation_type` 或显式 `method_ref` 决定 |
| `tool_ref` | Tool 组 | 每个刀具建一个组 |
| `precedence`（hard/soft） | 组树顺序 + 输出顺序 | Program 树顺序即刀路输出顺序 |

### 4.9 最小可行 Plan（MVP）字段清单（NX 插件口径）

MVP 以 [nx-plugin-design.md](./nx-plugin-design.md) §5 为唯一需求来源，面向 NX 插件
「导出 ground truth → 按 plan 重建 → 对比偏差」三步闭环；**不含 FreeCAD 仿真侧字段**
（`fc_template` / `approximation` 由 FreeCAD 消费者负责，NX 插件不消费）：

```
plan_id / input_ref / name
setups[]        mcs(origin,z_axis,x_axis), safe_plane_z, fixture_offset
resources.tools[] type,diameter,num_flutes,(flute_length),lower_corner_radius
features[]      feature_id,feature_type,geometry_ref(anchor_point),params
operations[]    operation_id,operation_type(+nx_template),tool_ref,strategy(必填项见 4.3),technology(必填项见 4.4)
workingsteps[]  workingstep_id,feature_ref,operation_ref,setup_ref
workplan(root,elements)  → Program 组树
diagnostics[]   (info/warning/error)
```

> 导出/导入共用同一清单：导出（PlanExporter）按此完整输出；导入（PlanExecutor）缺省字段
> 允许 NX 组继承值；对比（PlanComparer）以本清单为对齐基线。
> 其余字段（非切削细分、避让点、多轴驱动等）按"可选增强"处理：缺省时 NX 用组继承值，
> 依然可生成刀路，但工艺控制力下降。

---

## 5. 对接风险清单

| # | 风险 | 说明 | 缓解 |
| :--- | :--- | :--- | :--- |
| 1 | **几何映射鸿沟** | 跨文件的面无共享标识（NX Tag / OCCT ID / 重建后新 Tag 均不同），需几何属性匹配 | 质心+面积+类型+法向属性匹配（face_anchors，容差 0.01mm），命中不唯一写 diagnostic；当前 NX 闭环先跑通 NX↔NX，OCCT 接入时复用同算法 |
| 2 | **STEP 哑体** | 导入 STEP 无参数化特征树，AFR 的 Parametric 模式失效 | 用 Workpiece 识别模式；CAPP 侧以 B-Rep 特征输出为准 |
| 3 | **CAM 会话前置** | 每个 .prt 需要 CAMSetup；空 Part 要先 `CreateCamSetup()` + 建模板组 | 插件封装"模板化初始化"（预置 MACHINE/METHOD 模板） |
| 4 | **Inheritable 继承语义** | Builder 参数不设置时继承父组/方法组默认值，Plan 缺字段可能导致"结果和预期不同" | Mapper 显式填充关键参数；校验报告回读实际值。另注意**属性取值四形态混合**（附 A.1）——导出回读与导入写入必须按形态分支，不能统一 `.Value` |
| 5 | **许可证** | 操作创建/组创建/通用 Builder 均 cam_base，但具体功能许可不同（实证反例：`CreateFeatureProcessBuilder` 需 `ug_holemaking`） | 前置 License 检查，不满足时报错而非静默失败；**许可要求可从 XML remarks（`License requirements:`）程序化读取**，建议做成探测而非手写表 |
| 6 | **版本差异** | 如 `BottomClearance` 为 NX2312 新增（XML remarks "Created in NX2312.0.0" 实证）；API 面随版本漂移（2406 实证：`ProgramOrderView` 等视图对象移除、Builder 工厂移至 `OperationCollection`、`UseDefaultName` 枚举化、枚举嵌套化） | 文档标注最低版本；成员级版本可查 XML remarks "Created in NXxxxx"；能力探测 + 以 [nx2406-install-index.md](./nx2406-install-index.md) 事实清单为基准 |
| 7 | **部署形态** | NX 是重型桌面应用，`run_journal.exe -nogui` 可批处理但启动慢；Remoting 可长驻 | 工厂端插件（推荐）或 Remoting 服务，不建议云端按需拉起 |
| 8 | **刀路校验闭环** | NX 生成的刀路时间/长度/过切与 CAPP 预估可能不一致 | 回读 `getToolpathTime/getToolpathLength/gougeCheck` 结果反哺 Plan 评分 |

---

## 附 A：NX2406 属性取值形态与枚举宿主速查（2026-09-03 实证）

> 证据源：`NXBIN\managed\NXOpen.xml` + 反射 `NXOpen.dll`；完整资源索引与"不存在项"清单见 [nx2406-install-index.md](./nx2406-install-index.md)。

**A.1 四种取值形态**

1. `Inheritable*Builder` → `.Value = x`：余量（PartStock/FloorStock/WallStock/BottomStock/BottomClearance）、`FeedsBuilder.SpindleRpmBuilder`/`FeedCutBuilder`(InheritableFeedBuilder)、`PlanarOperationBuilder.DepthPerCut`、`CavityMillingBuilder.DepthPerCut`、刀具 `Tl*Builder`、`CrossOverDistance` 等。
2. **直接 `double`/`int` 直赋**：`NcmClearanceBuilder.SafeDistance/Radius/BoundingBoxClearance`、`MillCutParameters.BoundaryInTol/BoundaryOutTol`、`HoleMachiningCutParameters.MinimalClearance`、`FinishPassesBuilder.NumberOfFinishPasses`(int)。
3. **直接枚举直赋**：`MillCutParameters.CutOrder = CutParametersCutOrderTypes.…`、`NcmClearanceBuilder.ClearanceType = ClearanceTypes.…`、`StepoverBuilder.StepoverType`、`HoleDrillingBuilder.ControlPointOffset/RetractOutputMode/IntersectionStrategy`。
4. **类 + 嵌套枚举 `.Type`**：`CutDirection.Type = CutDirection.Types.…`、`CutPatternBuilder.CutPattern = CutPatternBuilder.Types.…`、`MultiDepthCut.StepMethod = MultiDepthCut.Types.…`。

**A.2 关键枚举宿主与取值（.NET 反射实证）**

| 概念 | .NET 类型 | 取值 |
| :--- | :--- | :--- |
| 切削顺序 | `CutParametersCutOrderTypes`（顶层，直赋） | `LevelFirst\|DepthFirst\|DepthFirstAlways` |
| 顺逆铣 | `CutDirection`（类）→ `.Type` | `Climb\|Conventional\|Forward\|Reverse\|Mixed` |
| 切削模式 | `CutPatternBuilder.Types` | `FollowPart\|FollowPeriphery\|Helical\|Spiral\|…\|Zig\|ZigZag\|…\|Profile\|…` 36 值 |
| 步距模式 | `StepoverBuilder.StepoverTypes` | `Constant\|Scallop\|PercentToolFlat\|Multiple\|Number\|Maximum\|…` |
| 安全几何 | `NcmClearanceBuilder.ClearanceTypes` | `UseCommon\|Automatic\|Plane\|Point\|Cylinder\|Sphere\|BoundingBox\|BoundingCylinder\|Body\|MachineBased\|None` |
| 壁清根 | `MillOperationBuilder.WallCleanupTypes` | `None\|AtStart\|AtEnd\|Automatic` |
| 铣刀子类型 | `MillToolBuilder.CutterSubtypes` | `Mill5\|Mill7\|Mill10\|MillBall\|ChamferTool\|SphericalMill\|DovetailMill` |
| 多刀深 | `MultiDepthCut.Types` | `Increment\|Passes` |
| 默认命名 | `OperationCollection.UseDefaultName` | `False\|True`（Create 第 7 参） |
| 视图 | `CAMSetup.View` | `ProgramOrder\|MachineMethod\|Geometry\|MachineTool` |
| 孔控制点偏置 | `HoleDrillingBuilder.ControlPointOffsetType` | `None\|Feature\|Initial` |
| 孔退刀输出 | `HoleDrillingBuilder.RetractOutputModeType` | `ClearanceOnly\|ClearanceInitial\|Always` |
| 孔相交策略 | `HoleDrillingBuilder.IntersectionStrategyType` | `None\|Part\|Ipw\|IpwAndPart` |

**A.3 版本 / 许可注记（XML remarks 可程序化读取）**

- 每个成员 remarks 含 `License requirements: …` 与 `Created in NXxxxx`。
- 实证：`BottomClearance` → "Created in NX2312.0.0"；操作/组创建 → `cam_base`；`CreateFeatureProcessBuilder` → `ug_holemaking`。

## 附 B：后续验证计划（建议）

1. 在 NX 2406 上用 `run_journal.exe -nogui` 跑通 3.2 的最小示例（建组 → Create → Builder 设参 → Commit → `GenerateToolPath`），确认本文 typeName/组模板类型串/Builder 名称与示例代码**全部可编译可运行**（重点核对 §3.2 标注"待实测"处）。
2. 用 NX 自带模板部件验证从空 Part 建 CAMSetup 的初始化步骤：`Part.CreateCamSetup("mill_contour")`（模板在 `mach\resource\template_part\metric\mill_contour.prt` 等；**NX2406 无 `cam_general_mill.prt`**）。
3. 按 4.2 扩展 `autocam-plan.schema.json` 的 `operation_type` 枚举并补充 `nx_template` / `strategy` / `technology` 子结构（枚举值按附 A.2 校准）。
4. 在本仓库 `src/NXPlugins`（`Autocam.Plugins.sln`）实现 PlanMapper 原型，用 4.9 MVP 字段清单生成一条 `CAVITY_MILL` + 一条 `DRILL` 工序做端到端验证。
5. 实测三处待验证取值：`Stepover` 常量百分比链路（`StepoverTypes.PercentToolFlat` + `PercentToolFlatBuilder.Value`）、`GetToolDrivePoint()/SetToolDrivePoint(string)` 的 string 取值集合、`SpindleModeBuilder` 数值编码到 RPM/SFM/MMPM 的映射。
6. 确认 `CAMSetup.View.MachineMethod` 与 UI"加工方法视图"标签的对应关系，以及 `GetRoot(View)` 四根组下 Program/Tool/Method/Geometry 组的模板 typeName 实际取值。
7. 用 XML remarks 许可注记实现一次许可探测原型（对比当前许可与成员 `License requirements:` 字符串），验证风险 #5 的缓解方案。
