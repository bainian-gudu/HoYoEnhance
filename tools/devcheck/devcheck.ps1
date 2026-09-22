#!/usr/bin/env pwsh
<#
.SYNOPSIS
    本地快速体检：不跑完整构建，就把「编译期会炸的问题」提前抓出来。

.DESCRIPTION
    分层检查，越靠前越快、依赖越少：

      vendor 安装器工具链只在独立项目 Kirara：本仓库不再内嵌 kachina 源码、不是
             submodule；CI 只从 Kirara 最新 Release 下载 kirara-builder.exe，
             不从其它来源拉源码 / 下二进制
      ps1    所有 .ps1 的语法解析（PowerShell Parser，秒级，无依赖）
      packaging 安装包配置与宿主源码的接线：品牌名 / 卸载时要回收的注册表值、计划任务、
             快捷方式、单用户数据目录、协议正文、更新源、自包含发布
      host   src/Host 的 dotnet build（Release，EnableWindowsTargeting）。只证明它编得过，
             不证明它算得对。
      contract C# DTO → TypeScript 桥接类型的生成一致性：DTO 改了但 TS 没重生成就失败。
      hosttest Host 行为断言（ProcessRunner：退出码 / 超时 / 管道排空上限；
             GameLocator：自定义目录快扫、目录剪枝、快捷方式目标反推）。
             自包含测试台，不依赖 xunit；非 Windows 上只做编译验证后 SKIP。
      ui     src/Ui 的 vite 构建（**不在 all 里**，需要先 npm install）。
      ci     CI 脚本行为：工作流里的 action 版本不低于 tools/devcheck/README.md
             登记的下限、all 集合的每一层在 devcheck.yml 里都有步骤真跑

    任何一层失败 → 退出码 1。缺工具链的层标记 SKIP 并给出提示（不算失败）。

.EXAMPLE
    pwsh tools/devcheck/devcheck.ps1
.EXAMPLE
    pwsh tools/devcheck/devcheck.ps1 -Layer host,hosttest

.NOTES
    安装器工具链（kachina 源码快照、kachina-builder、安装器自身的检查层）在
    Kirara 仓库，见该仓库的 tools/devcheck。
#>
[CmdletBinding()]
param(
    # 逗号或空格分隔的层名。故意用 [string] 而不是 [string[]]：
    # `pwsh -File devcheck.ps1 -Layer host,hosttest` 用数组类型会把 "host,hosttest" 当成一个值。
    [string]$Layer = 'all',

    # 不自动安装任何东西（npm install）
    [switch]$SkipInstall,

    # 自检：故意注入错误，确认每一层真的会报错。
    # 只改 tools/devcheck 下的临时文件与少量源码文件
    # （ProcessRunner.cs、GameLocator.Helpers.cs、UiConfigContract.cs）。
    # 自检持有仓库改动锁，并在磁盘上留备份：中断后下次运行会先恢复再开工。
    [switch]$SelfTest
)

# npm / npx / node 自身会打 DEP0040（punycode）、DEP0169（url.parse）这类
# 弃用告警，跟本仓库无关，只会把真正需要看的输出淹掉。子进程继承这个变量。
$env:NODE_NO_WARNINGS = '1'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "devcheck 需要 PowerShell 7+（当前 $($PSVersionTable.PSVersion)）。Windows PowerShell 5.1 请用: pwsh -File tools/devcheck/devcheck.ps1"
}

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# 层内部主动跳过用（类必须在使用前定义）
class LayerSkipped : System.Exception {
    LayerSkipped([string]$message) : base($message) {}
}

$script:IsWin = ($env:OS -eq 'Windows_NT')
$DevCheckRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $DevCheckRoot '..\..')).Path

. (Join-Path $DevCheckRoot 'lib/Common.ps1')
. (Join-Path $DevCheckRoot 'lib/Layers.ps1')
. (Join-Path $DevCheckRoot 'lib/CiScripts.ps1')
. (Join-Path $DevCheckRoot 'lib/SelfTest.ps1')

# ---------------------------------------------------------------------------
# 执行
# ---------------------------------------------------------------------------
if ($SelfTest) {
    Write-Host ''
    Write-Host "devcheck 自检 — 仓库根 $RepoRoot" -ForegroundColor White
    Write-Host ''
    $ok = Invoke-SelfTest
    if (-not $ok) { exit 1 }
    exit 0
}

$validLayers = @('all', 'vendor', 'ps1', 'packaging', 'host', 'contract', 'hosttest', 'ui', 'ci')
$requested = @($Layer -split '[,\s]+' | Where-Object { $_ })
if (-not $requested.Count) { $requested = @('all') }
foreach ($r in $requested) {
    if ($validLayers -notcontains $r) { throw "未知的层 '$r'，可选: $($validLayers -join ', ')" }
}
$wanted = if ($requested -contains 'all') { @('vendor', 'ps1', 'packaging', 'host', 'contract', 'hosttest', 'ci') } else { $requested }

Write-Host ''
Write-Host "devcheck — 仓库根 $RepoRoot" -ForegroundColor White
Write-Host "层      $($wanted -join ', ')" -ForegroundColor White
Write-Host ''

foreach ($l in $wanted) {
    switch ($l) {
        'vendor' { Invoke-Layer 'vendor 安装器工具链只在 Kirara' { Test-KiraraBoundary } }
        'ps1'   { Invoke-Layer 'ps1   PowerShell 脚本语法'    { Test-Ps1Syntax } }
        'packaging' { Invoke-Layer 'packaging 安装包配置接线' { Test-PackagingProfile } }
        'host'  { Invoke-Layer 'host  .NET Host 构建'         { Test-Host } }
        'contract' { Invoke-Layer 'contract C# DTO → TS 契约' { Test-Contracts } }
        'hosttest' { Invoke-Layer 'hosttest Host 行为断言'   { Test-HostTest } }
        'ui'    { Invoke-Layer 'ui    Web UI 构建'            { Test-Ui } }
        'ci'    { Invoke-Layer 'ci    CI 脚本行为'            { Test-CiScripts } }
    }
}

Write-Host ''
Write-Host '════ 汇总 ════' -ForegroundColor White
$script:Results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$failed = @($script:Results | Where-Object { $_.Status -eq 'FAIL' })
if ($failed.Count) {
    Write-Host "✗ $($failed.Count) 层失败: $(($failed | ForEach-Object Layer) -join ', ')" -ForegroundColor Red
    exit 1
}
if ($script:SkipCount) {
    Write-Host "✓ 通过（$($script:SkipCount) 层因缺工具链跳过）" -ForegroundColor Yellow
    exit 0
}
Write-Host '✓ 全部通过' -ForegroundColor Green
exit 0
