# HoYoEnhance

原神与崩坏：星穹铁道的帧率解锁器。支持国服和国际服，两款游戏的设置独立保存。

> 本项目为个人自用的第三方工具，使用 AI 辅助开发，未获米哈游授权，也未经独立安全审计。部分功能使用进程注入，可能违反游戏服务条款并导致账号处罚。使用前请阅读[用户协议](USER_AGREEMENT.txt)，风险自负。

## 下载

从 [GitHub Releases](https://github.com/bainian-gudu/HoYoEnhance/releases/latest) 下载：

- **安装版**：运行 `HoYoEnhance.Install.<版本>.exe`。
- **便携版**：解压 ZIP 或 7z，再运行 `HoYoEnhance.exe`。

需要 Windows 10（1607 或更高）或 Windows 11（x64），以及 WebView2、.NET Desktop Runtime 9。缺少运行库时按提示安装。

## 使用

1. 在顶部切换游戏，到「游戏设置」选择游戏主程序，不要选启动器。
2. 关闭游戏内的垂直同步（V-Sync），在 HoYoEnhance 打开解锁服务总开关和当前游戏的「帧率解锁」。
3. **原神**：设置目标 FPS，保持工具运行，启动游戏后自动应用。**崩坏：星穹铁道**：通过注册表设置固定 120 FPS；已打开游戏时需重启游戏生效。

两款游戏均可关闭角色虚化、隐藏 UID；移除水下马赛克仅限原神。需要关闭窗口后继续运行，可开启「启动后最小化到托盘」。

## 更新与卸载

- **更新**：在「关于」页检查更新。自动检查可在设置中关闭；工具不收集使用统计，也不上报崩溃。
- **卸载**：安装版可从 Windows「设置 → 应用」卸载；勾选「同时删除用户数据」才会清理配置、日志和缓存，可能影响本机其他用户。便携版退出后删除解压目录即可，用户数据会保留。

## 从源码构建

在 Windows 上使用 PowerShell 7、.NET 9 SDK、CMake、MSVC 和 Node.js 20+。运行 `.\build.ps1 -SkipSetup` 只编译，去掉 `-SkipSetup` 则同时打包。见[打包说明](packaging/README.md)和[开发自检](tools/devcheck/README.md)。

## 许可与致谢

本项目代码采用 [MIT](LICENSE)，[MinHook](third_party/minhook/LICENSE.txt) 采用 BSD-2-Clause。画面效果部分参考 [UnlockerIsland](https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.UnlockerIsland)（MIT）；安装工具链由 [Kirara](https://github.com/bainian-gudu/Kirara) 提供，源自未附许可文件的 [kachina-installer](https://github.com/YuehaiTeam/kachina-installer)。

图片素材不随本项目的 MIT 许可授权。游戏图标和角色形象归米哈游／HoYoverse；应用图标取自 [BetterGI](https://github.com/babalae/better-genshin-impact)（GPL-3.0），安装器配图取自 kachina-installer，界面横幅为 AI 生成的非官方插画。
