# compile-comparer-adapter.ps1 [-ExeName <name>] - csc merge pure-logic core + ComparerAdapter to exe
# (NX File>Execute). 榛樿杈撳嚭 ComparerAdapter.exe锛涜 NX 浼氳瘽鍗犵敤鏃舵崲鍚嶈緭鍑恒€?# 鍙傛暟绀轰緥: powershell -NoProfile -File scripts\compile-comparer-adapter.ps1 -ExeName ComparerAdapter-v2.exe
param([string]$ExeName = 'ComparerAdapter.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$nx = 'C:\Program Files\Siemens\NX2406\NXBIN\managed'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root ('.claude\tmp\' + $ExeName)
$rsp = Join-Path $root '.claude\tmp\adapter-src.rsp'

$src = @()
foreach ($d in @('PlanExporter','PlanExecutor','PlanComparer')) {
  $dir = Join-Path $root ('src\NXPlugins\' + $d)
  foreach ($f in (Get-ChildItem $dir -Filter *.cs | Sort-Object Name)) { $src += $f.FullName }
}
$src += Join-Path $root 'src\NXPlugins\Journal\NxCollect.cs'
$src += Join-Path $root 'src\NXPlugins\Journal\ComparerAdapter.cs'

$lines = New-Object 'System.Collections.Generic.List[string]'
foreach ($s in $src) { [void]$lines.Add('"' + $s + '"') }
foreach ($r in @((Join-Path $nx 'NXOpen.dll'), (Join-Path $nx 'NXOpen.Utilities.dll'),
  (Join-Path $nx 'NXOpen.UF.dll'), 'System.Runtime.Serialization.dll', 'System.Xml.dll')) {
  [void]$lines.Add('-r:"' + $r + '"')
}
$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($rsp, $lines, $enc)

& $csc -nologo -t:exe "-out:$out" "@$rsp"
Write-Output ('csc exit=' + $LASTEXITCODE)
