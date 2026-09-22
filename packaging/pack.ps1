<#
.SYNOPSIS
    用 Kirara 项目构建出的 kirara-builder 把 dist\ 打成离线安装器（内含更新器）。

.DESCRIPTION
    这是本仓库唯一的打包入口。安装器工具链（kachina 源码快照 + kirara-builder）
    已拆到独立项目 Kirara（默认取同级目录 ..\Kirara），本脚本只负责本项目的载荷与配置。
    步骤与上游 README 一致：
      1. kirara-builder pack -c packaging.config.json -o <app>\<更新程序>.exe
      2. kirara-builder gen  -i <appDir> -m metadata.json -o hashed -r <repoId> -t <ver> -u <updater>
      3. kirara-builder pack -c packaging.config.json -m metadata.json -d hashed -o <app>.Install.<ver>.exe

    产物统一落到 artifacts\：
      <HoYoEnhance 安装包>.exe        离线安装器（含 uninst / update）

.PARAMETER DistDir
    宿主发布输出目录，默认 <repo>\dist（由根目录 build.ps1 生成）。

.PARAMETER OutDir
    产物目录，默认 <repo>\artifacts。

.PARAMETER Version
    版本号；留空则从 src\Host\GenshinFpsUnlocker.Host.csproj 的 <Version> 读取。

.PARAMETER KiraraRepo
    Kirara 项目路径（含安装器源码与 build.ps1），默认同级目录 ..\Kirara。

.PARAMETER BuilderPath
    直接指定 kirara-builder.exe，给出后不再查找 / 构建 Kirara。

.PARAMETER SkipBuilderBuild
    不自动构建 kirara-builder（要求目标位置已有该 exe）。

.EXAMPLE
    pwsh build.ps1 -SkipSetup      # 只编译
    pwsh packaging/pack.ps1        # 只打包（需要 ..\Kirara 或 -BuilderPath）
    pwsh build.ps1                 # 编译 + 打包（内部调用本脚本）
#>
[CmdletBinding()]
param(
    [string]$DistDir = "",
    [string]$OutDir = "",
    [string]$Version = "",
    [string]$RepoId = "bainian-gudu/HoYoEnhance",
    [int]$Jobs = 6,
    [string]$KiraraRepo = "",
    [string]$BuilderPath = "",
    [switch]$SkipBuilderBuild
)

$ErrorActionPreference = "Stop"

$PackagingDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot     = Split-Path -Parent $PackagingDir
$AppName      = "HoYoEnhance"
$Config       = Join-Path $PackagingDir "packaging.config.json"

if (-not $DistDir) { $DistDir = Join-Path $RepoRoot "dist" }
if (-not $OutDir)  { $OutDir  = Join-Path $RepoRoot "artifacts" }
if (-not $KiraraRepo) { $KiraraRepo = Join-Path (Split-Path -Parent $RepoRoot) "Kirara" }
$Builder = if ($BuilderPath) { $BuilderPath } else { Join-Path $KiraraRepo "tools\kirara-builder.exe" }
# -BuilderPath 给的是现成产物：不再去 Kirara 目录找 build.ps1（CI 里没有 Kirara 检出）
if ($BuilderPath) { $SkipBuilderBuild = $true }

function Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok([string]$msg)   { Write-Host "    $msg" -ForegroundColor Green }
# 勿用 $args（PowerShell 自动变量）
function Run([string]$exe, [string[]]$arguments, [string]$what) {
    Write-Host "    $exe $($arguments -join ' ')" -ForegroundColor DarkGray
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$what 失败（exit $LASTEXITCODE）" }
}

# 打包中途会 Push-Location 到 out\pack，相对路径在那之后就失效了（CI 传进来的正是
# 相对路径）：一开始就把可能相对的入参固化成绝对路径。
function Get-AbsolutePath([string]$path) {
    if (-not $path) { return $path }
    if ([System.IO.Path]::IsPathRooted($path)) { return $path }
    return [System.IO.Path]::GetFullPath((Join-Path $PWD $path))
}
$DistDir = Get-AbsolutePath $DistDir
$OutDir  = Get-AbsolutePath $OutDir
$Builder = Get-AbsolutePath $Builder

