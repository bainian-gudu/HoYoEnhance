[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('offline-install', 'uninstall', 'already-latest', 'offline-update', 'portable-smoke')]
    [string]$Test,
    [string]$CurrentArtifacts = 'artifacts/current',
    [string]$UpdateArtifacts = 'artifacts/update'
)

$ErrorActionPreference = 'Stop'

function Resolve-Directory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '目录参数不能为空' }
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path $PWD $Path
    }
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-SingleArtifact([string]$Directory, [string]$Filter) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "缺少产物目录：$Directory"
    }
    $matches = @(Get-ChildItem -LiteralPath $Directory -Filter $Filter -File)
    if ($matches.Count -ne 1) {
        throw "$Directory 下应恰好有一个 $Filter，实际 $($matches.Count) 个"
    }
    return $matches[0].FullName
}

function Assert-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "缺少 $Description：$Path"
    }
}

function Assert-FileContains([string]$Path, [string]$Needle, [string]$Description) {
    Assert-File $Path $Description
    $text = [System.IO.File]::ReadAllText($Path)
    if (-not $text.Contains($Needle)) {
        throw "$Description 未包含 $Needle：$Path"
    }
}

function Get-Sha256([string]$Path) {
    Assert-File $Path $Path
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Show-InstallerLog {
    $log = Join-Path $env:TEMP 'KachinaInstaller.log'
    if (Test-Path -LiteralPath $log -PathType Leaf) {
        Write-Host '--- KachinaInstaller.log ---'
        Get-Content -LiteralPath $log -Tail 200
        Write-Host '--- end KachinaInstaller.log ---'
    }
}

function Invoke-Executable(
    [string]$Exe,
    [string[]]$Arguments,
    [string]$Label,
    [int]$TimeoutSeconds = 180
) {
    Assert-File $Exe $Label
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    foreach ($arg in $Arguments) {
        $psi.ArgumentList.Add($arg)
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }
        Show-InstallerLog
        throw "$Label 超时（${TimeoutSeconds}s）"
    }
    if ($process.ExitCode -ne 0) {
        Show-InstallerLog
        throw "$Label 失败（exit $($process.ExitCode)）"
    }
}

function Invoke-Install([string]$Installer, [string]$InstallDir, [string]$Label) {
    Invoke-Executable -Exe $Installer -Arguments @('-S', '-D', $InstallDir) -Label $Label
}

function Invoke-Uninstall([string]$InstallDir) {
    $uninstaller = Join-Path $InstallDir 'HoYoEnhance.uninst.exe'
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        Invoke-Executable -Exe $uninstaller -Arguments @('-S') -Label 'uninstall'
    }
}

function Assert-AppFiles([string]$InstallDir) {
    foreach ($relative in @(
        'HoYoEnhance.exe',
        'HoYoEnhance.update.exe',
        'HoYoEnhance.uninst.exe',
        'FpsUnlockerStub.dll',
        'StarRailStub.dll',
        'ui/index.html'
    )) {
        Assert-File (Join-Path $InstallDir $relative) $relative
    }
}

function Get-DataDirectories {
    return @(
        (Join-Path $env:LOCALAPPDATA 'HoYoEnhance'),
        (Join-Path $env:APPDATA 'HoYoEnhance'),
        (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'HoYoEnhance')
    )
}

function New-DataMarker([string]$Name, [string]$Content) {
    foreach ($dir in Get-DataDirectories) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Set-Content -LiteralPath (Join-Path $dir $Name) -Value $Content -Encoding utf8
    }
}

function Assert-DataMarker([string]$Name, [string]$Content) {
    foreach ($dir in Get-DataDirectories) {
        Assert-FileContains (Join-Path $dir $Name) $Content $Name
    }
}

function Remove-DataMarker([string]$Name) {
    foreach ($dir in Get-DataDirectories) {
        Remove-Item -LiteralPath (Join-Path $dir $Name) -Force -ErrorAction SilentlyContinue
    }
}

function New-TestDirectory([string]$Name) {
    $dir = Join-Path $env:TEMP "hoyo-package-e2e-$Name-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Remove-TestDirectory([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Remove-InstalledApp([string]$InstallDir) {
    try {
        Invoke-Uninstall $InstallDir
    } catch {
        Write-Warning "清理安装目录时卸载失败：$($_.Exception.Message)"
    }
    Remove-TestDirectory $InstallDir
}

function Invoke-OfflineInstall([string]$Installer) {
    $installDir = New-TestDirectory 'offline-install'
    try {
        Invoke-Install $Installer $installDir 'offline install'
        Assert-AppFiles $installDir
        Write-Host 'offline-install 通过'
    } finally {
        Remove-InstalledApp $installDir
    }
}

