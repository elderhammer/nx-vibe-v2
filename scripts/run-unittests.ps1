# run-unittests.ps1 — 纯逻辑层单测红线回归（PlanExporter + PlanExecutor，零依赖 Runner）
# 用法：powershell -NoProfile -File scripts\run-unittests.ps1
# 编译：csc 经响应文件（避免 shell 参数路径剥除问题）；产物输出 .claude\tmp\（已 gitignore）。
# 判定：退出码 = 失败数；全绿输出 "== 汇总: pass=N fail=0 =="。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = @()
foreach ($d in @('PlanExporter','PlanExecutor','PlanComparer','PlanExporterTests','PlanExecutorTests','PlanComparerTests')) {
  Get-ChildItem (Join-Path $root ('src\NXPlugins\' + $d)) -Filter *.cs | ForEach-Object { $src += $_.FullName }
}
New-Item -ItemType Directory -Force -Path (Join-Path $root '.claude\tmp') | Out-Null
$rsp = Join-Path $root '.claude\tmp\src.rsp'
$src | Set-Content -Path $rsp -Encoding ASCII
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root '.claude\tmp\unittests.exe'
& $csc -nologo -t:exe -out:$out "@$rsp"
if ($LASTEXITCODE -ne 0) { Write-Output ('csc 编译失败 exit=' + $LASTEXITCODE); exit $LASTEXITCODE }
& $out
exit $LASTEXITCODE
