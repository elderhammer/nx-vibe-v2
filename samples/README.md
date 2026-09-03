# samples — 测试资产（Plan 双向验证闭环）

> 状态盘点（2026-09-03，按 docs/nx2406-install-index.md §1 检索）：
> NX2406 安装目录**没有**现成的手编 ground truth（含 CAMSetup 工序的 .prt）与 STEP 样例；
> 只有两类可用素材（见下）。决策③：**首件由用户提供（samples/test.prt，内容待 NX 会话
> 确认）**，其余自建件由 NX2406 会话手编后入库。

## NX 安装目录可参考素材（**不提交 vendor 文件**，仅引用绝对路径）

| 素材 | 位置 | 用途 |
|---|---|---|
| CAM 模板部件 | `C:\Program Files\Siemens\NX2406\mach\resource\template_part\metric\`（`mill_contour.prt` / `mill_planar.prt` / `drill.prt` / `MillTurn_Exp.prt` / `cam_metric_template.prt`…） | `Part.CreateCamSetup(templateName)` 初始化实验、重建侧"打开+建组"链路冒烟 |
| 几何样件（无 CAM） | `C:\Program Files\Siemens\NX2406\UGOPEN\glass.prt` / `facetted_hood.prt` 等 | 纯几何读取/面属性遍历实验（FaceResolver 前置） |
| 官方 C# 参考实现 | `C:\Program Files\Siemens\NX2406\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport\` | PlanExecutor 建组/导库范式（代码级参考，不拷贝文件） |

## 需要自建的资产（本目录将来入库）

| 文件 | 内容 | 用途 |
|---|---|---|
| `test.prt`（**已入库**） | ground truth 首件（用户提供，2026-09-03；OLE2 容器，NX 私有格式，内容与版本待 NX 会话确认——预期含完整 CAMSetup 与工序） | 步骤①导出、③对比的基准；**开工第 0 步先用 NX2406 打开确认**：可读性/版本/含哪些工序与几何 |
| `test.step`（计划） | 同件 STEP 导出 | 步骤②"打开原始 STEP"重建路径 |
| `test.plan.json`（计划） | 由 test.prt 导出的 plan（schema 校验 + 回归基线） | 合同冒烟/对比基准 |

> 注意：西门子安装目录内文件（模板/样例/程序集）受许可约束，**只引用、不复制进 git**；
> 自建件由本仓库维护。
