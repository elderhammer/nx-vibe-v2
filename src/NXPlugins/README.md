# NXPlugins — NX 2406 插件骨架

> 定位：Plan 双向验证闭环（导出 → 按 plan 重建 → 对比）的 NX 侧实现载体。
> 需求与设计：`docs/nx-plugin-design.md`；API 事实源：`docs/nx2406-install-index.md`；
> 合同：`schema/autocam-plan.schema.json`（v3.0；2026-09-06 反推修正——schema 为协议规范面，
> 形状真值经 jsonschema 引擎对真实产物核验通过，语义镜像 = PlanValidator + 参数键集注册表，
> 两面同批收敛纪律见 schema 头注约定 10 与 CLAUDE.md 规则 5）。
## 实现策略（六点，2026-09-06 收口成文）

1. **锁定 NX 2406**：仅支持 NX2406、不做旧版本兼容（版本策略决策②，见
   docs/nx-plugin-design.md 头部）；NX API 面随版本漂移的事实由 2 的索引注记承担。
2. **接口事实索引**：为 NX2406 安装资料建立接口事实索引 docs/nx2406-install-index.md——
   以 NXOpen.xml 成员清单/remarks（"Created in NXxxxx"/License）、`UGOPEN\NXOpen\*.hxx`
   头文件注释（含废弃/替换注记）、UGOPEN 官方样例库（写面范式）三语料为源，并维护
   "不存在项"清单与属性取值四形态速查；三路查证协议与负结论证伪纪律见
   `.claude/skills/nx-api-verify`（§1.5）。
3. **需求文档随索引修正**：docs/ 设计/规格文档中的 API 细节以索引（及 nxopen-research
   附 A）为准，发现索引过期立即修正（CLAUDE.md 规则 4）；禁止凭记忆写 API。
4. **分阶段实现**：按 docs/nx-plugin-design.md §7 步骤 0-4 落地（API 形态基表 → PlanExporter
   → PlanExecutor 重建 → PlanComparer → 并入 sln）；每阶段收口 = spec-before-code 纪要落档
   （性质红线）→ [U] 单测骨架红占位 → 实现全绿 → [I] NX 会话实录验收（samples/ 证据档）。
5. **先审查索引、必要时探针定行为**：每个设计/实现改动先对照接口事实索引审查；索引未覆盖、
   与旧文献冲突、或需精确签名/枚举/行为 → 写 CamProbe* journal 探针实测 NX2406 真实行为
   （属性取值形态、可写性、持久性、引擎消费、会话纪律），以实证修正设计后再实现——U-1..U-7
   结案与 v2/v2.5 判别链（γ 五候选、方案 B 补漏键）均为该路线的产物与先例。
6. **schema 是契约、与实现相辅相成**：schema/autocam-plan.schema.json = 协议规范面（设计
   基准），执行面 = Doc.cs/PlanJsonSerializer（形状）+ PlanValidator（语义镜像）+ 参数键集
   注册表（docs/nx-param-registry-spec.md，键语义权威）；设计新增从规范面发起、实证纠正从
   执行面发起，任一面变更必须两面同批收敛（schema 头注约定 10 / CLAUDE.md 规则 5）。

> 工程决策（2026-09-03，见 nx-plugin-design.md 头部"已确认决策"）：
> 仅支持 NX2406；.NET Framework 4.8.1（2026-09-05 由 4.8 提升——本机无 v4.8 targeting pack、
> 官方 4.8 Dev Pack 安装器在沙箱挂起、4.8.1 pack 已就位且运行时 4.8.1；语义兼容 4.8 代码）；
> 代码全部在本目录（sln 在仓库根 `Autocam.Plugins.sln`）。

## 当前状态（2026-09-06 收口）：实证收官——v1 三步闭环 + 参数面扩展（v1.5-①③④⑤）+ **v2 几何
重建一体收官**（[nx-v2-geom-spec.md](../docs/nx-v2-geom-spec.md)：STEP 导入→签名面指派→带几何
刀路→Comparer 三维 + 腔铣维 gate；[I] 190859/…/192456 issues=21=预测、sigfaceset=4/4 验收关闭）
+ **v2.5 三批（区域配对 RegionPairing / reference_tool #17 / transfer_within_levels #18 + 深度键
修正）**：校准复跑 003738/004123/140734/143758，**issues 21→9**（param=55/55、tool=6/6、mcs=1/1、
fixture=1/1、template=6/6、sigfaceset=4/4，全部可解释）+ **残余归因收口（2026-09-06 两批判别）**：
γ 五候选开关闭合（判别⑧，4a51cc2：E1/E2 零消费、E3 Reversed 写不持久、ExtractCutArea 活性实证、
E5v2 区组通道零）→ OP-003 γ 定档引擎内部行为；方案 B 判别⑨ 关闭（be03deb：七补漏键写持久但
引擎逐位零消费 → B3=公开 Ncm 键面复刻上限、实施批取消）→ **残余 9 条全部为"已解释 + 无可写
通道"永久校准性质**（OP-001 stepover ×3 / OP-002 转移高度内部值 ×2 / OP-003 γ ×4，清单见
[nx-plan-comparer-spec.md](../docs/nx-plan-comparer-spec.md) §3 校准记录）。[U] 全量 **118/118**
（2026-09-06 审计批 +2：POST-2 真实文件原子替换 + INV-4 正向去重）。

