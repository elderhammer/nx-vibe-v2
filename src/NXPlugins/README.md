# NXPlugins — NX 2406 插件骨架

> 定位：Plan 双向验证闭环（导出 → 按 plan 重建 → 对比）的 NX 侧实现载体。
> 需求与设计：`docs/nx-plugin-design.md`；API 事实源：`docs/nx2406-install-index.md`；
> 合同：`schema/autocam-plan.schema.json`（v3.0）。
> 工程决策（2026-09-03，见 nx-plugin-design.md 头部"已确认决策"）：
> 仅支持 NX2406；.NET Framework 4.8.1（2026-09-05 由 4.8 提升——本机无 v4.8 targeting pack、
> 官方 4.8 Dev Pack 安装器在沙箱挂起、4.8.1 pack 已就位且运行时 4.8.1；语义兼容 4.8 代码）；
> 代码全部在本目录（sln 在仓库根 `Autocam.Plugins.sln`）。

## 当前状态（2026-09-05）：实证收官——v1 三步闭环 + v1.5-①③④ 参数面扩展 + STEP 资产收口（索引 §3 全划勾）

- `NXPlugins.csproj`：类库工程，已引用 NXOpen / NXOpen.UF / NXOpen.Utilities
  （HintPath 指向 `$(NX_DIR)\NXBIN\managed\`，默认
  `C:\Program Files\Siemens\NX2406`；`Private=False`，**NXOpen 程序集不随仓库分发**）。
  ✅ **生产代码已全部纳入 csproj**（Journal\*、PlanExporter\*、PlanExecutor\*；测试目录不入库，
  走 scripts/run-unittests.ps1 红线回归）——sln 构建 = 设计 §7 步骤 4 完成。
- `Properties/AssemblyInfo.cs`：装配元数据（初始骨架，v0.1.0）。
- `Journal/`：探针/工具 journal 18 个（步骤 0 实证收官 + 收官批 `CamProbeFinalize`/`CamProbeExecutor` +
  U-6 收口 `CamProbeStepover` + 键集 `CamProbeParams(-2)` + STEP 链 `CamProbeStepRebuild`/
  `CamProbeStepExport`（09-05 资产收口，见下）全通，结论回填 docs/nx2406-install-index.md §2.1/§3）；
  `ExporterAdapter.cs` / `ExecutorAdapter.cs` = 导出/重建 [I] 层适配器（test.prt → test.plan.json →
  test.rebuilt-*.prt 闭环跑通）。
- **2026-09-05 STEP 资产收口（索引 §3 项 6 划勾）**：导入（官方 sim_final2.stp 就地引用 →
  1 body/31 面 α）+ 导出（ugstep214.def 导出向修正 → samples/test.step，回导 1/26 = 源件一致）
  批处理实证闭环，v2 前置齐备（证据：samples/camprobe-steprebuild-012104*、camprobe-stepexport-012205*）。
- `PlanExporter/` + `PlanExecutor/` + `PlanComparer/`：纯逻辑核心（spec 各落档；[U] 红线 93/93 全绿
  ——v1.5-③ 全量回归；含 U-7 A′ 词集与 Comparer 全维比对：CompareCore 双快照 diff，
  见 docs/nx-plan-comparer-spec.md）；`PlanExporterTests/`/`PlanExecutorTests/`/`PlanComparerTests/`
  测试目录不入库编译。
- 合编脚本：`scripts/compile-executor-adapter.ps1`（重建 exe）与 `scripts/compile-exporter-adapter.ps1`
  （导出 exe，U-7 新增，镜像前者）→ .claude/tmp/*.exe 供 NX File → Execute。

## 规划目录（按 nx-plugin-design.md §7 步骤 0-4 进度）

```
Journal/            ✅ CamProbe×15 + 工具 journal×3 + ExporterAdapter/ExecutorAdapter [I] 适配器
PlanExporter/       ✅ [U]+[I] 闭环（spec 落档）
PlanExecutor/       ✅ [U] 33/33 + [I] 集成闭环（spec 落档；参考官方样例
                    %NX_DIR%\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport）
PlanParser/         ⛔ 不独立实现（复用 PlanExporter 的 PlanDocument/PlanJsonSerializer，
                    executor spec §1/§6）
FaceResolver/       🔧 v2 候选（U-5/U-5c 结案：面级锚点无生产源；区域级 CutRegionsData 增强候选，
                    设计模块表同口径）
PlanComparer/       ✅ [U]+[I] 闭环（spec 落档 2026-09-04；v1 终跑 comparer-run-144237 issues=6、
                    v1.5-③ 终跑 comparer-run-200339 issues=5 均与校准清单逐条一致）——设计 §7 步骤 3
                    收官，三步闭环 v1 + v1.5-①③④ 参数面扩展完成
```

## 实证收官注记（2026-09-04）

- 索引 §3 待验证清单全部 ★ 结案（收官批探针 CamProbeFinalize + U-6 收口 CamProbeStepover 源文件 +
  证据档 samples/camprobe-finalize-20260904-010401.txt + camprobe-stepover-20260904-{152830,153003,153051}.txt）。
- U-6 已负结案收口（2026-09-04，docs/nx-stepover-probe-spec.md：8 通道形态全负，公开 .NET 面无
  stepover 有效写入通道 → 索引 §3 唯一 [T] 清零，重建侧维持拒收 + diag）。
- U-1 维持（白名单 + diag 决议）。
- 运行纪律（含批处理 CAM 会话初始化顺序）见 docs/nx2406-install-index.md §2.1。

## 构建

- 打开仓库根 `Autocam.Plugins.sln`（Visual Studio，.NET Framework 4.8 工作负载）。
- 换机/换 NX 目录：设环境变量 `NX_DIR` 或在 csproj 覆盖 `<NX_DIR>`。
- 勿把 `NXOpen*.dll` 等西门子程序集提交进 git（`Private=False` 已保证引用不复制）。
- ✅ **sln 构建门已过（2026-09-05，MSBuild VS2022）**：csproj 目标框架提升 v4.8.1（winget
  Dev Pack 4.8.1 提供 v4.8.1 targeting；本机无 v4.8 pack）并补 `System.Runtime.Serialization`
  引用（DataContractJsonSerializer 依赖——csproj 原缺，sln 首建暴露）。构建产物
  `src/NXPlugins/bin/Debug/NXPlugins.dll`（仅既有 CS0618 弃用警告，探针代码、非阻塞）。

## 运行（验证入口，2026-09-04 实证）

- 批处理：`"C:\Program Files\Siemens\NX2406\NXBIN\run_journal.exe" src\NXPlugins\Journal\<探针>.cs`
  —— 无界面直接执行 journal 源文件（帮助用法**无 `-nogui` 旗标**）。含 CAM 会话操作须按索引 §2.1
  纪律：先 `NewDisplay` 建件 → `Session.CreateCamSession()` → `CreateCamSetup`。
- 交互：NX 会话 File → Execute → NX Open（csc 预编译 exe，历史探针/适配器路径）。
