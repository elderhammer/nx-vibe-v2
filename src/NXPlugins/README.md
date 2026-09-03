# NXPlugins — NX 2406 插件骨架

> 定位：Plan 双向验证闭环（导出 → 按 plan 重建 → 对比）的 NX 侧实现载体。
> 需求与设计：`docs/nx-plugin-design.md`；API 事实源：`docs/nx2406-install-index.md`；
> 合同：`schema/autocam-plan.schema.json`（v3.0）。
> 工程决策（2026-09-03，见 nx-plugin-design.md 头部"已确认决策"）：
> 仅支持 NX2406；.NET Framework 4.8；代码全部在本目录（sln 在仓库根 `Autocam.Plugins.sln`）。

## 当前状态：仅工程骨架

- `NXPlugins.csproj`：类库工程，已引用 NXOpen / NXOpen.UF / NXOpen.Utilities
  （HintPath 指向 `$(NX_DIR)\NXBIN\managed\`，默认
  `C:\Program Files\Siemens\NX2406`；`Private=False`，**NXOpen 程序集不随仓库分发**）。
- `Properties/AssemblyInfo.cs`：装配元数据（初始骨架，v0.1.0）。

## 规划目录（按 nx-plugin-design.md §7 步骤 0-4 逐步填充）

```
Journal/            Journal 入口（run_journal.exe 可执行类 / INXAddIn 宿主）
PlanExporter/       导出：组树遍历 + Builder 回读 → plan.json（步骤 1）
PlanParser/         plan.json → 强类型模型（对齐 schema v3）
PlanExecutor/       重建：CAMSetup/四组/逐工序创建（步骤 2；参考官方样例
                    %NX_DIR%\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport）
FaceResolver/       face_anchors 属性匹配 → NX Tag（容差 0.01mm）
PlanComparer/       偏差表 + 汇总评分（步骤 3；容差按决策④先默认后校准）
```

## 开工前置（步骤 0，见 nx2406-install-index.md §3 待验证清单）

1. 本机跑通最小 journal：组创建 → 操作创建 → 参数读写 → 刀路生成（沉淀"API 形态基表"）。
2. 实测项：typeName/组模板字面量、`CreateCamSetup("mill_contour")`、Stepover 链路、
   `run_journal.exe -nogui`、`CAMSetup.View.MachineMethod` 对应、工序关联几何读取可行性。
3. 确认本机功能许可清单（前置许可检查基线）。

## 构建

- 打开仓库根 `Autocam.Plugins.sln`（Visual Studio，.NET Framework 4.8 工作负载）。
- 换机/换 NX 目录：设环境变量 `NX_DIR` 或在 csproj 覆盖 `<NX_DIR>`。
- 勿把 `NXOpen*.dll` 等西门子程序集提交进 git（`Private=False` 已保证引用不复制）。
