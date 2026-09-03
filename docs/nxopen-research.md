# NX Open API 调研：CAM 编程能力全景与 CAPP Plan 对接

更新时间：2026-08-24\
适用范围：Siemens NX 2406+ / NX X（API 以 NXOpen .NET 为准，C++/Python/Java 同名）\
关联文档：[autocam-prd.md](../core/autocam-prd.md)、[capp-operation-mapping-table.md](../core/capp-operation-mapping-table.md)、[autocam-plan.schema.json](../schema/autocam-plan.schema.json)

---

## 1. 阅读指南

- 了解 NX Open 整体能力：读第 2 章
- **了解 NX CAM 编程 API（创建工序/刀具/几何/MCS/非切削/刀路生成）**：读第 3 章
- **指导 CAPP Plan 输出字段设计（按 NX Builder 参数面反推）**：读第 4 章
- 评估对接落地风险：读第 5 章
- 特征识别与交叉验证（已抽离至 [feature-ml/nx-feature-recognition-validation.md](../feature-ml/nx-feature-recognition-validation.md)）

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

### 3.1 CAM 对象模型

一个 Part 对应一个 CAM 配置（`CAMSetup`）。CAMSetup 内部是**四类视图 + 一个操作集合**：

```
CAMSetup (Part.CreateCamSetup() / Part.CAMSetup)
├── ProgramOrderView (程序顺序视图)   ← Program 组树，决定刀路输出顺序
├── MachineToolView  (机床视图)      ← Machine 组树（机床定义 + 控制器）
├── GeometryView     (几何视图)      ← Geometry 组树（MCS / WORKPIECE / PART / BLANK…）
├── MethodView       (方法视图)      ← Method 组树（粗加工 / 精加工 / 半精 / 钻孔…）
└── OperationCollection             ← 所有 Operation，每个挂在一个 Program / Method /
                                      Tool / Geometry 组之下（四视图交集定位）
```

**API 入口**：

```csharp
Session theSession = Session.GetSession();
Part workPart = theSession.Parts.Work;

CAM.CAMSetup camSetup = workPart.CAMSetup;              // 已存在
// 或 workPart.CreateCamSetup();

// 四个视图的根组集合
CAM.NCGroupCollection programs   = camSetup.ProgramOrderView.Root;
CAM.NCGroupCollection machines   = camSetup.MachineToolView.Root;
CAM.NCGroupCollection geometries = camSetup.GeometryView.Root;
CAM.NCGroupCollection methods    = camSetup.MethodView.Root;

// 操作集合
CAM.OperationCollection ops = camSetup.CAMOperationCollection;
```

### 3.2 创建一条工序的完整链路（C# 示例）

标准流程五步：**建组 → Create 操作 → 取 Builder 设参 → Commit → Destroy**。

