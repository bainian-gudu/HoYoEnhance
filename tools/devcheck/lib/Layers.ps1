# devcheck 各层实现（被 devcheck.ps1 dot-source）
#
# 每个 Test-* 函数对应一个层：返回字符串 = 通过的说明，抛异常 = 失败。
# 依赖 devcheck.ps1 的 $RepoRoot / $DevCheckRoot / $KachinaSrc 与 lib/Common.ps1 的助手函数。

function Test-KiraraBoundary {
    # 安装器工具链（上游 kachina-installer 源码快照 + 本地补丁 + kirara-builder）
    # 已拆到独立项目 Kirara。本仓库只保留 packaging/ 下的安装包配置与打包脚本，
    # 因此这里守住三件事：
    #   1) 本仓库不再内嵌 kachina 源码，也不是 submodule；
    #   2) 工作流 / 打包脚本除了从 Kirara 最新 Release 下载 kirara-builder.exe，
    #      没有其它从外部拉源码、下二进制的动作；
    #   3) CI 确实从 Kirara 最新 Release 取 builder，打包只用这个产物。
    $notes = [System.Collections.Generic.List[string]]::new()

    # 1) 本仓库不许再出现 kachina 源码
    if (Test-Path -LiteralPath (Join-Path $RepoRoot '.gitmodules')) {
        throw '存在 .gitmodules —— 安装器工具链必须在独立项目 Kirara，不能以 submodule 拉回来'
    }
    foreach ($stale in @('packaging/kachina', 'kachina', 'packaging/build-kachina.ps1',
                         'installer/kachina', 'installer/build-kachina.ps1')) {
        if (Test-Path -LiteralPath (Join-Path $RepoRoot $stale)) {
            throw "$stale 又出现在本仓库 —— kachina 源码与 builder 构建脚本属于 Kirara"
        }
    }
    $notes.Add('无内嵌 kachina 源码 / 非 submodule')

    # 2) 工作流与打包脚本里除 Kirara Release 下载外，不许出现其它
    #    「从外部拉源码 / 下二进制」的动作。
    #    只扫可执行内容：注释行（# 开头）跳过，避免误伤说明性文字
    $scan = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot '.github/workflows') -Filter '*.yml' -File)
    $scan += @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'packaging') -Filter '*.ps1' -File)
    $rootBuild = Join-Path $RepoRoot 'build.ps1'
    if (Test-Path -LiteralPath $rootBuild) { $scan += @(Get-Item -LiteralPath $rootBuild) }

    $badPatterns = @(
        'YuehaiTeam', 'kachina-installer\.git', 'releases/download', 'release-downloader',
        'git\s+clone', 'git\s+submodule', 'Invoke-WebRequest', 'Invoke-RestMethod',
        'DownloadFile', 'curl\s', 'wget\s'
    )
    $inBlockComment = $false
    foreach ($f in $scan) {
        $lineNo = 0
        foreach ($line in [System.IO.File]::ReadLines($f.FullName)) {
            $lineNo++
            $t = $line.Trim()
            if ($t -match '^<#' -or $inBlockComment) {
                $inBlockComment = -not ($t -match '#>')
                continue
            }
            if ($t.StartsWith('#')) { continue }
            foreach ($pat in $badPatterns) {
                if ($t -match $pat) {
                    throw "$($f.Name):$lineNo 出现从外部拉取的语句 [$pat]: $t"
                }
            }
        }
    }
    $notes.Add("$($scan.Count) 个工作流/脚本无外部拉取")

    # 3) build.yml 必须从 Kirara 最新 Release 下载 kirara-builder.exe
    $buildYml = [System.IO.File]::ReadAllText((Join-Path $RepoRoot '.github/workflows/build.yml'))
    if ($buildYml -notmatch 'gh\s+release\s+download') {
        throw 'build.yml 没有用 gh release download 拉取 kirara-builder —— 只允许直接使用 Kirara 已发布的构建产物'
    }
    if ($buildYml -notmatch '--repo\s+bainian-gudu/Kirara') {
        throw 'build.yml 下载 builder 时没有指定 bainian-gudu/Kirara —— 安装器工具链只能来自该仓库'
    }
    if ($buildYml -notmatch '--pattern\s+["'']?kirara-builder\.exe') {
        throw 'build.yml 下载的不是 kirara-builder.exe'
    }
    if ($buildYml -match 'repository:\s*bainian-gudu/Kirara' -or $buildYml -match 'kirara/build\.ps1') {
        throw 'build.yml 又在检出 / 编译 Kirara 源码 —— 只允许直接下载最新 Release 产物'
    }
    $notes.Add('CI 从 Kirara 最新 Release 下载 builder')

    # 4) 打包脚本只认 Kirara 的产物（或显式指定的 builder）
    $pack = [System.IO.File]::ReadAllText((Join-Path $RepoRoot 'packaging/pack.ps1'))
    foreach ($needle in @('$KiraraRepo', 'build.ps1', '$BuilderPath')) {
        if ($pack -notmatch [regex]::Escape($needle)) {
            throw "packaging/pack.ps1 里找不到 $needle —— 打包脚本必须只从 Kirara 取 kirara-builder"
        }
    }
    # -BuilderPath 给的是现成产物，必须就此跳过 Kirara 目录查找：
    # CI 的 pack job 里没有 Kirara 检出，多查一次就会直接失败（真踩过）。
    if ($pack -notmatch '(?m)^\s*if\s*\(\s*\$BuilderPath\s*\)\s*\{\s*\$SkipBuilderBuild\s*=\s*\$true\s*\}') {
        throw 'packaging/pack.ps1 给了 -BuilderPath 仍会去 Kirara 目录找 build.ps1 —— 与 .PARAMETER BuilderPath 的约定不符'
    }
    $notes.Add('打包脚本只从 Kirara 取 builder')

    return ($notes -join '；')
}