- `NXPlugins.csproj`：类库工程，已引用 NXOpen / NXOpen.UF / NXOpen.Utilities
  （HintPath 指向 `$(NX_DIR)\NXBIN\managed\`，默认
  `C:\Program Files\Siemens\NX2406`；`Private=False`，**NXOpen 程序集不随仓库分发**）。
  ✅ **生产代码已全部纳入 csproj**（Journal\*、PlanExporter\*、PlanExecutor\*；测试目录不入库，
  走 scripts/run-unittests.ps1 红线回归）——sln 构建 = 设计 §7 步骤 4 完成。
- `Properties/AssemblyInfo.cs`：装配元数据（初始骨架，v0.1.0）。
- `Journal/`：探针/工具 journal 49 个（42 × `CamProbe*`：U-6 `CamProbeStepover`、键集
  `CamProbeParams(-2)`、STEP 链 `CamProbeStepRebuild`/`CamProbeStepExport`、v2 面签名/几何
  `CamProbeV2Geom`/`CamProbeV2Gt`/`CamProbeFaceSig`/`CamProbeV2OpDiag`、OP-003 判别链
  `CamProbeV2{Regen,BpDiff,Surf,Fix,Flip,Sel,Body,ApiClone,RegionClone,RegionFull,SurfDiff,
  RefTool,Ncm,NcmH}`、γ 五候选 `CamProbeV2GammaCands`（判别⑧）、Ncm 补漏键 `CamProbeV2NcmGap`
  （判别⑨）、写面探针 `CamProbeFeedCut`（v1.5-⑤）+ `CamWriteProbe`/`SmokeOpen`/`DumpCamSetup`
  + `NxCollect`（共享采集），全通，结论回填 docs/nx2406-install-index.md §2.1/§3 与各 spec）；
  `ExporterAdapter.cs` / `ExecutorAdapter.cs` / `ComparerAdapter.cs` = 导出/重建/对比 [I] 层适配器
  （test.prt → test.plan.json → test.rebuilt-*.prt / v2.rebuilt-*.prt 闭环跑通；ComparerAdapter
  v2 版含 B 防呆自动选最新 v2.rebuilt 与 CompareV2 维 gate 渲染，da3fd80/2530c6d）。
- **2026-09-05 STEP 资产收口（索引 §3 项 6 划勾）**：导入（官方 sim_final2.stp 就地引用 →
  1 body/31 面 α）+ 导出（ugstep214.def 导出向修正 → samples/test.step，回导 1/26 = 源件一致）
  批处理实证闭环，v2 前置齐备（证据：samples/camprobe-steprebuild-012104*、camprobe-stepexport-012205*）。
- `PlanExporter/` + `PlanExecutor/` + `PlanComparer/`：纯逻辑核心（spec 各落档；[U] 红线 **118/118**
  全绿——历次全量回归含 U-7 A′ 词集、V15 union 值通道、CompareCore 双快照 diff、CompareV2
  三维/门控与 **RegionPairing 分层配对 12 条**（v2.5），见 docs/nx-plan-comparer-spec.md 与
  docs/nx-v2-geom-spec.md）；`PlanExporterTests/`/`PlanExecutorTests/`/`PlanComparerTests/`
  测试目录不入库编译（scripts/run-unittests.ps1 红线回归）。
- 合编脚本：`scripts/compile-executor-adapter.ps1`（重建 exe）、`scripts/compile-exporter-adapter.ps1`
  （导出 exe，U-7 新增，镜像前者）与 `scripts/compile-comparer-adapter.ps1`（对比 exe）→
  .claude/tmp/*.exe 供 NX File → Execute。

## 规划目录（按 nx-plugin-design.md §7 步骤 0-4 进度）

```
Journal/            ✅ CamProbe×42 + NxCollect/工具×3 + ExporterAdapter/ExecutorAdapter/ComparerAdapter
                    [I] 适配器（v2 面签名/几何、γ 判别⑧ 五候选、方案 B 判别⑨ 补漏键系列探针在内）
PlanExporter/       ✅ [U]+[I] 闭环（spec 落档；v2 I-1 重导带签名 adapter-run-190859）
PlanExecutor/       ✅ v1 [U] 33/33 + [I] 集成闭环（spec 落档；参考官方样例
                    %NX_DIR%\UGOPEN\SampleNXOpenApplications\DotNet\CAMSetupImport）
                    + v2 带几何重建收官（[I] 191434 ok=19/fail=0）
                    + v2.5 键批（深度键 CutLevel 子树/#17 reference_tool/#18 transfer_within_levels，
                    最新 executor-run-143712 ok=19/fail=0）——OP-003 空刀路 = γ 引擎内部行为
                    定档（判别⑦/⑧，见 nx-v2-geom-spec.md §7，executor 不改）
PlanParser/         ⛔ 不独立实现（复用 PlanExporter 的 PlanDocument/PlanJsonSerializer，
                    executor spec §1/§6）
FaceResolver/       🔧 → 被 v2 签名通道替代（F1 13/13 唯一命中，2026-09-05；U-5 负结案维持）；
                    区域级配对 = v2.5 已实现（RegionPairing.cs 分层配对 + 区域维诊断化，
                    2026-09-06，见 comparer spec §3 校准记录）——本组件不再推进
PlanComparer/       ✅ [U]+[I] 闭环（spec 落档 2026-09-04；v1 终跑 144237 issues=6、v1.5-③
                    200339 issues=5、v2 三维 gate 192456 issues=21=预测、v2.5 复跑 004123/140734/
                    143758 **issues=9 全部已解释** + 区域维诊断化（差层面积占比 + A-only 位置））
                    ——设计 §7 步骤 0-4 全收官：v1 + v1.5 参数面扩展 + v2 几何重建 + v2.5
                    区域配对/键批一体交付完成；残余 9 条均为永久校准性质（清单见 comparer
                    spec §3）
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