```csharp
// ---- 1. 四类组：Program / Method / Tool / Geometry ----
CAM.NCGroup programGroup = (CAM.NCGroup)programs.CreateProgram(
    "PROGRAM_MAIN", "MainProgram");                        // (组名, 显示名)
CAM.NCGroup methodGroup  = (CAM.NCGroup)methods.CreateMethod(
    "MILL_ROUGH", "Roughing");                             // 方法组
CAM.NCGroup toolGroup    = (CAM.NCGroup)machines.CreateTool(
    "T1_D10", "D10 End Mill");                             // 刀具组
CAM.NCGroup geomGroup    = (CAM.NCGroup)geometries.CreateGeometry(
    "MCS_1", "MCS 1");                                     // 几何组（MCS/Workpiece）

// ---- 2. 创建操作：四个父组 + 类型名 + 子类型名 + 是否默认命名 + 新名 ----
CAM.Operation operation = ops.Create(
    programGroup,        // 父 Program 组
    methodGroup,         // 父 Method 组
    toolGroup,           // 父 Tool 组
    geomGroup,           // 父 Geometry 组
    "CAVITY_MILL",       // typeName（操作类型，见 3.3）
    "",                  // subtypeName（子类型，如 "MILL_OPEN"）
    true,                // useDefaultName
    "CAVITY_1");         // newOperationName

// ---- 3. Builder 设参：createXxxBuilder(operation) 返回对应参数面板 ----
CAM.PlanarMillingBuilder builder = camSetup.CreatePlanarMillingBuilder(operation);
builder.CutParameters.PartStock.Value        = 0.3;   // 侧壁余量
builder.CutParameters.FloorStock.Value       = 0.3;   // 底面余量
builder.CutParameters.Stepover.Percent       = 50.0;  // 步距 50% 刀径
builder.CutParameters.DepthPerCut.Value      = 2.0;   // 每刀深度 mm
builder.CutParameters.CutOrder.Value         = CAM.CutOrder.LevelFirst;
builder.CutParameters.CutDirection.Value     = CAM.CutDirection.Climb;
// 非切削移动（安全平面 + 进刀/退刀），见 3.10
CAM.NcmPlanarBuilder ncm = builder.NonCuttingBuilder;
ncm.ClearanceBuilder.ClearanceType.Value = CAM.NcmClearanceBuilder.ClearanceTypes.Plane;
ncm.ClearanceBuilder.SafeDistance.Value  = 10.0;
// 进给与转速，见 3.4 FeedsBuilder
builder.FeedsBuilder.SpindleRpmBuilder.Value = 6000;
builder.FeedsBuilder.FeedCutBuilder.Value    = 1200;

// ---- 4. 提交 / 销毁 ----
NXOpen.NXObject nXObject = builder.Commit();
builder.Destroy();
```

关键约定：

- `Create()` 的 **typeName / subtypeName** 是 NX 内部操作类型的字符串名（如 `CAVITY_MILL` / `DRILL` / `TURNING_ROUGH`），可写进 Plan 的 `nx_template` 字段。
- 每个操作类型有对应的 **Builder 类**（约 70 个 `createXxxBuilder(CAMObject)` 方法），Builder 上暴露的参数就是 NX 对应工序对话框的每一个参数。
- Builder 是**增量修改**模型：`builder.Get()` 读当前值、`属性.Value = x` 写值、`Commit()` 生效、`Destroy()` 释放。
- 所有参数面均可**不设置**——不设置时工序继承所在组的默认值（Inheritable 语义，见 5.4），这也是"最小 Plan 也能出刀路"的基础。

### 3.3 操作类型全景（不限于 AP224 15 类）

`OperationCollection.Create()` 的 typeName 覆盖 NX CAM 全部加工域，按大类列举如下（**全部 cam_base 许可**）：

| 操作大类 | typeName 示例（非穷举） | 对应 Builder | 用途 |
| :--- | :--- | :--- | :--- |
| **铣削-2.5轴** | `CAVITY_MILL` / `PLANAR_MILL` / `FACE_MILLING` / `PLUNGE_MILL` / `GROOVE_MILL` | `CavityMillingBuilder` / `PlanarMillingBuilder` / `FaceMillingBuilder` / `PlungeMillingBuilder` / `GrooveMillingBuilder` | 挖槽/平面轮廓/面铣/插铣/槽铣 |
| **铣削-3轴** | `ZLEVEL_PROFILE` / `ZLEVEL_FOLLOW_PARTS` / `SURFACE_CONTOUR` / `FLOWCUT` / `CHAMFER_MILL` / `ENGRAVE` / `CYLINDER_MILL` | `ZlevelMillingBuilder` / `SurfaceContourBuilder` / `FlowcutBuilder` / `ChamferMillingBuilder` / `EngravingBuilder` / `CylinderMillingBuilder` | 等高/曲面轮廓/清根/倒角/雕刻/圆柱铣 |
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

### 3.4 Builder 参数面：铣削

