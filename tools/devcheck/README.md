# tools/devcheck — 不跑完整构建的本地体检

这里检查的是**应用本体**（Web UI + Stub + Host）与**打包配置**。安装器工具链
（上游 kachina 源码快照、`kirara-builder`、安装器自身的 Rust / Vue 检查）在独立项目
Kirara 的 `tools/devcheck` 里，本仓库不再持有那份源码。

```powershell
# 仓库根目录
pwsh tools/devcheck/devcheck.ps1                    # all：vendor ps1 packaging host hosttest ci
pwsh tools/devcheck/devcheck.ps1 -Layer host,hosttest
pwsh tools/devcheck/devcheck.ps1 -SelfTest          # 自检：注入错误，确认每层都会报错
pwsh tools/devcheck/devcheck.ps1 -Layer ui          # 不在 all 里：要先 cd src/Ui && npm install
```

任何一层失败 → 退出码 1。缺工具链的层标记 `SKIP` 并给出提示，不算失败。
这套检查在 `.github/workflows/devcheck.yml` 里自动跑（push 到 main、任何 PR、
手动触发；ubuntu + windows 双 runner）。

## 分层

| 层 | 检查什么 | 需要的工具 | 热跑耗时 |
| --- | --- | --- | --- |
| `vendor` | **安装器工具链只在 Kirara**：本仓库没有内嵌 kachina 源码、不是 submodule；工作流与打包脚本除从 Kirara 最新 Release 下载 `kirara-builder.exe` 外，没有其它从外部拉源码 / 下二进制的动作 | pwsh 7 | <0.1s |
| `ps1` | 仓库里全部 `.ps1` 的语法（PowerShell Parser） | pwsh 7 | <0.1s |
| `packaging` | **打包配置与宿主源码的接线**：`packaging/packaging.config.json` 的品牌名 / 旧品牌兼容名 / 卸载时要回收的注册表值、计划任务、快捷方式、用户数据目录、协议正文、更新源，逐项与 `src/Host` 里的常量交叉断言 | pwsh 7 | ~0.1s |
| `host` | `src/Host` 的 `dotnet build -c Release -p:EnableWindowsTargeting=true` | .NET 9 SDK | ~2–8s |
| `hosttest` | Host 的**行为断言**（自包含测试台，不依赖 xunit/MSTest）：`ProcessRunner` 的正常退出 / 非 0 退出码 / 超时杀进程树 / 启动失败 / 双管道并发读 / **孙进程继承管道写端时不干等**；`GameLocator` 的自定义目录快扫命中、系统目录与 installer 剪枝、`_Data` 在上一层的布局、同名假 exe 排除、主程序名判定、快捷方式目标反推。Linux/macOS 只验证测试台能编译，断言在 windows-latest 上真跑 | .NET 9 SDK | 首次 ~8s，之后 ~4s |
| `ui` | `src/Ui` 的 `vite build`（**不在 `all` 里**，要先 `cd src/Ui && npm install`） | node + npm | 视机器 |
| `ci` | 工作流里的 action 版本不低于本文登记的**版本下限**；`all` 集合的每一层在 `devcheck.yml` 里都有步骤真跑 | pwsh 7 | ~1s |

全套热跑 ≈ 5–10 秒（`hosttest` 的断言依赖 Windows，缺平台时只做编译后 SKIP）。

## `vendor` 层：安装器工具链只在 Kirara

安装器（Kachina 源码快照 + 本地补丁 + `kirara-builder`）已拆到独立项目 Kirara。
这一层把「本仓库不再持有它」变成可执行的断言：

1. 仓库根不存在 `.gitmodules`，且 `packaging/kachina`、`kachina/`、
   `packaging/build-kachina.ps1` 都不存在（历史路径 `installer/...` 一并守着，
   源码不能再被搬回来）
2. `.github/workflows/*.yml`、`packaging/*.ps1`、`build.ps1` 里除 Kirara Release 下载外
   **没有任何其它**从外部拉取的动作：`YuehaiTeam`、`kachina-installer.git`、
   `releases/download`、`release-downloader`、`git clone`、`git submodule`、
   `Invoke-WebRequest`、`Invoke-RestMethod`、`DownloadFile`、`curl`、`wget`
   （只扫可执行内容，`#` 注释行与 `<# #>` 块跳过）
3. `build.yml` 用 `gh release download --repo bainian-gudu/Kirara --pattern kirara-builder.exe`
   直接取最新 Release 的构建产物，不再检出 Kirara 源码、不再调用它的 `build.ps1`
4. `packaging/pack.ps1` 只从 Kirara 取 builder（`$KiraraRepo` / `build.ps1` / `$BuilderPath`），
   给出 `-BuilderPath` 后不再去 Kirara 目录找 `build.ps1`（CI 的 pack job 里没有 Kirara 检出），
   且 `$Builder` / `$DistDir` / `$OutDir` 会先固化成绝对路径（中途 `Push-Location` 到 `out\pack`）

