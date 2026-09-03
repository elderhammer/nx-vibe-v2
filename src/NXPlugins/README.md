# NXPlugins — NX 2406 插件骨架

> 定位：Plan 双向验证闭环（导出 → 按 plan 重建 → 对比）的 NX 侧实现载体。
> 需求与设计：`docs/nx-plugin-design.md`；API 事实源：`docs/nx2406-install-index.md`；
> 合同：`schema/autocam-plan.schema.json`（v3.0）。
> 工程决策（2026-09-03，见 nx-plugin-design.md 头部"已确认决策"）：
> 仅支持 NX2406；.NET Framework 4.8；代码全部在本目录（sln 在仓库根 `Autocam.Plugins.sln`）。

## 当前状态（2026-09-04）：实证收官 + PlanExporter 完成

- `NXPlugins.csproj`：类库工程，已引用 NXOpen / NXOpen.UF / NXOpen.Utilities
  （HintPath 指向 `$(NX_DIR)\NXBIN\managed\`，默认
  `C:\Program Files\Siemens\NX2406`；`Private=False`，**NXOpen 程序集不随仓库分发**）。
  ⚠️ **csproj/sln 尚未纳入 PlanExporter/Journal 源码**——代码以 csc 临时编译 + NX 运行方式使用；
  并入 sln 发布（设计 §7 步骤 4）为待办。
- `Properties/AssemblyInfo.cs`：装配元数据（初始骨架，v0.1.0）。
- `Journal/`：探针 ×12（步骤 0 实证收官 + 2026-09-04 收官批 `CamProbeFinalize` 全通，结论回填
  docs/nx2406-install-index.md §2.1/§3）；`ExporterAdapter.cs` = [I] 层导出适配器
  （test.prt → samples/test.plan.json，schema 复验 PASS）。
- `PlanExporter/` + `PlanExporterTests/`：纯逻辑核心 + 性质表单测全绿（spec 落档，无 NX 依赖）。

## 规划目录（按 nx-plugin-design.md §7 步骤 0-4 进度）

```
Journal/            ✅ 探针×12 入库（步骤 0 实证收官）；ExporterAdapter=[I] 适配器
PlanExporter/       ✅ 纯逻辑核心 + [U] 单测（spec 落档）；⚠️ 未并入 csproj
PlanParser/         未开工（导入侧）
PlanExecutor/       未开工（步骤 2；参考官方样例
                    %NX_DIR%\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport）
FaceResolver/       未开工（U-5/U-5c 结案：面级锚点无生产源，待区域级增强）
PlanComparer/       未开工（步骤 3；容差按决策④先默认后校准）
```

## 实证收官注记（2026-09-04）

- 索引 §3 待验证清单全部 ★ 结案（收官批探针 CamProbeFinalize 源文件 + 证据档
  samples/camprobe-finalize-20260904-010401.txt）。
- 剩余 [T]：spec U-6（Stepover 有效写入通道）、U-1 维持（白名单 + diag 决议）。
- 运行纪律（含批处理 CAM 会话初始化顺序）见 docs/nx2406-install-index.md §2.1。

## 构建

- 打开仓库根 `Autocam.Plugins.sln`（Visual Studio，.NET Framework 4.8 工作负载）。
- 换机/换 NX 目录：设环境变量 `NX_DIR` 或在 csproj 覆盖 `<NX_DIR>`。
- 勿把 `NXOpen*.dll` 等西门子程序集提交进 git（`Private=False` 已保证引用不复制）。

## 运行（验证入口，2026-09-04 实证）

- 批处理：`"C:\Program Files\Siemens\NX2406\NXBIN\run_journal.exe" src\NXPlugins\Journal\<探针>.cs`
  —— 无界面直接执行 journal 源文件（帮助用法**无 `-nogui` 旗标**）。含 CAM 会话操作须按索引 §2.1
  纪律：先 `NewDisplay` 建件 → `Session.CreateCamSession()` → `CreateCamSetup`。
- 交互：NX 会话 File → Execute → NX Open（csc 预编译 exe，历史探针/适配器路径）。