Builder 继承链：`OperationBuilder → MillOperationBuilder → PlanarOperationBuilder / CavityMillingBuilder / ZlevelMillingBuilder / ...`

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

**MillCutParameters（铣削切削参数面，与 CAPP strategy 直接对应）**：

| 参数 | 说明 | 典型取值 |
| :--- | :--- | :--- |
| `PartStock` / `FloorStock` / `WallStock` | 侧壁/底面/壁余量 (mm) | 0.2-0.5 |
| `Stepover` | 步距（Percent 或 Value） | 50% 刀径 |
| `CutOrder` | 切削顺序 | `LevelFirst` / `AreaFirst` |
| `CutDirection` | 顺逆铣 | `Climb` / `Up` |
| `FinishPasses` | 精加工刀数 | 1-2 |
| `MultiDepthCut` | 多层切削开关 | true/false |
| `DepthPerCut` | 每刀深度 (mm) | 0.5-2.0 |
| `BoundaryInTol` / `OutTol` | 边界内/外公差 | 0.01 |

**PlanarOperationBuilder（平面铣追加）**：`CutPattern`（切削模式：等距环切/平行/轮廓/单向/往复…）、`CutArea`（切削区域）、`PartGeometry`（部件几何）、`NonCuttingBuilder`（返回 `NcmPlanarBuilder`，见 3.10）、`ToolAxisFix` 等。

**FeedsBuilder（进给/转速面）**：

| 参数 | 说明 |
| :--- | :--- |
| `SpindleRpmBuilder` | 主轴转速 (rpm) |
| `SurfaceSpeedBuilder` | 表面速度 (m/min)（与 rpm 二选一） |
| `SpindleModeBuilder` | 主轴模式（RPM / SFM / MMPM） |
| `FeedCutBuilder` | 切削进给 (mm/min 或 mm/rev) |
| `FeedApproachBuilder` / `FeedEngageBuilder` / `FeedDepartureBuilder` | 逼近/进刀/退刀进给 |
| `RetractSpeed` | 退刀速度 |
| `RecalculateData()` | 按公式重算进给数据 |

### 3.5 Builder 参数面：孔加工

继承链：`HoleDrillingBuilder → HoleMachiningBuilder → ...`。孔加工是 CAPP 孔特征直连价值最高的域。

**HoleMachiningCutParameters（孔加工切削参数，继承自 CutParameters）**：

| 参数 | 说明 |
| :--- | :--- |
| `bottomStock` | 底部余量 (mm) |
| `topOffset`（`VerticalPosition`） | 顶部偏置（从孔口/面起算） |
| `cornerControl`（`CornerControlBuilder`） | 转角控制（如倒角停留） |
| `minimalClearance` | 最小间隙 |
| `bottomClearance` | 底部间隙（**NX2312 新增**） |

**HoleDrillingBuilder（继承 HoleMachiningBuilder）**：

| 参数 | 说明 |
| :--- | :--- |
| `controlPointOffset`（`ControlPointOffsetType`） | 控制点偏置方式（孔顶/孔底/自动） |
| `retractOutputMode`（`RetractOutputModeType`） | 退刀输出模式（退到安全平面/增量…） |
| `intersectionStrategy`（`IntersectionStrategyType`） | 与已加工特征相交策略 |
| `crossOverDistance`（`InheritableToolDepBuilder`） | 越程距离（与刀具相关） |
| `toolDrivePoint`（get/set） | 刀具驱动点（刀尖/球心） |
| `cutParameters()` | **已 Deprecated**，改用 `holeMachiningBuilder.cuttingParameters()` |

**HoleMachiningBuilder 完整参数面**：`CycleTable` / `Cycle`（钻孔循环：DRILL/PECK/TAP/BORE/REAM…）/ `cuttingParameters(HoleMachiningCutParameters)` / `PredefinedDepth`（预定义深度：通孔/盲孔/深度值）/ `ControlPointOffset` / `CollisionCheck` / `RetractOutputMode`。

