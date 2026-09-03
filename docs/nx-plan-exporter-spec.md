# PlanExporter 规格（spec-before-code 纪要落档，2026-09-03）

> 状态：已落档；实现以本文性质表为红线基准（第 6 步单测骨架按编号引用）。
> 需求源：docs/nx-plugin-design.md §7 步骤 1 / §2.1；合同：schema/autocam-plan.schema.json v3.0；
> API 事实源：docs/nx2406-install-index.md（§2.1/§2.4/§2.5/§3）；几何实证：samples/camprobe-geom.txt（U-5）。

## 0. 一段话结论

PlanExporter 是「许可 gate → 只读单遍扫描（Tag 去重）→ 分型 Builder 生效值回读 → schema v3 校验 →
原子落盘」的 NX 会话内转换器。参数回读可行性已实证（FloorStock 生效值可读）；风险重心在契约层面：
nx_template 细分模板类型无法程序化读回（白名单 + 歧义降级，U-1）；面级锚点数值契约（质心+面积）无生产源
（U-5 结案）→ 首版 features 走组级 + anchor 兜底。运行载体：Journal（csc 编译 exe → Execute → NX Open，
复用 src/NXPlugins/Journal 纪律）。

## 1. 协议（外部边界）

| 项 | 约定 |
|---|---|
| 调用序列 | NX 会话 exe 入口 → `OpenDisplay(prt)+SetWork`（空会话纪律）→ cam_base 许可 gate（`LicenseManager.Reserve`）→ 结构扫描（四根组）→ 逐 Operation 读回 → 内存组装 plan → schema 校验 → `.tmp` 写入 + rename 原子落盘 → 退出 |
| 输入 | 手编 prj（.prt，含 CAMSetup 与工序），如 samples/test.prt |
| 输出 | plan.json（schema v3 校验通过）；`contract_version=3.0` 写死 |
| 失败语义 | 结构级（无 CAMSetup/许可不可用/无显示部件）→ 中止、不落盘（POST-5）；工序/字段级 → diagnostics + 继续（POST-3） |
| 只读纪律 | 全流程不 Commit/不修改/不保存 NX 对象；回读 Builder 用毕 Destroy（MONO-1） |
| 版本兼容 | 本版本不改 schema 结构；仅注释/示例按实证修正（CONFLICT-1 已落地） |

## 2. 数据结构要点

- 顶层照 schema v3：plan_id/name/input_ref/meta/setups[]/resources.tools[]/features[]/operations[]/
  workingsteps[]/workplan/diagnostics[]。
- `nx_template = {type, subtype}`：**type=模板部件名、subtype=对象模板类型**（如 mill_contour/CAVITY_MILL，
  成对直供 Create）。CONFLICT-1 已修（schema $comment/字段描述/minimal 示例）。
- features[] 首版条目：1 workingstep → 1 feature（feature_type=geometry_group），geometry_ref 仅
  anchor 兜底，**face_anchors 留空数组** + diagnostic（U-5 结案，见 §5）。
- ref 五类闭合（tool/method/setup/feature/operation ↔ workingstep）。
- 数值：double 原样 round-trip（JSON number，不格式化截断）（POST-4）。

## 3. 性质（红线；[U]=离线单测硬红线 [I]=集成验证 [T]=待实测）

| 编号 | 断言 | 依据 | 判据 | 层级 |
|---|---|---|---|---|
| PRE-1 | 入口要求：部件打开为显示+工作、CAMSetup 非空，否则拒绝并中止 | 空会话取件纪律（实证） | 空引用/空 setup → 明确错误返回 | [U]（gate 替身）+[I] |
| PRE-2 | cam_base 许可可 Reserve 才继续 | 许可探测配方（实证） | Reserve 失败 → 中止报许可错误 | [U]（替身） |
| PRE-3 | 模板白名单对表（CAMSession 枚举快照）加载成功 | 枚举实证 | 空表 → 中止 | [U] |
| POST-1 | 成功产出 plan.json 通过 schema 校验（含落盘后复验） | 设计定义 | 校验器对夹具全过 | [U] |
| POST-2 | 失败不产生半成品；旧文件不被破坏（.tmp+rename） | 设计约束 | 注入失败 → 目标文件保持原样 | [U] |
| POST-3 | 字段/工序级回读失败 → diagnostics 记录，不静默缺字段 | 实证口级纪律 | 替身抛错 → diag 含 code+level+所属 op | [U] |
| POST-4 | 数值原样保真（double round-trip 无损） | 设计约束 | 序列化→反序列化 == 原值 | [U] |
| POST-5 | 结构级失败（PRE-1/2/3 不满足）→ 中止且不落盘 | 协议定义 | 注入失败 → 无输出文件 | [U] |
| INV-1 | 任意产出（含最终文件）schema 合法 | 合同强制 | 夹具×结构变体全过 | [U] |
| INV-2 | ref 闭合：tool_ref/method_ref/setup_ref/feature_ref/operation_ref 指向存在 id | 合同定义 | 夹具引用完整性 | [U] |
| INV-3 | 1 operation ↔ ≤1 workingstep；workingstep.operation_ref 回指存在 | 决策⑤ 1:1 口径 | 同上 | [U] |
| INV-4 | 每 operation 带四父链信息；父名缺失 → diag warning | 导出设计 | 替身缺父 → warning 非整条丢弃 | [U] |
| INV-5 | Tag 去重：同一操作在四视图树各出现一次，plan 内恰一条 | 实证（dump） | 替身多视图同 Tag → 1 条 | [U] |
| INV-6 | diagnostics：同类同 op 聚合一次，error 级对应具体缺失 | 工程纪律 | 重复场景 → 单条 | [U] |
| MONO-1 | 单遍只读：不 Commit/不修改/不保存；回读 Builder 用毕 Destroy | 导出定义 | 评审 + 集成冒烟 | [I]（人工） |
| MONO-2 | 组树遍历终止（树有界无环） | 定义 | 环状/深树替身不挂死 | [U] |
| POST-6 | 歧义工序（同大类多子类型，PTP 家族）→ nx_template 默认对 + diag W | U-1 决议 | 替身 PTP → (hole_making, DRILLING)+W | [U] |

