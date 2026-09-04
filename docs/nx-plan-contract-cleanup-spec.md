# plan 合同残留清理规格（D-4，spec-before-code 纪要落档，2026-09-04）

> 状态：**纪要落档（2026-09-04）**；范围决策 = operation_type/feature_type 走 **X（自由串两档开放）**、
> geometry_ref/blank/machines 走 **i（全面删）**；索引回填三项必做。实现按本文件性质表为红线。
> 需求源：docs/nx-tool-type-enum-spec.md §7（第二批清单）+ 2026-09-04 审查（云端 CAPP 表述 vs 实证）；
> 合同：schema/autocam-plan.schema.json v3.0（本批 = 枚举去断言/字段删除，结构收窄，contract_version 维持 3.0）。
> 事实源：nx2406-install-index.md §2.1/§2.5 + NX2406 安装资料源码（uf_modl.h、NXOpen.xml、CAMSetupImport 样例）。

## 0. 一段话结论

D-4 = 把 schema 中"NX2406 事实不可生产、仓库无消费者、注释与实证脱节"的 CAPP/云端押注结构清到与事实一致：
operation_type 33 细类词与 feature_type 17 AP224 词**取消枚举断言**（改自由串 + 两档描述——保留概念与未来
CAPP/识别词汇能力，但不押注任何词表）；geometry_ref 整块（face_anchors/face_ids/edge_ids/anchor_point）与
machines/machine 定义/setup.machine_ref/blank_ref **删除**（面级契约被 uf_modl.h body-only 注记与 NXOpen.Face
零成员双重否定；机床=库装载、prt 无导出内容；schema 落点引用了不存在的 `MillGeomBuilder.Blank`）。
feature 条目保留 {feature_id, feature_type, params}（ws.feature_ref 闭合与 1:1 结构不破）。产出文件形状变化
→ 重导 test.plan.json + Executor I-2 复跑回归。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 输入/输出 | plan.json 结构收窄：operation_type/feature_type 值域从枚举变自由串（语义两档见下）；features[] 条目去 geometry_ref；resources 去 machines；setup 去 machine_ref/blank_ref |
| operation_type 语义（两档） | NX 导出侧产粗类语义值（milling/drilling/other，FamilyToOperationType 现状）；外部 CAPP 素材挂载后可产细类词（如 mill_cavity）并 additive 恢复枚举——本批不断言任一档 |
| feature_type 语义（两档） | NX 导出侧恒 "geometry_group"（组级口径）；识别模块挂载后可补 AP224 细类——本批不断言 |
| 失败语义/调用序列 | 不变（导出/导入两侧消费字段均为现状子集，无新增依赖） |
| 版本兼容 | 删除字段对现有消费者零影响（Executor 不消费；validator 无相关检查；DataContract 反序列化对多余 JSON 键宽松）→ contract_version 维持 3.0；旧 test.plan.json 由重导替换 |

## 2. 数据结构要点

- feature = {feature_id(required), feature_type(自由串，默认 geometry_group), params(自由对象，恒空)}；
  geometry_ref 定义整体移除（含 face_anchor def）。
- operation 去 33 词枚举（字段/required/nx_template 不动）。
- resources = {tools(required)}；machine 定义、setup.machine_ref/blank_ref 移除。
- schema 大 $comment：约定 4)（face_anchors/双路径）删除改写；5)（feature_type 本地口径）按自由串两档重述。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| C1-INV-1 | operation_type/feature_type 无枚举断言（自由串），产出恒合法 | X 决策（两档描述） | schema 无 enum 键 + 描述含两档 | [U] |
| C1-INV-2 | 产出 JSON 不含 geometry_ref/machines/machine_ref/blank_ref（序列化层同步，无孤儿字段） | i 决策 | 重导文件 grep 零命中 | [U]+[I] |
| C1-INV-3 | ws↔feature 1:1 与 ref 闭合保持（feature 条目 = id+type+params 仍在） | 设计决策⑤/INV-1/INV-2 | validator 夹具全过 | [U] |
| C1-INV-4 | feature_type 缺省写 "geometry_group"（导出语义不变） | 组级口径 | 序列化默认断言 | [U] |
| C1-POST-1 | 重导 plan 通过 validator + 落盘复验；Executor I-2 复跑全 PASS | 集成回归 | 复验日志 | [I] |
| C1-POST-2 | schema 无 MillGeomBuilder.Blank 引用（不存在成员出清） | NX 源码事实（XML 成员清单） | schema grep 零命中 | [U] |
| C1-POST-3 | 索引回填：Blank 不存在项、uf_modl.h:4324 body-only + Face 零成员入 U-5 链、Blank→BlankGeometry 修正 | CLAUDE.md 回填规则 | diff 审阅 | [doc] |