### 3.6 Builder 参数面：车削 / 多轴 / WEDM / 探测（概要）

- **车削**：`RoughTurningBuilder`（粗车：切削区域 cut region、深度、进给分层）、`FinishTurningBuilder`（精车）、`ThreadTurningBuilder`（螺纹车削：螺距/牙型/多次进刀）、`CenterlineDrillTurningBuilder`（车床中心线钻孔）、`MultiAxisTurnMillingBuilder`（多轴车铣）。车削切削参数面为 `TurnCutParameters`（`TurCutRegion`、`Stock`、`Depth`、`FeedRate` 等）。
- **多轴**：`MultiAxisRoughingBuilder` / `MultiAxisWallFinishingBuilder` / `MultiAxisDeburringBuilder`，参数面含刀具轴（`ToolAxis`）、驱动方式（`DriveMode`）、切削深度等。
- **WEDM**：`WedmOperationBuilder`（线切割：轮廓、锥度、多次切割、张丝参数）。
- **探测**：`OnMachineProbingBuilder`（探测点/矢量/触测速度/保护移动）、`MillToolProbingBuilder`（刀具长度/直径测量）。

### 3.7 几何组与刀具 Builder

**组创建（NCGroupCollection）**：

| 方法 | 说明 |
| :--- | :--- |
| `CreateProgram(name, displayName)` | 程序组（如 `PROGRAM`、按 Setup 分组的子程序） |
| `CreateMethod(name, displayName)` | 方法组（如 `MILL_ROUGH` / `MILL_FINISH` / `DRILL_METHOD`） |
| `CreateTool(name, displayName)` | 刀具组（如 `T1_D10`） |
| `CreateGeometry(name, displayName)` | 几何组（如 `MCS_1` / `WORKPIECE`） |

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
| `holderSectionBuilder` / `shankSectionBuilder` | MillingToolBuilder | 夹持段 / 刀柄段 |
| `coolantThrough` | MillingToolBuilder / DrillToolBuilder | 中心出水 |
| `millingTrackpointBuilder` | MillingToolBuilder | 对刀点 |
| `cutterSubtype()` / `setCutterSubtype(CutterSubtypes)` | MillToolBuilder | 铣刀子类型（平底/球头/牛鼻/T 型…） |
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

### 3.8 MCS 与装夹（OrientGeomBuilder）

`MillOrientGeomBuilder`（铣）/ `TurnOrientGeomBuilder`（车）负责装夹坐标系与安全几何：

| 参数 | 说明 |
| :--- | :--- |
| `mcs()` / `setMcs(CartesianCoordinateSystem)` | 加工坐标系（原点 + Z 轴 + X 轴） |
| `rcs()` / `setRcs` | 参考坐标系 |
| `fixtureOffsetBuilder` | 夹具偏置号（G54/G55…） |
| `linkRcsToMcs` | RCS 跟随 MCS 联动 |
| `getToolAxisMode` / `setToolAxisMode` | 刀轴模式 |
| `toolAxisVector` / `setToolAxisVector` | 刀轴矢量 |
| `mcsLocationMode` / `mcsWorkpiece` | MCS 定位模式 / 工件关联 |
| `setBlockMcsOrigin(McsZeroFace, McsZeroPosition)` | 方块件 MCS 原点（顶面+角点） |
| `setCylinderMcsOrigin` | 回转件 MCS 原点 |
| `transferClearanceBuilder` | 安全平面（见 3.10） |
| `transferAvoidanceFromBuilder` / `StartBuilder` / `ReturnBuilder` / `GohomeBuilder` | FROM / START / RETURN / GOHOME 避让点 |
| `setLowerLimitMode` / `LowerLimitPlane` | 最低加工限制 |

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