## 4. 算法（步骤 → 性质映射）

A1 打开取件（OpenDisplay+SetWork）→ PRE-1
A2 许可 gate（Reserve）→ PRE-2/POST-5
A3 四根组扫描 + Tag 去重收集（程序顺序树为序）→ INV-5/MONO-2
A4 逐 op 元信息：Name/UserName、四父链 → INV-4
A5 nx_template 白名单匹配（表：模板对，见 A3 收集的 GetNameOfType 大类 → 候选对）→ POST-6/PRE-3
A6 分型 Builder 回读：形态注册表（成员路径+形态类）驱动 → strategy/technology 字段；回读失败 → diag
   → POST-3/POST-4；`InheritanceStatus` 标记（bool 语义待实测）暂不输出（增强候选）
A7 刀具组（NCGroup→Builder 读回直径/刃数等 MVP 字段）/ MCS（MillOrientGeomBuilder.Mcs 属性）/
   方法组名 → resources/setups/method_ref → [I]
A8 workingsteps 1:1 生成；COPY 链展开为独立 operation 条目（name 保留 _COPY 后缀链），diag 记副本关系
   → INV-3（决策②口径：展开 + diag）
A9 features：组级存在性（几何父组链）+ anchor 兜底；face_anchors 空 → 见 U-5
A10 JSON 序列化（double 原样）→ POST-4
A11 schema 校验 → INV-1/POST-1
A12 `.tmp` 写入 + rename；diagnostics 汇总写回 → POST-2/POST-5/INV-6

## 5. 实证结案与未决

- **U-5 结案**（camprobe-geom.txt）：腔铣面级可枚举（CutAreaGeometry → 13 Face + UF 类型/点/法向），
  但 NXOpen.Face / UFModl **无面质心/面积 API** → face_anchors 数值契约无生产源 → 首版不导出（POST/§2 已含）；
  区域级通道（CutRegionsData 质心+面积真值，仅腔铣）记为增强候选。
- **U-5b**（离线归档）：Face/UFModl 反射负结果（2026-09-03）；孔工序 PTP 旧模板面通道存储未知 → 继续未决。
- **U-5c**（[T]）：`UFModl.AskMassProps3d(Tag[]{face}, …)` 对 face 对象能否给出面积/质心——唯一存活候选，单独实测。
- **U-1**（[T] 残余）：操作细分模板对若能经内部模板属性读回则精修；当前决议 = 白名单 + PTP 默认
  (hole_making, DRILLING) + W diagnostic（打点类定心钻语义偏差以 diag 记录，不静默）。
- **U-3**（[T]）：`InheritableBuilder.InheritanceStatus : bool`（2406 反射实证存在）布尔语义待运行验证；
  首版导出生效值、不打显式/继承标。
- **U-4**（[I]）：刀具/MCS 参数回读集成验证（A7）。
- **决策②**：COPY 链展开为独立条目 + diag（不引入 schema 副本字段）。
- **决策③**：导出生效值（实测可读）；显式/继承打标后置。
- **决策⑤ 冲突已决**：几何首版砍至组级（CONFLICT-2，理由见 U-5 结案）。

## 6. 与设计文档差异提示

- nx-plugin-design.md §2.1 导出行"按 NX Tag → 几何属性锚点（FaceResolver 反向）"受 U-5 限制：
  首版不可完整实现（面级锚点无质心/面积生产源）；待 U-5c 或区域级增强后回补。该处后续随 Exporter
  实现进展修订。
- nxopen-research.md §4.6/§5 风险 1 同理：FaceResolver 0.01mm 质心匹配协议的"导出侧"源待 U-5 后续。