## 4. 算法/改动面（步骤 → 性质映射）

1. schema：op/feature 枚举→自由串+描述（C1-INV-1）；删 geometry_ref/face_anchor/machine def/machines/
   machine_ref/blank_ref + 注释改写（C1-INV-2/POST-2）
2. Doc.cs：删 GeometryRefJson、FeatureJson.geometry_ref、ResourcesJson.machines（C1-INV-2）；ExporterCore 删
   anchor 兜底两行（C1-INV-2）；Model/Validator 零改动
3. plan.minimal.json 重写 v1 实态（C1-INV-3/4）
4. 单测：现有 [U] 全量回归 + 新增 C1-* 断言夹具（C1 系）
5. 集成：ExporterAdapter 重导 test.plan.json（落盘复验）+ ExecutorAdapter I-2 复跑（C1-POST-1）
6. 索引 §2.5/§2.1 回填三项 + 文档同步（design §5、exporter spec §2/§5、executor spec §1、research §4.6/4.7 指针）
   终止性：有限静态面，无循环新增。

## 5. 决策与证据（NX2406 安装资料源码落点）

- **D-5（operation_type/feature_type 值域）= X**：枚举→自由串 + 两档描述。依据：33 词/17 词无 NX 生产出处
  （细分类型无读回，索引 §2.1）；但词表**可能**由未来 CAPP/识别侧产出 → 不删概念、不断言词表（与 D-3 同原则）。
- **D-6（geometry/machine 结构）= i 全面删**。证据链：
  - face 面级：`UGOPEN\uf_modl.h:4322-4326` `UF_MODL_ask_mass_props_3d` 参数注记 = "solid or sheet body
    identifiers"（不含 face）；`NXOpen.Face` 成员清单零命中 Area/Centroid/Mass/Measure（NXOpen.xml）；
    NX 会话负证据（camprobe-finalize S6，Unknown feature type）。
  - blank_ref：`NXOpen.CAM.MillGeomBuilder` 成员 = BlankGeometry/CheckGeometry/PartGeometry（**无 Blank**；
    BlankGeometry Created NX8.0 / License None）→ schema 落点引用不存在成员（C1-POST-2 必清）。
  - machines：官方样例 `SetupPartExtensions.cs:24-46` LoadMachine = CreateMachineGroupBuilder→RemoveMachine→
    **库装载**（libRef 驱动，缺库抛 NotSupportedException）；test.prt 机床 = 模板默认 GENERIC_MACHINE
    （test.camdump.txt:47），无 libRef 可导出。
  - 消费者面（源码实证）：ExecutorCore ref 闭合只查 tool/setup/op（不含 features 内容）；features 唯一消费 =
    导出侧 validator ws.feature_ref 闭合 → feature 保留条目即可。
- **必做回填**（不论结构处置）：① `MillGeomBuilder.Blank` 入索引 §2.5 不存在项；② uf_modl.h:4324 body-only
  注记 + Face 零成员入 U-5 证据链（§2.1/§2.5）；③ schema/文档 Blank 落点修正为 BlankGeometry（随字段删除已自然达成）。

## 6. 不在本批范围

Comparer（设计 §7 步骤 3，宜在其 spec 落维度）；research §4 对接建议正文逐行修订（横幅兜底已立）。
（2026-09-04 跟进：U-7 A′ 实现批与 PlanValidator 枚举收紧已在本批后完成收官，见 nx-tool-type-enum-spec.md 头部。）