| 方法 | 说明 |
| :--- | :--- |
| `holeBossGeomType` / `setHoleBossGeomType(HoleBossTypes)` | 几何类型（孔/凸台/螺纹孔/螺纹凸台） |
| `createHoleBossBuilder(entities, diameter, depth, toolAxis, depthLimit)` | 定义孔（面集 + 直径 + 深度 + 刀轴 + 深度限制） |
| `createThreadedHoleBuilder(...)` | 定义螺纹孔（tapDrillSize / depth / majorDiameter / minorDiameter / size / pitch / rotation / form / tableStandard） |
| `createThreadedBossBuilder(...)` | 定义螺纹凸台 |
| `holeList` / `bossList` / `threadedHoleList` / `threadedBossList` | 批量孔/凸台列表 |
| `setFormAndPitch` / `setPitch` / `setRotation` | 螺纹形式 / 螺距 / 旋向 |
| `setOptimization` | 孔加工顺序优化 |
| `getCenterHoleGeometry` / `getChamferHoleGeometry` | 中心孔 / 倒角孔几何 |

### 3.10 非切削移动与安全平面（NcmPlanarBuilder）

`NcmPlanarBuilder`（平面铣非切削移动，通过 `PlanarOperationBuilder.NonCuttingBuilder` 获取）是 CAPP 非切削策略的落点：

**嵌套枚举**：`CutcomTypes`（刀具补偿）、`FinalTypes` / `InitialTypes`（最终/初始安全平面类型）、`InitialEng` / `FinalRet`（进刀/退刀策略）、`TransferBetweenLevels` / `TransferBetweenRegions` / `TransferWithinLevels`（层间/区域间/层内转移）、`PredrillPointsOutputOptions`（预钻孔点输出）。

| 方法 | 返回类型 | 说明 |
| :--- | :--- | :--- |
| `clearanceBuilder()` | `NcmClearanceBuilder` | 安全平面/点（见下） |
| `engageClosedAreaBuilder()` / `engageOpenAreaBuilder()` / `engageInitialClosedBuilder()` / `engageInitialOpenBuilder()` | `NcmPlanarEngRetBuilder` | 进刀策略（封闭区/开放区/初始） |
| `retractAreaBuilder()` / `retractFinalBuilder()` | `NcmPlanarEngRetBuilder` | 退刀策略 |
| `transferAvoidanceFromBuilder()` / `StartBuilder()` / `ReturnBuilder()` / `GohomeBuilder()` | `NcmAvoidancePointBuilder` | FROM/START/RETURN/GOHOME 避让点 |
| `smoothingBuilder()` | — | 拐角光顺 |
| `collisionCheck` | — | 碰撞检查开关 |
| `suppressCutcom` / `cutcomType` | — | 刀具补偿 |
| `initialSafeDistanceBuilder()` / `finalSafeDistanceBuilder()` | — | 初始/最终安全距离 |
| `transferWithinLevelsHeightBuilder()` / `transferBetweenLevelsSafeDistanceBuilder()` | — | 层内抬刀高度 / 层间安全距离 |
| `predrillPoints(Point[])` | — | 预钻孔点列表 |

**NcmClearanceBuilder（安全平面）**：

| 参数 | 说明 |
| :--- | :--- |
| `ClearanceTypes` 枚举 | 自动 / 平面 / 点 / 圆柱 / 球 / 包围盒 / 体 |
| `clearanceType()` / `setClearanceType` | 安全几何类型 |
| `safeDistance()` / `setSafeDistance` | 安全距离 (mm) |
| `planeXform()` / `setPlaneXform(Plane 或 Xform)` | 安全平面定义 |
| `pointObject()` / `setPointObject(Point)` | 安全点 |
| `axisObject()` / `setAxisObject(Direction)` | 圆柱/旋转轴 |
| `radius()` / `setRadius` | 圆柱/球半径 |
| `body()` / `setBody` | 体安全几何 |
| `boundingBoxClearance()` / `setBoundingBoxClearance` | 包围盒安全几何 |

