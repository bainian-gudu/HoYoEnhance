# packaging/ — 安装包配置与打包脚本

本目录只放「应用侧」的打包配置与入口脚本。安装器工具链在独立项目
[Kirara](https://github.com/bainian-gudu/Kirara)：上游
[kachina-installer](https://github.com/YuehaiTeam/kachina-installer) 的源码快照、本地
补丁与 `kirara-builder` 都在那边，安装器的实现、构建前置与升级步骤也记在那个仓库。

```text
packaging/
├── README.md               本文件
├── packaging.config.json   安装包配置（安装目录、ARP 名称、运行库、协议正文、界面资源、
│                           卸载时清理的注册表 / 计划任务 / 快捷方式 / 用户数据目录）
├── left.webp               安装界面左栏图（配置里的 imageFile）
└── pack.ps1                打包总入口：dist\ → Install.exe / 便携 zip / 便携 7z
```

## 快速开始

在仓库根目录执行：

```powershell
# 编译 UI + Stub + Host，再打包安装包
.\build.ps1

# 已经编译过、只想重新打包
.\packaging\pack.ps1

# 只编译，不打包
.\build.ps1 -SkipSetup
```

`pack.ps1` 默认到同级目录 `..\Kirara` 取 builder；Kirara 不在那里、或手上已经有现成的
builder 时用参数指定：

```powershell
.\build.ps1 -KiraraRepo D:\src\Kirara          # 指定 Kirara 项目路径
.\build.ps1 -BuilderPath D:\kirara-builder.exe # 直接用现成 builder
.\build.ps1 -SkipBuilderBuild                  # 不自动构建（要求默认位置已有 builder）
```

CI 不检出 Kirara 源码，直接下载它的最新 Release 产物，见下「CI」。

## 产物

统一落在仓库根的 `artifacts\`：

| 文件 | 说明 |
| --- | --- |
| `HoYoEnhance.Install.<版本>.exe` | 离线安装器。装完的安装目录里含卸载程序与更新程序 |
| `HoYoEnhance-portable-win-x64.zip` | 便携包（内含更新程序，可直接升级） |
| `HoYoEnhance_v<版本>.7z` | 便携 7z（本机检测到 7-Zip 时才生成） |

## 打包步骤（`pack.ps1` 内部做的事）

`pack.ps1` 按以下三步生成更新器、索引和离线安装器：

```powershell
# 1) 更新器（也会被塞进便携包，用于在线升级）
kirara-builder.exe pack -c packaging\packaging.config.json -o <app>\<更新程序>.exe

# 2) 生成 metadata + 分块 hashed 目录
kirara-builder.exe gen -j 6 -i HoYoEnhance -m metadata.json -o hashed `
    -r bainian-gudu/HoYoEnhance -t <ver> -u .\<app>\<更新程序>.exe

# 3) 离线安装器
kirara-builder.exe pack -c packaging\packaging.config.json -m metadata.json -d hashed `
    -o <HoYoEnhance 安装包>.exe
```

中间目录用 `out\pack\`（已被 `.gitignore` 排除）。
> 不要用 `build\`：Windows 路径大小写不敏感，会和历史上的 `Build\` 目录混淆。
> 上述命令里的产品标识与输出文件名以 `packaging\pack.ps1`、`packaging.config.json`
> 的实际配置为准。

## 配置项

`packaging.config.json` 是「应用侧」的唯一事实来源：安装目录、ARP 名称、运行库、协议
正文、界面资源、卸载时要清理的注册表 / 计划任务 / 快捷方式 / 用户数据目录。下面几项
跟着宿主行为走 —— 改了宿主就要一起改，`tools/devcheck` 的 `packaging` 层拿宿主常量
交叉断言，漏改会直接报错。

### `extraUninstallRegistry` — 卸载时回收宿主的开机自启值

```json
"extraUninstallRegistry": [
  { "hive": "HKCU", "key": "Software\\Microsoft\\Windows\\CurrentVersion\\Run", "value": "HoYoEnhance" }
]
```

| 字段 | 说明 |
| --- | --- |
| `hive` | `HKCU` / `HKLM` / `HKCR` / `HKU`（大小写不敏感，也接受全称） |
| `key` | 子键路径 |
| `value` | 给了就只删这一个值；省略则删整棵子键，慎用 |

宿主把普通权限的开机自启写在 `HKCU\...\Run` 的 `HoYoEnhance` 值上
（`src/Core/Autostart.cs`），所以卸载必须回收它。卸载器通常以管理员身份运行，那时
`HKCU` 指向的是管理员账户，因此这里声明 `HKCU` 时它会额外遍历已加载的用户配置单元，
确保删掉的是当初装机用户的那一份。ARP 卸载项由安装器按 `regName` 自己删，不要在这里
重复声明。

### `extraUninstallScheduledTasks` — 卸载时回收宿主的管理员自启任务

```json
"extraUninstallScheduledTasks": [
  "HoYoEnhance.AutoStart"
]
```

同时开「开机自启动」与「启动时自动以管理员权限运行」时，宿主改登记任务计划程序里的
最高权限登录任务（`src/Core/Autostart.cs`）。它既不是文件也不是注册表值，只能按任务名
回收；漏了它，卸载后每次登录都会去拉起一个已经不存在的 exe。这里填**任务名**，必须带
产品前缀（`regName` 开头），通配符会被拒绝。

### `extraUninstallLnkNames` — 卸载时回收宿主自建 / 改名的快捷方式

```json
"extraUninstallLnkNames": [
  "HoYoEnhance.lnk",
  "Uninstall HoYoEnhance.lnk"
]
```

只写**文件名**：目录由安装器按 shell 的桌面 / 开始菜单位置解析，用户桌面被 OneDrive
重定向也能命中。完整名单以 `packaging.config.json` 为准。

### `userDataPath` — 卸载时清理用户数据（勾选后才生效）

```json
"userDataPath": [
  "%LOCALAPPDATA%/HoYoEnhance",
  "%APPDATA%/HoYoEnhance",
  "%USERPROFILE%/Documents/HoYoEnhance"
]
```

这三条不是历史兜底，而是宿主当前就在用的回退链：`src/Core/AppPaths.cs` 依次尝试
LocalAppData → Roaming AppData → 文档，用**第一个能创建并通过写探测的**目录。三条都
要声明，否则换了落盘位置的用户数据清不掉。

没勾选「同时删除用户数据」时一个数据目录都不会碰（安装器收到的是空数组）；此时仍会
删掉快捷方式与开始菜单里的产品文件夹。

### `agreementFile` / `agreementFormat` / `agreementTitle` — 可配置的用户协议

```json
"agreementFile": "../USER_AGREEMENT.txt",
"agreementFormat": "text",
"agreementTitle": "用户协议"
```

- `agreementFile` 相对**配置文件所在目录**解析，这里指向仓库根的 `USER_AGREEMENT.txt`；
  与 `pack.ps1` 的工作目录无关。
- `agreementFormat` 支持 `text` / `markdown` / `html`。
- 打包时正文被**内联进 exe**，所以离线安装器、`update.exe`、`uninst.exe` 共用同一份
  协议，运行期不读文件、不联网。
- 内联了正文就必须主动勾选同意才能点「安装」；`silent` / `non_interactive` 安装、更新、
  卸载不受影响。
- 读文件失败只打印 warning 并继续打包，此时界面上的协议入口退化为不可点击的文字。

### `imageFile` / `iconFile` — 安装界面左栏图与安装器图标

```json
"imageFile": "left.webp",
"iconFile": "../src/Host/Assets/app.ico"
```

| 字段 | 说明 |
| --- | --- |
| `imageFile` | 安装界面左栏的图片；内容按 `RIFF/WEBP` 魔数识别为图片，其它内容按 CSS 主题注入 |
| `iconFile` | 安装器与更新器 exe 的图标（`.ico`），三个 exe 的图标因此一致 |

- 两个字段都相对**配置文件所在目录**解析（左栏图就放在本目录），只在打包期消费，
  不会写进包内配置；点名了文件却读不到时**打包直接失败**，不静默回退。
- 两项都不写时用工具链内置的左栏图与图标。
- 这两个键由 Kirara 的 `pack` 读取。CI 用的是 Kirara **最新 Release** 的 builder：
  该 Release 若早于这两项支持，builder 只会把它们当未识别键打一行 warning 后忽略
  （安装器退回内置资源），不会让打包失败 —— 那时配置看起来生效了，实际没有。
- `pack.ps1` 不逐条传 `--image` / `--icon`：三步 `pack` 共用同一份配置，不会各带一套
  资源。命令行参数仍优先于配置，改用命令行时三步必须传同一份。
- 左栏图是 399×454 的竖图：安装界面左栏是 180px 宽的竖条，方形图（如应用图标位图
  `src/Host/Assets/app.webp`）会被 `object-fit: contain` 留出大片空白。当前这张与
  Kirara 内置的左栏图同源，写在配置里是为了不受工具链内置资源变化的影响。

## CI

两个工作流：**Devcheck**（`devcheck.yml`，push/PR 自动）与 **Build**（`build.yml`，
仅手动，出安装包）。

- **Devcheck** 跑 `tools/devcheck` 的全部检查层 + 自检 + Web UI 构建。其中 `vendor` 层
  断言「安装器工具链只来自 Kirara 的最新 Release」，`packaging` 层断言配置与宿主常量
  一致。
- **Build** 依次跑 `build-builder`（下载 Kirara 最新 Release 的 `kirara-builder.exe`）→
  `build-app` → `pack`（`packaging\pack.ps1 -BuilderPath packaging\tools\kirara-builder.exe`，
  给了 `-BuilderPath` 就不再查找 Kirara 目录）→ `package-e2e`（真装、真升、真卸，
  并解压便携包跑一次诊断启动）。

本仓库不内嵌 kachina 源码、不是 submodule；工作流与打包脚本除
`gh release download --repo bainian-gudu/Kirara` 外，没有任何其它 `git clone` /
`releases/download` / `Invoke-WebRequest` 之类的外部拉取动作。

### workflow 里那些看着多余的设置

工作流与脚本的注释只留一句指针，完整理由记在这里（约定：**思路、踩坑过程、参考的
项目 / issue 一律写文档，不写进代码注释**）。

| 设置 | 为什么 |
| --- | --- |
| `NODE_NO_WARNINGS: "1"` | `actions/setup-node` 自己（含它的 post-job 缓存步骤）会打 `[DEP0040] punycode` / `[DEP0169] url.parse()` 弃用告警，是 action 内部依赖的事，跟本仓库无关。`env` 对所有步骤生效，在这里统一静音 |
| `schtasks` 真建一次登录计划任务 | 任务计划程序 XML 的格式对不对只有它自己说了算，devcheck 那几层管不到。`build-app` 里用真实宿主导出 XML 再 `schtasks /Create`，避免「管理员自启动」因为 XML 被拒而静默退回普通权限自启 |
| 工作流里 action 的版本下限 | `actions/cache` ≥ v5、`pnpm/action-setup` ≥ v6、`actions/download-artifact` ≥ v7：这三个版本起 `action.yml` 声明 `node24`，低于下限的版本会在 runner 上打 Node 20 弃用告警。升级时对照 `action.yml` 的 `runs.using` 复核 |
| 仓库根的 `.gitattributes`（`* text=auto eol=lf`） | windows-latest 的 git 默认 `core.autocrlf=true`，检出成 CRLF 后 `prettier --check` 在 Windows 上必挂。详见 [`../tools/devcheck/README.md`](../tools/devcheck/README.md)「跨平台的坑」 |
| `git config --global init.defaultBranch main`（放在 checkout 之前） | `actions/checkout` 会先 `git init`，ubuntu 镜像上默认分支名还是 `master`，每次打 8 行 hint |

## 宿主侧的卸载前校验

程序内「卸载本软件」启动 `uninst.exe` 前会校验路径在自身目录内、文件名符合约定、不是
符号链接、目录不是系统 / 配置根目录；**宿主已提权时还要求安装目录位于 `Program Files`
下**，否则拒绝启动（避免普通用户在可写目录放同名 exe 借管理员令牌执行）。
