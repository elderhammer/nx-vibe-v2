# compile-exporter-adapter.ps1 [-ExeName <name>] - csc merge pure-logic core + ExporterAdapter to exe
# (NX File>Execute). 默认输出 ExporterAdapter.exe；被 NX 会话占用时换名输出。
# 参数示例: powershell -NoProfile -File scripts\compile-exporter-adapter.ps1 -ExeName ExporterAdapter-v2.exe
param([string]$ExeName = 'ExporterAdapter.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$nx = 'C:\Program Files\Siemens\NX2406\NXBIN\managed'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $root ('.claude\tmp\' + $ExeName)
$rsp = Join-Path $root '.claude\tmp\adapter-src.rsp'

$src = @()
foreach ($d in @('PlanExporter','PlanExecutor')) {
  $dir = Join-Path $root ('src\NXPlugins\' + $d)
  foreach ($f in (Get-ChildItem $dir -Filter *.cs | Sort-Object Name)) { $src += $f.FullName }
}
$src += Join-Path $root 'src\NXPlugins\Journal\ExporterAdapter.cs'

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