### 3.11 刀路生成与后处理（CAMSetup / Operation）

| API | 说明 |
| :--- | :--- |
| `Operation.createToolPathEditorBuilder()` | 打开刀路编辑器（查看/编辑刀位点） |
| `Operation.generateIpw()` | 生成 IPW（中间工件，供残余加工） |
| `Operation.getToolpathTime()` / `getToolpathLength()` | 刀路时间 / 长度 |
| `Operation.getCuttingTime()` / `getCuttingLength()` | 纯切削时间 / 长度 |
| `Operation.gougeCheck()` | 过切检查 |
| `Operation.insertFeature()` / `removeFeature()` | 增删关联加工特征 |
| `Operation.getParent(CAMSetup.View)` | 取操作在指定视图下的父组 |
| `CAMSetup.generateToolPath(CAMObject[])` | 生成选中操作的刀路（批处理） |
| `CAMSetup.parallelGenerate` | 并行生成（多核） |
| `CAMSetup.outputClsf()` | 输出 CLSF（刀位源文件） |
| `CAMSetup.postprocess()` / `postprocessWithPostprocessor()` | 后处理为 NC 代码 |
| `CAMSetup.calculateMachiningTimes()` | 统计加工时间 |
| `CAMSetup.gougeCheck()` | 批量过切检查 |
| `CAMSetup.deleteMachineCode()` | 删除已生成 NC |
| `CAMSetup.createWorkInstructionBuilder()` | 生成作业指导书（工单卡片） |
| `CAMSetup.createPostProcessManagerBuilder()` | 后处理管理器 |

> 这意味着 CAPP 关心的**周期时间估算、刀路长度、过切风险**都可直接从 NX 拿真实数据回传，形成"Plan → NX → 实际参数 → 反馈校准"闭环。

### 3.12 特征驱动加工（NX 侧的特征→工序）

NX CAM 不仅接受"手动选几何建工序"，还支持**特征驱动**链路，与 Autocam 的"特征识别 → CAPP"范式同构：

- `FeatureRecognitionBuilder`：程序化触发特征识别（AFR 的编程接口，识别孔/槽/腔体/台阶/凸台/倒角/圆角）。
- `createFeatureProcessBuilder(...)`：按特征自动生成加工工序（feature process）。
- `Operation.insertFeature()` / `removeFeature()`：把 CAM 特征挂到已有工序上，实现"同一工序加工一组同参数特征"（对应 CAPP 的 merge_group 合并逻辑）。

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
       ├─ 派生计算：stepover% → Stepover.Percent；安全平面 → clearanceType=Plane + safeDistance
       └─ 几何解析：geometry_ref（面/边/孔心）→ NX Face/Edge/Point（面映射见 [nx-feature-recognition-validation.md](../feature-ml/nx-feature-recognition-validation.md) §5）
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

> 保留 `feature_type`（AP224 15 类）作为**特征层分类**不变；`operation_type` 升格为**工序层分类**，两者通过 workingstep 关联。特征→工序的映射规则继续沿用 [capp-operation-mapping-table.md](../core/capp-operation-mapping-table.md)。

### 4.3 strategy 结构化字段（按 NX Builder 参数面反推）

所有字段都能在 3.4-3.10 找到直接落点：