function Invoke-UninstallTest([string]$Installer) {
    $installDir = New-TestDirectory 'uninstall'
    $marker = 'package-e2e-userdata.txt'
    try {
        Invoke-Install $Installer $installDir 'install before uninstall'
        New-DataMarker $marker 'keep'
        Invoke-Uninstall $installDir
        if (Test-Path -LiteralPath (Join-Path $installDir 'HoYoEnhance.exe')) {
            throw '卸载后 HoYoEnhance.exe 仍然存在'
        }
        Assert-DataMarker $marker 'keep'
        Write-Host 'uninstall 通过：程序文件已删除，用户数据默认保留'
    } finally {
        Remove-DataMarker $marker
        Remove-TestDirectory $installDir
    }
}

function Invoke-AlreadyLatest([string]$Installer) {
    $installDir = New-TestDirectory 'already-latest'
    try {
        Invoke-Install $Installer $installDir 'first install'
        $before = Get-Sha256 (Join-Path $installDir 'HoYoEnhance.exe')
        Invoke-Install $Installer $installDir 'second install'
        $after = Get-Sha256 (Join-Path $installDir 'HoYoEnhance.exe')
        if ($before -ne $after) {
            throw "already-latest 改写了 HoYoEnhance.exe：$before -> $after"
        }
        Assert-AppFiles $installDir
        Write-Host 'already-latest 通过：重复安装未改写已装文件'
    } finally {
        Remove-InstalledApp $installDir
    }
}

function Invoke-OfflineUpdate([string]$Installer, [string]$UpdateInstaller) {
    $installDir = New-TestDirectory 'offline-update'
    $marker = 'package-e2e-update-userdata.txt'
    try {
        Invoke-Install $Installer $installDir 'install v1'
        New-DataMarker $marker 'keep'
        Invoke-Install $UpdateInstaller $installDir 'install v2'
        Assert-FileContains (Join-Path $installDir 'ci-update-marker.txt') 'v2' 'ci-update-marker.txt'
        Assert-DataMarker $marker 'keep'
        Assert-AppFiles $installDir
        Write-Host 'offline-update 通过：更新载荷已安装，用户数据保留'
    } finally {
        Remove-DataMarker $marker
        Remove-InstalledApp $installDir
    }
}

function Invoke-PortableSmoke([string]$PortableZip) {
    $extractDir = New-TestDirectory 'portable-smoke'
    try {
        Expand-Archive -LiteralPath $PortableZip -DestinationPath $extractDir -Force
        $exe = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter 'HoYoEnhance.exe' |
            Select-Object -First 1
        if ($null -eq $exe) {
            throw '便携包内没有 HoYoEnhance.exe'
        }
        $appDir = $exe.DirectoryName
        foreach ($relative in @('HoYoEnhance.update.exe', 'FpsUnlockerStub.dll', 'StarRailStub.dll', 'ui/index.html')) {
            Assert-File (Join-Path $appDir $relative) $relative
        }
        $xml = Join-Path $extractDir 'elevated-task.xml'
        Invoke-Executable -Exe $exe.FullName -Arguments @('--dump-elevated-task-xml', $xml) -Label 'portable smoke'
        if ((Get-Item -LiteralPath $xml).Length -le 0) {
            throw '便携包诊断输出为空'
        }
        Write-Host 'portable-smoke 通过：便携包内容完整且主程序可启动诊断路径'
    } finally {
        Remove-TestDirectory $extractDir
    }
}

$CurrentArtifacts = Resolve-Directory $CurrentArtifacts
if ($Test -eq 'offline-update') {
    $UpdateArtifacts = Resolve-Directory $UpdateArtifacts
}

$installer = Get-SingleArtifact $CurrentArtifacts 'HoYoEnhance.Install.*.exe'
switch ($Test) {
    'offline-install' { Invoke-OfflineInstall $installer }
    'uninstall' { Invoke-UninstallTest $installer }
    'already-latest' { Invoke-AlreadyLatest $installer }
    'offline-update' {
        $updateInstaller = Get-SingleArtifact $UpdateArtifacts 'HoYoEnhance.Install.*.exe'
        Invoke-OfflineUpdate $installer $updateInstaller
    }
    'portable-smoke' {
        $portable = Get-SingleArtifact $CurrentArtifacts 'HoYoEnhance-portable-win-x64.zip'
        Invoke-PortableSmoke $portable
    }
}
