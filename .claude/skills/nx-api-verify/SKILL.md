---
name: nx-api-verify
description: 在本机 NX2406 安装资料中实证 NXOpen API（类型/成员/枚举取值/签名/许可/版本注记），四路交叉：NXBIN\managed\NXOpen.xml 成员清单、PowerShell 反射 NXOpen.dll、UGOPEN\NXOpen C++ 头文件、UGOPEN 官方样例库。当任务涉及 NXOpen API 且 docs/nx2406-install-index.md 或 nxopen-research.md 附 A 未覆盖、或与旧文献冲突、或需要精确签名/枚举值，或需要判定"不存在"时，先运行本 skill 再写代码或文档。
---

# NX API 查证协议（NX2406 本机实证）

## 何时用

- 要写/引用一个**不在** [docs/nx2406-install-index.md](../../../docs/nx2406-install-index.md)（§2 速查、§2.5 不存在项）和 nxopen-research 附 A 里的 NXOpen 成员/枚举/签名；
- 设计文档与直觉/旧文献冲突；
- 需要精确的 .NET 属性类型、枚举取值、方法返回、许可或 "Created in NXxxxx" 版本注记。

## 证据源（固定常量，NX 根 = `C:\Program Files\Siemens\NX2406`）

| 源 | 路径 | 能回答 |
|---|---|---|
| .NET XML 文档注释 | `%NX_ROOT%\NXBIN\managed\NXOpen.xml`（另：NXOpen.Utilities/UI/UF.xml） | 成员**存在性**与 remarks（`License requirements:` / `Created in NXxxxx`）；**不含类型** |
| .NET 程序集（反射） | `%NX_ROOT%\NXBIN\managed\NXOpen.dll` | **精确签名**：属性类型、方法返回、枚举宿主与取值 |
| C++ 头文件 | `%NX_ROOT%\UGOPEN\NXOpen\CAM_*.hxx`（CAM 类按 `CAM_<类名>.hxx`） | C++ 侧声明与 doc comment（注意声明可能跨多行） |
| 官方样例 | `%NX_ROOT%\UGOPEN\SampleNXOpenApplications\DotNet\CAM\`、`...\CAMSetupImport\` | 实际调用范式（VB/C#） |
| CAM 模板 | `%NX_ROOT%\mach\resource\template_part\{metric,english}\` | `CreateCamSetup` 模板名、模板部件清单 |

## 协议（按序执行，命中即止但尽量三路都过一遍）

### 1. XML 成员清单 + 注记（最常用，先跑）
```bash
cd "/c/Program Files/Siemens/NX2406/NXBIN/managed"
# 类型存在性（含嵌套，用 . 号）：
grep -oE 'name="T:NXOpen\.CAM\.<类名>(\.[A-Za-z]+)?"' NXOpen.xml | sort -u
# 某类型全部公开成员：
grep -oE 'name="[MP]:NXOpen\.CAM\.<类名>\.[^"]*"' NXOpen.xml | sort -u
# 某成员全注释（含 Created in / License）：
awk '/name="[MP]:NXOpen\.CAM\.<类名>\.<成员>/{f=1} f{print; c++} c>8{exit}' NXOpen.xml
# 全局搜枚举取值 / 宿主：
grep -oE 'name="[TF]:NXOpen\.CAM\.[^"]*\.(<取值>)"' NXOpen.xml | sort -u
```
注意事项：① `<类名>` 大小写敏感（如 `ZLevelMillingBuilder`）；② 成员常为**嵌套枚举**（宿主类 `.Types` 或类内嵌套），先按 `T:` 搜宿主再取 `F:` 取值；③ XML 不给属性/返回类型——下一步反射。

**检索纪律（防假负结案，2026-09-04 STEP 导入教训——Step203/214/242Importer 曾因检索缺陷被误判"不存在"）**：

- **类名可能含版本数字**（203/214/242/AP242…）：纯字母 pattern（如 `[Ss]tep[A-Za-z]*`）**必然漏检**。存在性检索一律先跑大小写不敏感子串 + 数字宽容形态：
  ```bash
  grep -oiE 'name="[TMP]:NXOpen\.[^"]*<关键子串>[^"]*"' NXOpen.xml | sort -u          # -i 子串含数字
  grep -oiE 'name="[TMP]:NXOpen\.[^"]*<关键子串>[^"]*"' NXOpen.xml | grep -c '<匹配'    # 先知量级
  ```
- **head 截断 ≠ 零命中**：`sort -u` 后 `T:`（类型）按字节序排在 `M:`（方法）之后——`head -N` 截断会先切掉全部类型条目。存在性问题：先 `grep -c` 知总量，输出落文件再 `tail` 复核，或直接不截断。
- **单形态检索不足为凭**：同一结论至少两种检索形态交叉（精确名 / -i 子串 / 宽松 pattern）。

### 1.5 负结论证伪协议（"不存在"定案前三关，全过才可入索引 §2.5/文档）

任何"X 不存在/零命中/无公开通道"结论，定案前必过三关并在结论附**证伪检索清单**（pattern/语料/计数）：

1. **XML/.NET 关**：NXOpen.xml 全语料 `-i` 子串 + 数字宽容形态零命中，且反射 `NXOpen*.dll` 全程序集类型级零命中（防 XML 收录不全）：
   ```bash
   grep -rli '<关键子串>' NXOpen*.xml 2>/dev/null    # 全 XML 文件
   # 反射：foreach asm in NXOpen*.dll → GetTypes() 过滤 FullName 含关键子串
   ```
2. **C++ 头文件关**：文件名级 + 内容级零命中：
   ```bash
   ls "%NX_ROOT%\UGOPEN\NXOpen" | grep -i '<关键子串>'            # 文件名（含 Step203Importer.hxx 这类）
   grep -rliE '<关键子串>' "%NX_ROOT%\UGOPEN\NXOpen\*.hxx" 2>/dev/null
   grep -rniE '<关键子串>' "%NX_ROOT%\UGOPEN\uf_*.h" 2>/dev/null
   ```
3. **官方样例关**（全库递归，.cs/.vb/.cpp/.py/.java/.hxx）：
   ```bash
   grep -rliE '<关键子串>|Import<X>|Create<X>Importer' "%NX_ROOT%\UGOPEN\SampleNXOpenApplications" "%NX_ROOT%\UGOPEN\NXOpenExamples" 2>/dev/null
   ```
   样例命中即推翻"不存在"（即使 XML 零命中）；已知参考样例（如索引 §1 资源 5 `CAMSetupImport`）内部文件须实际读过才算查过。

索引 §2.5 负条目标注证伪日期与形态；后续出现反例即**负结论撤回 + 索引修订**（U-5 面积、STEP 导入两先例同款）。

### 2. PowerShell 反射（拿精确类型/枚举值/方法签名）
写临时脚本后执行（不要 `-ExecutionPolicy Bypass`，环境会拦截；用 `powershell -NoProfile -File <脚本>`），用毕删除：
```powershell
$dir = 'C:\Program Files\Siemens\NX2406\NXBIN\managed'
$resolving = New-Object 'System.Collections.Generic.HashSet[string]'   # 防 AssemblyResolve 重入栈溢出
$h = [System.ResolveEventHandler]{ param($s,$e)
  $n = ($e.Name -split ',')[0]
  if ($resolving.Contains($n)) { return $null }
  [void]$resolving.Add($n)
  try { $p = Join-Path $dir ($n + '.dll'); if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) } }
  finally { [void]$resolving.Remove($n) } }