| Plan.strategy 字段 | 类型 | NX Builder 落点 | 必填 |
| :--- | :--- | :--- | :--- |
| `cut_pattern` | 枚举 | `PlanarOperationBuilder.CutPattern`（FOLLOW_PART/FOLLOW_PERIPHERY/HILBERT/PARALLEL_LINES/PROFILE/…） | 粗铣必填 |
| `cut_order` | 枚举 | `MillCutParameters.CutOrder`（LevelFirst/AreaFirst） | 建议 |
| `cut_direction` | 枚举 | `MillCutParameters.CutDirection`（Climb/Up） | 建议 |
| `depth_per_cut` | number(mm) | `MillCutParameters.DepthPerCut.Value` | 粗铣必填 |
| `stepover` | number | `MillCutParameters.Stepover`（Percent 或 Value） | 粗铣必填 |
| `finish_passes` | int | `MillCutParameters.FinishPasses` | 建议 |
| `multi_depth_cut` | bool | `MillCutParameters.MultiDepthCut` | 粗铣建议 |
| `part_stock` / `floor_stock` / `wall_stock` | number(mm) | `MillCutParameters.PartStock/FloorStock/WallStock` | 建议 |
| `wall_cleanup` | 枚举 | `MillOperationBuilder.WallCleanupType` | 可选 |
| `tool_axis` | vector | `MillOrientGeomBuilder.toolAxisVector` / `DrillGeomBuilder.toolAxis` | 建议 |
| `cycle` | 枚举 | `HoleMachiningBuilder.Cycle`（DRILL/PECK/TAP/BORE/REAM/…） | 钻孔必填 |
| `depth` / `depth_limit` | number/枚举 | `HoleMachiningBuilder.PredefinedDepth`（through/blind/值） | 钻孔必填 |
| `bottom_stock` / `bottom_clearance` | number(mm) | `HoleMachiningCutParameters.bottomStock / bottomClearance` | 建议 |
| `top_offset` | number(mm) | `HoleMachiningCutParameters.topOffset` | 可选 |
| `control_point_offset` | 枚举 | `HoleDrillingBuilder.controlPointOffset`（顶/底/自动） | 可选 |
| `retract_output_mode` | 枚举 | `HoleDrillingBuilder.retractOutputMode`（退到安全平面/增量） | 可选 |
| `tool_drive_point` | 枚举 | `HoleDrillingBuilder.toolDrivePoint`（刀尖/球心） | 可选 |
| `cross_over_distance` | number(mm) | `HoleDrillingBuilder.crossOverDistance` | 可选 |
| `turn_cut_region` | 枚举 | `RoughTurningBuilder` 切削区域（外圆/端面/内孔…） | 车削必填 |
| `non_cutting.approach` / `engage` / `retract` / `final` | 对象 | `NcmPlanarBuilder` engageClosed/Open、retractArea/Final（`NcmPlanarEngRetBuilder`：策略+距离+角度） | 可选 |
| `non_cutting.clearance` | 对象 | `NcmClearanceBuilder`：`{ type: "plane"|"point"|"cylinder"|"sphere"|"box"|"body", safe_distance, origin, normal, radius }` | 建议必填 |
| `non_cutting.from/start/return/gohome` | 点对象 | `NcmAvoidancePointBuilder`（FROM/START/RETURN/GOHOME） | 可选 |
| `non_cutting.transfer_within_levels_height` | number | `transferWithinLevelsHeightBuilder` | 可选 |
| `non_cutting.predrill_points` | 点数组 | `predrillPoints(Point[])` | 可选 |

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
| `spindle_mode` | `FeedsBuilder.SpindleModeBuilder` | RPM / SFM / MMPM |
| `feed_cut` | `FeedsBuilder.FeedCutBuilder.Value` | 切削进给（mm/min 或 mm/rev，由单位字段决定） |
| `feed_approach` / `feed_engage` / `feed_departure` | `FeedApproachBuilder` / `FeedEngageBuilder` / `FeedDepartureBuilder` | 细分进给 |
| `retract_speed` | `FeedsBuilder.RetractSpeed` | 退刀速度 |
| `coolant` | 操作/刀具 coolant 参数（含 `coolantThrough`） | 冷却类型枚举（on/mist/flood/through） |
| `tolerance.intol` / `tolerance.outtol` | `MillCutParameters.BoundaryInTol / OutTol` | 轮廓内外公差 |
| `minimal_clearance` | `HoleMachiningCutParameters.minimalClearance` | 孔加工最小间隙 |

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
| `type` | `MillToolBuilder.setCutterSubtype(CutterSubtypes)` 或钻孔子类型 | 平底/球头/牛鼻/T 型/倒角/钻头/丝锥… |
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
| `holder` | `holderSectionBuilder` | 夹持段参数 |
| `coolant_through` | `coolantThrough` | 中心出水 |
| `track_point` | `millingTrackpointBuilder` | 对刀点 |
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

