# NXPlugins — NX 2406 插件骨架

> 定位：Plan 双向验证闭环（导出 → 按 plan 重建 → 对比）的 NX 侧实现载体。
> 需求与设计：`docs/nx-plugin-design.md`；API 事实源：`docs/nx2406-install-index.md`；
> 合同：`schema/autocam-plan.schema.json`（v3.0）。
> 工程决策（2026-09-03，见 nx-plugin-design.md 头部"已确认决策"）：
> 仅支持 NX2406；.NET Framework 4.8；代码全部在本目录（sln 在仓库根 `Autocam.Plugins.sln`）。

## 当前状态（2026-09-04）：实证收官 + PlanExporter/PlanExecutor 完成 + U-7 A′ 收官

- `NXPlugins.csproj`：类库工程，已引用 NXOpen / NXOpen.UF / NXOpen.Utilities
  （HintPath 指向 `$(NX_DIR)\NXBIN\managed\`，默认
  `C:\Program Files\Siemens\NX2406`；`Private=False`，**NXOpen 程序集不随仓库分发**）。
  ✅ **生产代码已全部纳入 csproj**（Journal\*、PlanExporter\*、PlanExecutor\*；测试目录不入库，
  走 scripts/run-unittests.ps1 红线回归）——sln 构建 = 设计 §7 步骤 4 完成。
- `Properties/AssemblyInfo.cs`：装配元数据（初始骨架，v0.1.0）。
- `Journal/`：探针 ×14（步骤 0 实证收官 + 收官批 `CamProbeFinalize` + `CamProbeExecutor` 全通，
  结论回填 docs/nx2406-install-index.md §2.1/§3）；`ExporterAdapter.cs` / `ExecutorAdapter.cs` =
  导出/重建 [I] 层适配器（test.prt → test.plan.json → test.rebuilt-*.prt 闭环跑通）。
- `PlanExporter/` + `PlanExecutor/`：纯逻辑核心（spec 各落档；[U] 红线 54/54 全绿——含 U-7 A′
  词集：Tool.GetTypeAndSubtype 原文直写 + 重建注册对表 + 家族关键词回退 + PlanValidator 词集收紧，
  见 docs/nx-tool-type-enum-spec.md）；`PlanExporterTests/`/`PlanExecutorTests/` 测试目录不入库编译。
- 合编脚本：`scripts/compile-executor-adapter.ps1`（重建 exe）与 `scripts/compile-exporter-adapter.ps1`
  （导出 exe，U-7 新增，镜像前者）→ .claude/tmp/*.exe 供 NX File → Execute。

## 规划目录（按 nx-plugin-design.md §7 步骤 0-4 进度）

```
Journal/            ✅ 探针×14 + ExporterAdapter/ExecutorAdapter [I] 适配器
PlanExporter/       ✅ [U]+[I] 闭环（spec 落档）
PlanExecutor/       ✅ [U] 33/33 + [I] 集成闭环（spec 落档；参考官方样例
                    %NX_DIR%\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport）
PlanParser/         未开工（导入侧）
FaceResolver/       未开工（U-5/U-5c 结案：面级锚点无生产源，待区域级增强）
PlanComparer/       未开工（步骤 3；两端素材已齐：test.prt ↔ test.rebuilt.prt（U-7 最新）；历史
                    重建件 132130/014933 保留为基准）
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
- ⚠️ 本机（2026-09-04）缺 .NET Framework 4.8 Developer Pack → MSBuild 构建报 MSB3644；
  编译有效性由 csc 合编路径背书（scripts/compile-executor-adapter.ps1 全量同集源码通过）。
  装 Developer Pack 后即可 sln 内一键构建。

## 运行（验证入口，2026-09-04 实证）

- 批处理：`"C:\Program Files\Siemens\NX2406\NXBIN\run_journal.exe" src\NXPlugins\Journal\<探针>.cs`
  —— 无界面直接执行 journal 源文件（帮助用法**无 `-nogui` 旗标**）。含 CAM 会话操作须按索引 §2.1
  纪律：先 `NewDisplay` 建件 → `Session.CreateCamSession()` → `CreateCamSetup`。
- 交互：NX 会话 File → Execute → NX Open（csc 预编译 exe，历史探针/适配器路径）。