[System.AppDomain]::CurrentDomain.add_AssemblyResolve($h)
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $dir 'NXOpen.dll'))
$t = $asm.GetType('NXOpen.CAM.<类名>')                    # 嵌套枚举：'NXOpen.CAM.<类名>+<嵌套枚举>'
if (-not $t) { Write-Output 'NOT FOUND'; exit }
Write-Output ('Type: ' + $t.FullName + '  IsEnum=' + $t.IsEnum)
if ($t.IsEnum) { Write-Output ('Enum values: ' + ([System.Enum]::GetNames($t) -join '|')); exit }
foreach ($p in $t.GetProperties([Reflection.BindingFlags]'Instance,Public')) {
  $l = ('P {0} : {1}' -f $p.Name, $p.PropertyType.FullName)
  if ($p.PropertyType.IsEnum) { $l += '  [enum: ' + ([System.Enum]::GetNames($p.PropertyType) -join '|') + ']' }
  Write-Output $l }
foreach ($m in $t.GetMethods([Reflection.BindingFlags]'Instance,Public,DeclaredOnly')) {
  Write-Output ('M {0}() -> {1}' -f $m.Name, $m.ReturnType.FullName) }
```
可对多个类重复 `GetType` 段；属性形态按四种归类：`Inheritable*Builder`(.Value) / 直接 double·int / 直接枚举 / 类+嵌套枚举(.Type)。

### 3. C++ 头文件（可选，交叉验证）
```bash
ls "/c/Program Files/Siemens/NX2406/UGOPEN/NXOpen" | grep -iE "^CAM_.*<类名>"
grep -nE 'Create<类名>Builder\(' "/c/Program Files/Siemens/NX2406/UGOPEN/NXOpen/CAM_*.hxx" | head
```

### 4. 样例与模板（落地范式）
```bash
grep -rl "<API 名或取值>" "/c/Program Files/Siemens/NX2406/UGOPEN/SampleNXOpenApplications" 2>/dev/null | head
ls "/c/Program Files/Siemens/NX2406/mach/resource/template_part/metric" | grep -iE "mill|drill"
```

## 输出与回填

1. 按此格式汇报：**存在性**（`NXOpen.CAM.xxx` 存在/不存在于 NX2406）→ **宿主/签名** → **取值形态**（四种之一）→ **枚举值** → **remarks**（Created in / License）→ 若本地任何源都无字面量证据（如 typeName 字符串 `"CAVITY_MILL"`）→ 标注**待运行时验证**（列入索引 §3）。
2. 与 docs/nx2406-install-index.md 或 nxopen-research.md 附 A 冲突时，**高亮冲突点**，以本次实证为准。
3. **回填**：把新实证结论补入索引 §2 速查（或 nxopen-research 附 A），并把"不存在项"并入索引 §2.5、运行时验证项并入索引 §3；回填改动向用户展示 diff 摘要。

## 已知坑

- `powershell -ExecutionPolicy Bypass` 会被本环境拦截 → 用 `-NoProfile -File`；
- `AssemblyResolve` 处理器内再 `LoadFrom` 会栈溢出 → 必须带 `$resolving` 去重（上面脚本已含）；
- XML/头文件检索**大小写敏感**；`.NET` 枚举多嵌套在宿主类里（`CutDirection.Types`、`MillToolBuilder.CutterSubtypes`），先找宿主类再取取值；
- 旧文献 API（`ProgramOrderView`、`camSetup.CreatePlanarMillingBuilder`、`CAMSetupBuilder`…）多数在 2406 已不存在，先对照索引 §2.5 再跑协议。
