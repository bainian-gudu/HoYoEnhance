# devcheck 自检（被 devcheck.ps1 dot-source）
#
# 证明这套检查不是空壳：往**真实文件 / 临时文件**里注入错误，逐个确认对应层会失败。
# 任何一层「注入了错误却没报错」= 自检失败。

function Invoke-SelfTest {
    $tmpDir = Join-Path $DevCheckRoot '_selftest'
    $cases = [System.Collections.Generic.List[object]]::new()

    function Add-Case {
        param([string]$Name, [scriptblock]$Mutate, [scriptblock]$Run, [scriptblock]$Cleanup = {})
        $cases.Add([pscustomobject]@{ Name = $Name; Mutate = $Mutate; Run = $Run; Cleanup = $Cleanup })
    }

    # --- 1) vendor：kachina 源码又被搬回本仓库就必须报错 ---
    $staleKachina = Join-Path $RepoRoot 'packaging/kachina'
    Add-Case 'vendor 层能抓到 kachina 源码被搬回来' `
        -Mutate {
            New-Item -ItemType Directory -Path $staleKachina -Force | Out-Null
            Set-Content -Path (Join-Path $staleKachina 'package.json') -Encoding utf8 -Value '{"name":"kachina-installer"}'
        } `
        -Run { Test-KiraraBoundary } `
        -Cleanup { if (Test-Path -LiteralPath $staleKachina) { Remove-Item -LiteralPath $staleKachina -Recurse -Force } }

    # --- 2) vendor：工作流里出现从外部拉取的动作就必须报错 ---
    $badWorkflow = Join-Path $RepoRoot '.github/workflows/zz-devcheck-selftest.yml'
    Add-Case 'vendor 层能抓到工作流从外部拉取' `
        -Mutate {
            Set-Content -Path $badWorkflow -Encoding utf8 -Value @'
name: selftest
on: workflow_dispatch
jobs:
  x:
    runs-on: ubuntu-latest
    steps:
      - run: Invoke-WebRequest https://example.invalid/kirara-builder.exe -OutFile packaging/tools/kirara-builder.exe
'@
        } `
        -Run { Test-KiraraBoundary } `
        -Cleanup { if (Test-Path -LiteralPath $badWorkflow) { Remove-Item -LiteralPath $badWorkflow -Force } }

    # --- 3) vendor：打包脚本不再指向 Kirara 就必须报错 ---
    $packScript = Join-Path $RepoRoot 'packaging/pack.ps1'
    Add-Case 'vendor 层能抓到打包脚本不再引用 Kirara' `
        -Mutate {
            $text = [System.IO.File]::ReadAllText($packScript)
            $broken = $text.Replace('$KiraraRepo', '$SomewhereElse')
            if ($broken -eq $text) { throw '注入失败：pack.ps1 里没有 $KiraraRepo' }
            [System.IO.File]::WriteAllText($packScript, $broken)
        } `
        -Run { Test-KiraraBoundary } `
        -Cleanup { Restore-RepoFile -Backup (Get-RepoBackupPath -Path $packScript) -Path $packScript }

    # --- 3b) vendor：build.yml 不再从 Kirara 的 Release 取 builder 就必须报错 ---
    $buildYml = Join-Path $RepoRoot '.github/workflows/build.yml'
    Add-Case 'vendor 层能抓到 builder 来源不再是 Kirara Release' `
        -Mutate {
            $text = [System.IO.File]::ReadAllText($buildYml)
            $broken = $text.Replace('bainian-gudu/Kirara', 'example/Elsewhere')
            if ($broken -eq $text) { throw '注入失败：build.yml 里没有 bainian-gudu/Kirara' }
            [System.IO.File]::WriteAllText($buildYml, $broken)
        } `
        -Run { Test-KiraraBoundary } `
        -Cleanup { Restore-RepoFile -Backup (Get-RepoBackupPath -Path $buildYml) -Path $buildYml }

    # --- 3c) vendor：-BuilderPath 不再隐含跳过 Kirara 就必须报错 ---
    Add-Case 'vendor 层能抓到 -BuilderPath 仍去找 Kirara 源码' `
        -Mutate {
            $text = [System.IO.File]::ReadAllText($packScript)
            $broken = $text.Replace('if ($BuilderPath) { $SkipBuilderBuild = $true }', 'if ($false) { $SkipBuilderBuild = $true }')
            if ($broken -eq $text) { throw '注入失败：pack.ps1 里没有 -BuilderPath 隐含跳过的分支' }
            [System.IO.File]::WriteAllText($packScript, $broken)
        } `
        -Run { Test-KiraraBoundary } `
        -Cleanup { Restore-RepoFile -Backup (Get-RepoBackupPath -Path $packScript) -Path $packScript }

    # --- 4) ps1：临时放一个语法错误的 .ps1 进仓库 ---
    Add-Case 'ps1 层能抓到 PowerShell 语法错误' `
        -Mutate {
            New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
            Set-Content -Path (Join-Path $tmpDir 'broken.ps1') -Value 'if ($x { Write-Host "unclosed" ' -Encoding utf8
        } `
        -Run { Test-Ps1Syntax }

    # --- 5) packaging：打包配置与宿主源码对不上就必须报错 ---
    $profile = Join-Path $RepoRoot 'packaging/packaging.config.json'
    Add-Case 'packaging 层能抓到打包配置与宿主不一致' `
        -Mutate {
            $text = [System.IO.File]::ReadAllText($profile)
            $broken = $text.Replace('"exeName": "HoYoEnhance.exe"', '"exeName": "SomethingElse.exe"')
            if ($broken -eq $text) { throw '注入失败：packaging.config.json 里没有 exeName 的当前取值' }
            [System.IO.File]::WriteAllText($profile, $broken)
        } `
        -Run { Test-PackagingProfile } `
        -Cleanup { Restore-RepoFile -Backup (Get-RepoBackupPath -Path $profile) -Path $profile }

    # --- 6) hosttest：把 ProcessRunner 超时路径上的 KillTree 拿掉 ---
    #      只注入一个能编过的行为错误（少杀进程树，而不是改标记或提前 return，
    #      后两者会带 CS0162 噪音或者只在特定断言上暴露）。
    $processRunner = Join-Path $RepoRoot 'src/Host/ProcessRunner.cs'
    Add-Case 'hosttest 层能抓到超时没杀进程树' `
        -Mutate {
            $text = [System.IO.File]::ReadAllText($processRunner)
            # 换行可能是 LF 也可能是 CRLF（取决于 checkout 时的 autocrlf），
            # 先归一化成 LF 再匹配，否则在 Windows runner 上会「注入失败」。
            $broken = $text.Replace("`r`n", "`n").Replace("                KillTree(process);`n", '')
            if ($broken -eq $text) { throw '注入失败：没找到 ProcessRunner 的超时分支' }
            [System.IO.File]::WriteAllText($processRunner, $broken)
        } `
        -Run { Test-HostTest } `
        -Cleanup { Restore-RepoFile -Backup (Get-RepoBackupPath -Path $processRunner) -Path $processRunner }

    # --- 7) hosttest：磁盘快扫的目录剪枝被改坏 ---
    #      注入方式是删掉剪枝表里的系统目录那一行（而不是 return false），
    #      编译干净、没有 CS0162 噪音，只有真的跑断言才会发现。
    $gameLocatorHelpers = Join-Path $RepoRoot 'src/Host/GameLocator.Helpers.cs'
    Add-Case 'hosttest 层能抓到快扫不再剪枝系统目录' `
        -Mutate {
            $text = [System.IO.File]::ReadAllText($gameLocatorHelpers)
            # 换行可能是 LF 也可能是 CRLF（取决于 checkout 时的 autocrlf）。
            $needle = '        "Windows", "WinSxS", "System32", "SysWOW64", "SystemApps", "servicing",' + "`n"
            $broken = $text.Replace("`r`n", "`n").Replace($needle, '')
            if ($broken -eq $text) { throw '注入失败：没找到剪枝表里的系统目录行' }
            [System.IO.File]::WriteAllText($gameLocatorHelpers, $broken)
        } `
        -Run { Test-HostTest } `
        -Cleanup { Restore-RepoFile -Backup (Get-RepoBackupPath -Path $gameLocatorHelpers) -Path $gameLocatorHelpers }

    # 先按磁盘备份清掉上一次被中断的自检留下的注入，再上锁独占。
    Clear-RepoMutations
    $caught = 0; $missed = 0; $skipped = 0

    Enter-RepoLock
    try {
        Write-Step 'selftest 注入错误自检'
        $idx = 0
        foreach ($c in $cases) {
            $idx++
            try {
                # Mutate 之前先把原始内容落到磁盘：进程被杀也留得下恢复依据。
                # 只在备份不存在时写：否则某个用例没清干净时，备份会被「已污染」的
                # 内容覆盖，之后再也回不到原始版本。
                foreach ($rel in $script:SelfTestRepoFiles) {
                    $path = Join-Path $RepoRoot $rel
                    if (-not (Test-Path -LiteralPath (Get-RepoBackupPath -Path $path))) {
                        Backup-RepoFile -Path $path | Out-Null
                    }
                }
                & $c.Mutate
            }
            catch {
                $missed++
                Write-Bad "  ✗ $($c.Name) —— 注入失败: $($_.Exception.Message)"
                continue
            }
            Write-Host "  ── 注入 $idx/$($cases.Count)：$($c.Name)" -ForegroundColor DarkYellow
            Write-Host '     ↓ 接下来这段报错是故意注入的，看到它才说明这层没被架空' -ForegroundColor DarkGray
            $outcome = 'CAUGHT'
            try {
                & $c.Run | Out-Null
                $outcome = 'MISSED'
            }
            catch [LayerSkipped] { $outcome = 'SKIPPED' }
            catch { $outcome = 'CAUGHT' }
            try { & $c.Cleanup } catch { }

            switch ($outcome) {
                'CAUGHT'  { $caught++;  Write-Ok "  ✓ $($c.Name)" }
                'SKIPPED' { $skipped++; Write-Info "  - $($c.Name)（缺工具链，跳过）" }
                'MISSED'  { $missed++;  Write-Bad "  ✗ $($c.Name) —— 注入了错误却没报错，这层是空壳！" }
            }
        }

        # 收尾：删掉临时目录
        if (Test-Path -LiteralPath $tmpDir) { Remove-Item -LiteralPath $tmpDir -Recurse -Force }
    }
    finally {
        # 无论正常结束还是抛异常，都要把仓库内的注入还原并放锁。
        Clear-RepoMutations -Quiet
        # Backup-RepoFile 会顺手建目录，这里再兜底删一次，避免留下空目录。
        Remove-Item -LiteralPath (Get-SelfTestBackupDir) -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Get-SelfTestBackupDir) -Recurse -Force -ErrorAction SilentlyContinue
        Exit-RepoLock
    }

    Write-Host ''
    if ($missed) {
        Write-Host "✗ 自检失败：$missed 个注入错误没被抓到" -ForegroundColor Red
        return $false
    }
    Write-Host "✓ 自检通过：$caught 个注入错误全部被抓到$(if ($skipped) { "，$skipped 个因缺工具链跳过" } else { '' })" -ForegroundColor Green
    return $true
}