$imageExtensions = @(".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff")
function Remove-ImageFiles([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $imageExtensions -contains $_.Extension.ToLowerInvariant() } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------ 前置校验
if (-not (Test-Path $Config)) { throw "缺少 Kachina 配置：$Config" }
if (-not (Test-Path (Join-Path $DistDir "$AppName.exe"))) {
    throw "未找到 $DistDir\$AppName.exe —— 请先执行根目录 build.ps1（或 -DistDir 指定发布目录）"
}
if (-not (Test-Path (Join-Path $DistDir "FpsUnlockerStub.dll"))) {
    Write-Warning "$DistDir 内没有 FpsUnlockerStub.dll，安装后无法注入"
}
if (-not (Test-Path (Join-Path $DistDir "StarRailStub.dll"))) {
    Write-Warning "$DistDir 内没有 StarRailStub.dll，星穹铁道的画面效果无法注入（帧率注册表解锁不受影响）"
}
if (-not (Test-Path (Join-Path $DistDir "ui\index.html"))) {
    Write-Warning "$DistDir\ui\index.html 缺失，安装后主界面会走原生兜底页"
}
if (-not $Version) {
    $csproj = Join-Path $RepoRoot "src\Host\GenshinFpsUnlocker.Host.csproj"
    $raw = Get-Content $csproj -Raw
    $Version = if ($raw -match "<Version>([^<]+)</Version>") { $Matches[1].Trim() } else { "1.0.0" }
}
Write-Host "==> 版本 $Version / 仓库 $RepoId" -ForegroundColor Cyan

# ------------------------------------------------------------------ kirara-builder（来自 Kirara）
if ($SkipBuilderBuild) {
    if (-not (Test-Path -LiteralPath $Builder)) {
        throw "kirara-builder.exe 不存在：$Builder（去掉 -SkipBuilderBuild，或用 -BuilderPath 指定）"
    }
} else {
    $kiraraBuild = Join-Path $KiraraRepo "build.ps1"
    if (-not (Test-Path -LiteralPath $kiraraBuild)) {
        throw "找不到 Kirara 的构建脚本：$kiraraBuild（用 -KiraraRepo 指定 Kirara 路径，或用 -BuilderPath 直接给出 kirara-builder.exe）"
    }
    # 交给 Kirara 的脚本判断：builder 缺失、或源码比它新才重建
    Step "检查 / 构建 kirara-builder（$KiraraRepo）"
    & $kiraraBuild | Out-Null
}
if (-not (Test-Path -LiteralPath $Builder)) { throw "kirara-builder.exe 不可用：$Builder" }
Ok("kirara-builder: $Builder")

# ------------------------------------------------------------------ 暂存应用目录
# 注意不要用 build\（Windows 大小写不敏感，会和历史 Build\ 目录冲突）
$Work   = Join-Path $RepoRoot "out\pack"
$AppDir = Join-Path $Work $AppName
if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $AppDir | Out-Null

Step "暂存应用目录 $AppDir"
Copy-Item (Join-Path $DistDir "*") $AppDir -Recurse -Force
Remove-ImageFiles $AppDir
# 这些不该进安装包
foreach ($junk in @("Setup", "$AppName.Install.*.exe", "$AppName.update.exe")) {
    Get-ChildItem $AppDir -Filter $junk -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
}
foreach ($extra in @("USER_AGREEMENT.txt", "LICENSE", "config.example.json")) {
    $src = Join-Path $RepoRoot $extra
    if ((Test-Path $src) -and -not (Test-Path (Join-Path $AppDir $extra))) {
        Copy-Item $src (Join-Path $AppDir $extra) -Force
    }
}
Ok("已暂存 $((Get-ChildItem $AppDir -Recurse -File).Count) 个文件")

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ------------------------------------------------------------------ 1) 更新器
$UpdaterName = "$AppName.update.exe"
$UpdaterPath = Join-Path $AppDir $UpdaterName
Step "pack 更新器 → $UpdaterName"
Run $Builder @("pack", "-c", $Config, "-o", $UpdaterPath) "pack updater"

# ------------------------------------------------------------------ 2) metadata + hashed
Step "gen metadata / hashed"
Push-Location $Work
try {
    Run $Builder @(
        "gen", "-j", "$Jobs", "-i", $AppName,
        "-m", "metadata.json", "-o", "hashed",
        "-r", $RepoId, "-t", $Version,
        "-u", ".\$AppName\$UpdaterName"
    ) "gen"
    # -------------------------------------------------------------- 3) 离线安装器
    $InstallName = "$AppName.Install.$Version.exe"
    Step "pack 离线安装器 → $InstallName"
    Run $Builder @("pack", "-c", $Config, "-m", "metadata.json", "-d", "hashed", "-o", $InstallName) "pack install"

    Copy-Item (Join-Path $Work $InstallName) (Join-Path $OutDir $InstallName) -Force
    Ok("安装器 → $OutDir\$InstallName")
} finally {
    Pop-Location
}

Write-Host ""
Step "打包完成，产物在 $OutDir"
Get-ChildItem $OutDir -File | Sort-Object Name | Format-Table Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 2) } } -AutoSize