**几何解析链路**：Plan 里的 `face_id / edge_id` 是 OCCT 侧的遍历 ID，NX 侧需要通过**几何属性面映射**（质心+面积+类型+法向，见 [nx-feature-recognition-validation.md](../feature-ml/nx-feature-recognition-validation.md) §5）解析成 NX Tag 后传入 Builder；`anchor_point` 为无映射能力时的近似兜底。这是整个对接中唯一需要算法的环节。

### 4.7 setup 补 MCS 与机床

| Plan.setup 字段 | NX Builder 落点 | 建议 |
| :--- | :--- | :--- |
| `mcs.origin` / `mcs.z_axis` / `mcs.x_axis` | `MillOrientGeomBuilder.setMcs(CartesianCoordinateSystem)` | 每个 setup 必填 |
| `fixture_offset` | `fixtureOffsetBuilder` | G54=1…，建议必填 |
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
| 1 | **几何映射鸿沟** | Plan 的 OCCT 面/边 ID 与 NX Parasolid Tag 无共享标识，需几何属性匹配 | 质心+面积+类型+法向面映射（[nx-feature-recognition-validation.md](../feature-ml/nx-feature-recognition-validation.md) §5），容差 0.01mm |
| 2 | **STEP 哑体** | 导入 STEP 无参数化特征树，AFR 的 Parametric 模式失效 | 用 Workpiece 识别模式；CAPP 侧以 B-Rep 特征输出为准 |
| 3 | **CAM 会话前置** | 每个 .prt 需要 CAMSetup；空 Part 要先 `CreateCamSetup()` + 建模板组 | 插件封装"模板化初始化"（预置 MACHINE/METHOD 模板） |
| 4 | **Inheritable 继承语义** | Builder 参数不设置时继承父组/方法组默认值，Plan 缺字段可能导致"结果和预期不同" | Mapper 显式填充关键参数；校验报告回读实际值 |
| 5 | **许可证** | 操作类型均 cam_base，但具体模块（车削/多轴/WEDM/增材/探测）需要对应功能许可 | 前置 License 检查，不满足时报错而非静默失败 |
| 6 | **版本差异** | 如 `bottomClearance` 为 NX2312 新增；Builder API 版本间有微调 | 文档标注最低版本，插件按 NX 版本做能力探测 |
| 7 | **部署形态** | NX 是重型桌面应用，`run_journal.exe -nogui` 可批处理但启动慢；Remoting 可长驻 | 工厂端插件（推荐）或 Remoting 服务，不建议云端按需拉起 |
| 8 | **刀路校验闭环** | NX 生成的刀路时间/长度/过切与 CAPP 预估可能不一致 | 回读 `getToolpathTime/getToolpathLength/gougeCheck` 结果反哺 Plan 评分 |

---

## 附：后续验证计划（建议）

1. 在 NX 2406 上用 `run_journal.exe -nogui` 跑通 3.2 的最小示例（建组 → Create → Builder 设参 → Commit → generateToolPath），确认 typeName/Builder 名称与文档一致。
2. 用 NX 自带模板零件（如 cam_general_mill.prt）验证从空 CAMSetup 建工序的初始化步骤。
3. 按 4.2 扩展 `autocam-plan.schema.json` 的 `operation_type` 枚举并补充 `nx_template` / `strategy` / `technology` 子结构。
4. 在 `autocam-plugins` 的 NX 插件中实现 PlanMapper 原型，用 4.9 MVP 字段清单生成一条 `CAVITY_MILL` + 一条 `DRILL` 工序做端到端验证。
