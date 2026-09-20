function Test-CiScripts {
    # 工作流用的 action 版本必须 ≥ tools/devcheck/README.md 里登记的下限（低于下限会在
    # runner 上打 Node 20 弃用告警）。README 改了、工作流忘了跟着升，这里就会失败。
    $readme = Get-Content -LiteralPath (Join-Path $RepoRoot 'tools/devcheck/README.md') -Raw
    $floorRow = ($readme -split "`n") | Where-Object { $_ -match 'action 的版本下限' } | Select-Object -First 1
    if (-not $floorRow) { throw 'tools/devcheck/README.md 里找不到「工作流里 action 的版本下限」那一行' }
    $floors = @{}
    foreach ($m in [regex]::Matches($floorRow, '`([\w\-]+/[\w\-]+)`\s*≥\s*v(\d+)')) {
        $floors[$m.Groups[1].Value] = [int]$m.Groups[2].Value
    }
    if ($floors.Count -eq 0) { throw '版本下限那一行里没有解析出任何 action' }

    $checked = 0
    foreach ($wf in Get-ChildItem -LiteralPath (Join-Path $RepoRoot '.github/workflows') -Filter '*.yml' -File) {
        $text = Get-Content -LiteralPath $wf.FullName -Raw
        foreach ($m in [regex]::Matches($text, 'uses:\s*([\w\-]+/[\w\-]+)@v(\d+)')) {
            $name = $m.Groups[1].Value
            if (-not $floors.ContainsKey($name)) { continue }
            $used = [int]$m.Groups[2].Value
            $checked++
            if ($used -lt $floors[$name]) {
                throw "$($wf.Name) 里 $name@v$used 低于 README 登记的下限 v$($floors[$name])"
            }
        }
    }
    if ($checked -eq 0) { throw '工作流里没有找到任何受版本下限约束的 action，检查正则是否失效' }

    # all 集合的每一层都必须在 devcheck.yml 里真有一步跑它 ——
    # 新加一层却忘了接进工作流，本地和 CI 都会「绿」，那层等于没写。
    $main = Get-Content -LiteralPath (Join-Path $DevCheckRoot 'devcheck.ps1') -Raw
    $allBlock = [regex]::Match($main, "\`$wanted\s*=\s*if\s*\([^\n]*\)\s*\{\s*@\(([^)]*)\)")
    if (-not $allBlock.Success) { throw 'devcheck.ps1 里找不到 all 层的 $wanted 定义，解析规则失效了' }
    $layers = @([regex]::Matches($allBlock.Groups[1].Value, "'([a-z0-9]+)'") |
        ForEach-Object { $_.Groups[1].Value })
    if ($layers.Count -lt 5) { throw "只从 all 集合里解析出 $($layers.Count) 层，解析规则失效了" }

    $workflow = Get-Content -LiteralPath (Join-Path $RepoRoot '.github/workflows/devcheck.yml') -Raw
    # 无参数的那一步才会跳过参数解析、跑 $wanted 里由 all 定义的层；
    # 只写 -Layer all 不会命中这条，所以 all 集合必须在 CI 上真跑过。
    $runsAll = $workflow -match '(?m)^\s*(?:-\s*)?run:\s*pwsh\s+-NoProfile\s+-File\s+\S*devcheck\.ps1\s*$'
    if (-not $runsAll) {
        throw 'devcheck.yml 里没有一步是不带 -Layer 跑 devcheck.ps1 —— all 集合的层不会在 CI 上执行'
    }
    # 每层要么被 -Layer <名字> 单独点名，要么由上面那次裸调用覆盖。
    $single = @($layers | Where-Object { $workflow -match "-Layer\s+$_\b" })

    return "devcheck.yml 用 all 覆盖 $($layers.Count) 层（其中 $($single.Count) 层有单独步骤：$($single -join '、')）"
}