function Test-PackagingProfile {
    # packaging/packaging.config.json 是「应用侧」的唯一事实来源：安装目录、ARP 名称、
    # 旧品牌兼容名、卸载时要清理的注册表 / 计划任务 / 快捷方式 / 用户数据目录。
    # 安装器工具链（Kirara）只读它，所以「改了宿主却忘了改配置」只能在这里发现 ——
    # 每一项都拿宿主源码里的常量交叉断言，而不是在检查里再抄一遍字面量。
    $cfgPath = Join-Path $RepoRoot 'packaging/packaging.config.json'
    if (-not (Test-Path -LiteralPath $cfgPath)) { throw "缺少 $cfgPath" }
    $cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json

    # 宿主源码里的常量：唯一事实来源。有些常量是表达式（ProductName + ".exe"），
    # 所以这里解析后递归求值，而不是只认字面量。
    $script:HostConstCache = @{}
    function Get-HostConst([string]$file, [string]$name) {
        $key = "$file|$name"
        if ($script:HostConstCache.ContainsKey($key)) { return $script:HostConstCache[$key] }
        $text = [System.IO.File]::ReadAllText((Join-Path $RepoRoot $file))
        $m = [regex]::Match($text, "const\s+string\s+$name\s*=\s*([^;]+);")
        if (-not $m.Success) { throw "在 $file 里找不到常量 $name（宿主改了命名？）" }
        $expr = $m.Groups[1].Value.Trim()
        $value = $null
        if ($expr -match '^"([^"]*)"$') {
            $value = $Matches[1]
        }
        elseif ($expr -match '^([A-Za-z_]\w*)\s*\+\s*"([^"]*)"$') {
            $value = (Get-HostConst $file $Matches[1]) + $Matches[2]
        }
        if ($null -eq $value) { throw "无法解析 $file 里的常量 $name：$expr" }
        $script:HostConstCache[$key] = $value
        return $value
    }
    $productName = Get-HostConst 'src/Host/AppPaths.cs' 'ProductName'         # 内部注册表键（历史名）
    $displayName = Get-HostConst 'src/Host/AppPaths.cs' 'ProductDisplayName'  # 用户可见品牌名
    $exeName = Get-HostConst 'src/Host/AppPaths.cs' 'ExecutableFileName'
    $legacyExe = Get-HostConst 'src/Host/AppPaths.cs' 'LegacyExecutableFileName'
    $uninstName = Get-HostConst 'src/Host/AppPaths.cs' 'UninstallerFileName'
    $legacyUninst = Get-HostConst 'src/Host/AppPaths.cs' 'LegacyUninstallerFileName'
    $legacyUpdater = Get-HostConst 'src/Host/AppPaths.cs' 'LegacyUpdaterFileName'
    $taskName = Get-HostConst 'src/Host/Autostart.cs' 'ElevatedTaskName'

    $bad = [System.Collections.Generic.List[string]]::new()
    function Want([string]$what, [bool]$ok, [string]$detail) {
        if (-not $ok) { $bad.Add("$what（$detail）") }
    }
    function HasValue($list, [string]$value) {
        return @($list) -contains $value
    }

    # 1) 品牌名 / 可执行文件 / 安装目录
    Want 'appName 与宿主 ProductDisplayName 一致' ($cfg.appName -eq $displayName) "$($cfg.appName) vs $displayName"
    Want 'exeName 与宿主 ExecutableFileName 一致' ($cfg.exeName -eq $exeName) "$($cfg.exeName) vs $exeName"
    Want 'programFilesPath 与品牌名一致' ($cfg.programFilesPath -eq $displayName) "$($cfg.programFilesPath)"
    Want 'shortcutName 与品牌名一致' ($cfg.shortcutName -eq $displayName) "$($cfg.shortcutName)"
    Want 'title 与品牌名一致' ($cfg.title -eq $displayName) "$($cfg.title)"
    Want 'uninstallName 与宿主 UninstallerFileName 一致' ($cfg.uninstallName -eq $uninstName) "$($cfg.uninstallName) vs $uninstName"

    # 2) 改名前的兼容识别：旧 exe / 旧卸载器 / 旧安装目录 / 旧更新器
    Want 'legacyExeNames 含宿主 LegacyExecutableFileName' (HasValue $cfg.legacyExeNames $legacyExe) "$($cfg.legacyExeNames -join ',')"
    Want 'legacyUninstallNames 含宿主 LegacyUninstallerFileName' (HasValue $cfg.legacyUninstallNames $legacyUninst) "$($cfg.legacyUninstallNames -join ',')"
    Want 'legacyProgramFilesPaths 含历史安装目录' (HasValue $cfg.legacyProgramFilesPaths $productName) "$($cfg.legacyProgramFilesPaths -join ',')"
    Want 'extraUninstallPath 覆盖旧更新器' (@($cfg.extraUninstallPath) -contains "`${INSTALL_PATH}/$legacyUpdater") "$($cfg.extraUninstallPath -join ' | ')"
    Want '内部注册表键继续使用历史名' ($cfg.regName -eq $productName) "$($cfg.regName) vs $productName"

    # 3) 卸载回收：宿主写的自启动项 / 计划任务 / 快捷方式
    $runEntry = @($cfg.extraUninstallRegistry) | Where-Object {
        $_.hive -eq 'HKCU' -and $_.key -eq 'Software\Microsoft\Windows\CurrentVersion\Run' -and $_.value -eq $productName
    }
    Want 'extraUninstallRegistry 回收宿主写的 HKCU Run 值' ($runEntry.Count -eq 1) "value=$productName"
    Want 'extraUninstallScheduledTasks 登记宿主那个计划任务名' (HasValue $cfg.extraUninstallScheduledTasks $taskName) "$($cfg.extraUninstallScheduledTasks -join ',') vs $taskName"
    foreach ($lnk in @("$displayName.lnk", "Uninstall $displayName.lnk", "卸载$displayName.lnk", "$productName.lnk", '原神帧率解锁.lnk')) {
        Want "extraUninstallLnkNames 覆盖 $lnk" (HasValue $cfg.extraUninstallLnkNames $lnk) "$($cfg.extraUninstallLnkNames -join ',')"
    }

    # 4) 用户数据目录：宿主的三条回退链都要被卸载器覆盖，且都是「%VAR%/…/产品名」形状
    $dataPaths = @($cfg.userDataPath)
    Want 'userDataPath 非空' ($dataPaths.Count -gt 0) ''
    foreach ($entry in $dataPaths) {
        $t = "$entry".TrimEnd('/', '\')
        $ok = $t.StartsWith('%') -and $t.EndsWith($productName) -and ($t.Split([char[]]@('/', '\')).Count -ge 2)
        Want "userDataPath 形状合法：$entry" $ok '需要 %VAR%/…/产品名，不能是容器本身'
    }
    foreach ($var in @('%LOCALAPPDATA%', '%APPDATA%', '%USERPROFILE%/Documents')) {
        $hit = $dataPaths | Where-Object { "$_" -like "$var/*" }
        Want "userDataPath 覆盖宿主回退目录 $var" ($null -ne $hit) "$($dataPaths -join ' | ')"
    }

    # 5) 协议正文与更新源
    $agreement = Join-Path (Split-Path -Parent $cfgPath) $cfg.agreementFile
    $agreementOk = (Test-Path -LiteralPath $agreement) -and ((Get-Item -LiteralPath $agreement).Length -gt 100)
    Want 'agreementFile 指向的协议正文存在且非空' $agreementOk "$agreement"
    Want 'agreementFormat 为 text' ($cfg.agreementFormat -eq 'text') "$($cfg.agreementFormat)"
    Want 'agreementTitle 非空' (-not [string]::IsNullOrWhiteSpace($cfg.agreementTitle)) "$($cfg.agreementTitle)"
    $expectedUri = "bainian-gudu/HoYoEnhance/releases/download/v`${version}/$($cfg.appName).Install.`${version}.exe"
    $uri = @($cfg.source)[0].uri
    Want 'source 指向本仓库的 Release 安装包' ("$uri" -like "*$expectedUri") "$uri"

    # 6) 运行库与提权策略
    Want 'runtimes 含 .NET Desktop Runtime 9' (@($cfg.runtimes) -contains 'Microsoft.DotNet.DesktopRuntime.9') "$($cfg.runtimes -join ',')"
    Want 'runtimes 含 VCRedist' (@($cfg.runtimes) -contains 'Microsoft.VCRedist.2015+.x64') "$($cfg.runtimes -join ',')"
    Want 'uacStrategy 为 prefer-admin' ($cfg.uacStrategy -eq 'prefer-admin') "$($cfg.uacStrategy)"

    if ($bad.Count) {
        throw "安装包配置与宿主源码不一致（$($bad.Count) 项）：`n   - " + ($bad -join "`n   - ")
    }
    return "配置与宿主一致：$displayName / regName=$productName / 任务=$taskName / 数据目录 $($dataPaths.Count) 条"
}

function Test-Ps1Syntax {
    $files = @(Get-ChildItem -Path $RepoRoot -Recurse -Filter '*.ps1' -File |
        Where-Object { $_.FullName -notmatch '[\\/](node_modules|target|dist|bin|obj|gen|\.git)[\\/]' })
    $bad = 0
    foreach ($f in $files) {
        $tokens = $null; $errs = $null
        [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errs) | Out-Null
        if ($errs -and $errs.Count) {
            $bad++
            Write-Bad $f.FullName.Substring($RepoRoot.Length + 1)
            foreach ($e in $errs) { Write-Bad "   line $($e.Extent.StartLineNumber): $($e.Message)" }
        }
    }
    if ($bad) { throw "$bad / $($files.Count) 个 .ps1 有语法错误" }
    return "$($files.Count) 个 .ps1 语法通过"
}

function Test-Host {
    $dotnet = Get-Tool 'dotnet'
    if (-not $dotnet) { Skip-Layer 'dotnet 不在 PATH（.NET 9 SDK）' }
    $proj = Join-Path $RepoRoot 'src/Host/GenshinFpsUnlocker.Host.csproj'
    if (-not (Test-Path -LiteralPath $proj)) { throw "找不到 $proj" }
    $r = Invoke-Native -FilePath $dotnet `
        -Arguments @('build', $proj, '-c', 'Release', '-p:EnableWindowsTargeting=true', '--nologo', '-v', 'q') `
        -WorkingDirectory $RepoRoot -Tail 30
    if ($r.ExitCode -ne 0) { throw 'dotnet build 失败' }
    $warnLine = ($r.Output -split "`r?`n" | Where-Object { $_ -match 'Warning' } | Select-Object -First 1)
    if ($warnLine) { return $warnLine.Trim() }
    return 'Host 构建通过'
}

function Test-HostTest {
    $dotnet = Get-Tool 'dotnet'
    if (-not $dotnet) { Skip-Layer 'dotnet 不在 PATH（.NET 9 SDK）' }
    $proj = Join-Path $DevCheckRoot 'hosttest/Harness.csproj'
    if (-not (Test-Path -LiteralPath $proj)) { throw "找不到 $proj" }

    # 测试台引用的是托管宿主（WinExe + WinForms），任何平台都能编，
    # 但只有 Windows 装得了 Microsoft.WindowsDesktop.App 运行时、跑得起来。
    # 输出目录由 SDK 推导（RID / TargetFramework 因机器而异），这里直接问 MSBuild 要，
    # 不猜路径：Windows runner 上曾有 TFM 目录与 RID 子目录两种布局。
    $buildArgs = @('build', $proj, '-c', 'Debug', '-p:EnableWindowsTargeting=true', '--nologo', '-v', 'q')
    $r = Invoke-Native -FilePath $dotnet -Arguments $buildArgs -WorkingDirectory $RepoRoot -Tail 20
    if ($r.ExitCode -ne 0) { throw '测试台编译失败' }

    $r = Invoke-Native -FilePath $dotnet `
        -Arguments @('msbuild', $proj, '-p:Configuration=Debug', '-p:EnableWindowsTargeting=true', '-getProperty:TargetPath') `
        -WorkingDirectory $RepoRoot -Tail 5
    if ($r.ExitCode -ne 0) { throw '取不到测试台输出路径（dotnet msbuild -getProperty:TargetPath）' }
    $dll = ($r.Output -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim()
    if (-not $dll) { throw 'dotnet msbuild 没有返回 TargetPath' }

    if (-not $script:IsWin) {
        Skip-Layer "非 Windows：测试台依赖 WindowsDesktop 运行时，这里只验证它能编过（断言交给 windows-latest）：$dll"
    }

    if (-not (Test-Path -LiteralPath $dll)) { throw "测试台没有产出 $dll" }
    $r = Invoke-Native -FilePath $dotnet -Arguments @($dll) -WorkingDirectory (Split-Path -Parent $dll) -Tail 40

    # 退出码之外再抓一行汇总：测试进程被运行时错误打断时，行号/原因是唯一线索。
    $summary = ($r.Output -split "`r?`n" | Where-Object { $_ -match '====' } | Select-Object -Last 1)
    if ($r.ExitCode -ne 0) {
        if (-not $summary) { $summary = "退出码 $($r.ExitCode)" }
        throw "Host 行为断言失败：$summary"
    }
    if (-not $summary) { $summary = '断言全部通过' }
    return $summary.Trim()
}

function Test-Ui {
    $npm = Get-Tool 'npm'
    if (-not $npm) { Skip-Layer 'npm 不在 PATH' }
    $ui = Join-Path $RepoRoot 'src/Ui'
    if (-not (Test-Path (Join-Path $ui 'node_modules'))) {
        Skip-Layer 'src/Ui/node_modules 不存在（先 cd src/Ui && npm install）'
    }
    $r = Invoke-Native -FilePath $npm -Arguments @('run', 'build') -WorkingDirectory $ui -Tail 30
    if ($r.ExitCode -ne 0) { throw 'src/Ui 构建失败' }
    return 'src/Ui vite build 通过'
}