> CI 仍然会联网取 NuGet / npm registry / marketplace action —— 那是任何构建都免不了的；
> 这一层保证的是**安装器工具链只来自 Kirara 官方发布的最新 Release 产物**，
> 不从任何其它来源拉源码或下二进制。

## `packaging` 层：配置与宿主不能各说各话

`packaging/packaging.config.json` 是「应用侧」的唯一事实来源：安装目录、ARP 名称、
旧品牌兼容名、卸载时要清理的注册表 / 计划任务 / 快捷方式 / 用户数据目录、UAC 策略、
协议文件、运行库。安装器只读它，所以「改了宿主却忘了改配置」只能在这里发现 ——
每一项都拿 `src/Host/AppPaths.cs` / `Autostart.cs` 里的常量交叉断言，而不是在检查里
再抄一遍字面量（`AppPaths.cs` 里 `ProductName + ".exe"` 这类表达式会被解析后求值）。

## `-SelfTest`：证明这套检查不是空壳

检查工具最大的风险是「跑通了但其实什么都没查」。`-SelfTest` 会注入 10 个错误，逐个确认
对应层会失败：把 kachina 源码搬回仓库、工作流里加一条 `Invoke-WebRequest`、把打包脚本
的 `$KiraraRepo` 改名、把 `build.yml` 里的 Kirara Release 仓库改成别家、让 `-BuilderPath`
不再跳过 Kirara 查找、让 builder 路径不再固化绝对路径、放一个语法错误的 `.ps1`、改坏
`packaging.config.json` 的 `exeName`、拿掉 `ProcessRunner` 超时路径的 `KillTree`、
删掉 `GameLocator` 剪枝表里的系统目录行。

自检会临时改写**仓库里的真实文件**，因此有两个保护：

1. **仓库改动锁**（`tools/devcheck/.repo-lock`）：同一工作区同时只允许一个自检进程，
   第二个进程会明确报「另一个 devcheck 正持有仓库改动锁（PID …）」而不是互相污染。
2. **磁盘备份 + 启动清场**（`tools/devcheck/.selftest-backup/`）：注入前把原始内容落盘，
   进程被杀（Ctrl+C、CI 取消）后下次运行会先按备份恢复再开工。

两个目录都在 `.gitignore` 里。

## 日志里哪些 `Warning` / `error` 是正常的

| 字样 | 来源 |
| --- | --- |
| `warning NU1900` / `NU1901` 之类的 NuGet 源提示 | 本机 NuGet 配置，与代码无关 |
| `hosttest` 在非 Windows 上的 `SKIP: 非 Windows：测试台依赖 WindowsDesktop 运行时…` | 断言在 windows-latest 上真跑，本地只验证能编过 |
| `ui` 层的 `Some chunks are larger than 500 kB` | 单文件内联的 Web UI 产物本来就这样 |

## 跨平台的坑（都在 CI 上真实踩过）

- **换行**：`actions/checkout` 在 windows-latest 上默认 `core.autocrlf=true`，检出成 CRLF
  会让依赖 LF 的检查（prettier、自检里的字符串替换）失败。仓库根的 `.gitattributes`
  （`* text=auto eol=lf`）是唯一的换行约定；自检里对宿主源码做替换时也会先把 CRLF
  归一化成 LF。
- **管道死锁**：`Invoke-Native` 必须并发读 stdout / stderr —— Windows 上管道缓冲只有
  4 KB，串行读会死锁。两个流读完再拼接，所以日志里 stderr 一律排在 stdout 后面，
  中间会插一行分隔说明。
- **Node 弃用告警**：npm / npx / node 自己会打 `DEP0040`（punycode）、`DEP0169`
  （`url.parse()`），跟本仓库无关，工作流里用 `NODE_NO_WARNINGS=1` 静音。

## 工作流里的 action 版本

工作流里 action 的版本下限（低于它的版本会在 runner 上打 Node 20 弃用告警）：`actions/cache` ≥ v5、`pnpm/action-setup` ≥ v6、`actions/download-artifact` ≥ v7、`actions/checkout` ≥ v5、`actions/setup-node` ≥ v5、`actions/upload-artifact` ≥ v6、`actions/setup-dotnet` ≥ v5。

`ci` 层会解析上面这一行并与 `.github/workflows/*.yml` 里实际用到的版本比对：
升工作流时忘了同步这里（或反过来）都会失败。

## 维护约定

- 新增一层：在 `lib/Layers.ps1` 写 `Test-*`，在 `devcheck.ps1` 的 `$validLayers` /
  `$wanted`（`all` 集合）/ `switch` 三处登记，并在本文的表格里写清楚它检查什么。
  `ci` 层会断言 `all` 集合里的每一层都在 `devcheck.yml` 里真跑过。
- 改了 `src/Host` 里的品牌名、任务名、快捷方式名或数据目录：`packaging` 层会立刻报错，
  同步改 `packaging/packaging.config.json`。
- 安装器侧（Kachina 源码、卸载安全阀、builder）的检查在 Kirara 仓库，
  这里的改动不要试图去覆盖那边。
