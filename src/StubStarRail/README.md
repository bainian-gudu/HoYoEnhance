# StarRailStub.dll 适配规格书（已实现，待 Windows 侧验证）

崩坏：星穹铁道的注入模块。宿主侧已经按「独立模块」接好，本文件记录**要做到什么、
怎么做、怎么验**，以及实现前为什么**刻意不塞一个空壳 DLL**。

## 〇、实现状态（2026-10-07）

源码已经落地，当前等待 Windows 侧的 MSVC 编译与游戏内验证。定位层已改为
**动态特征码**（不写死 RVA），对 4.5.0 与 2026-09-28 新版 `GameAssembly.dll`
两版分别扫描：必需目标全部唯一命中，路径查找与 `TextMeshProUGUI` 的多命中目标
命中数落在预期区间（证据见 `SIGNATURES.md`）：

| 文件 | 职责 |
| --- | --- |
| `dllmain.cpp` | 模块生命周期、共享内存连接、定位重试、状态机与错误码 |
| `Il2CppBridge.h/.cpp` | 动态特征码定位（唯一命中 + 相邻孪生校验）、函数头校验、IL2CPP 对象读写（类名 / 字符串 / float） |
| `AntiBlur.h/.cpp` | ① 反角色虚化：Hook `BaseShaderPropertyTransition` 相机 Dither，把 Camera 来源 alpha 压回 1.0；② 反场景景深：Hook `RPGDepthOfField.IsActiveImpl` 返回 false |
| `HideUid.h/.cpp` | 隐藏 UID 水印：路径查找（`GameObject.Find` + `GetComponent("UnityEngine.UI.Graphic")`）与文本识别（`Graphic` / `TMP_Text` / `TextMeshProUGUI` 的 Dirty 入口）两条路一起上；`RPGApplication.OnUpdate` 做路径查找、还原与状态上报 |
| `SIGNATURES.md` | 每条特征码的字节来源、两版命中数、候选身份与重推方法论 |
| `CMakeLists.txt` / `StarRailStub.def` | 独立构建 `StarRailStub.dll`，导出 `WndProc` |

**解耦约定**：原神 `src/Stub` 与星铁 `src/StubStarRail` 各自拥有完整的业务代码，
互不引用；两者只共用 `src/Common` 下与游戏无关的 `Scanner` / `PatternMatch` /
`IpcData`。公共扫描器由原神侧 `Scanner.cpp` 迁移而来，原神构建改引用
`../Common/Scanner.cpp`，功能与特征码不变。

构建入口已接进根目录 `build.ps1`：原神 Stub 构建完成后，会在独立的
`out/stub-starrail` 目录构建 `StarRailStub.dll` 并拷贝到 `dist`。

错误码（Host 状态栏按十六进制显示）：

| 错误码 | 含义 |
| --- | --- |
| `0xE101` | 找不到 `GameAssembly.dll` |
| `0xE102` | `RPGApplication.OnUpdate` 特征码定位失败 |
| `0xE103` | 相机 Dither 汇合入口与距离 / 高度兜底入口全部定位失败 |
| `0xE104` | `RPGDepthOfField.IsActiveImpl` 特征码定位失败 |
| `0xE105` | `Graphic.SetVerticesDirty` 特征码定位失败（含相邻孪生校验） |
| `0xE106` | 反虚化 Hook 创建失败 |
| `0xE107` | UID 隐藏 Hook 创建失败 |
| `0xE108` | `MH_EnableHook` 失败 |
| `0xE109` | 主线程 tick 踩到结构化异常 |
| `0xE10A` | MinHook 初始化失败 |
| `0xE10B` | 模块 PIN 失败 |

## 一、现状：缺这个模块会发生什么

- 宿主 `GameCatalog.StarRail.StubFileName = "StarRailStub.dll"`，只有在星铁档案里
  开启了画面效果（反角色虚化 / 反场景景深 / 隐藏 UID）时才会去注入，并且注入前做可信度校验
  （`ModuleTrust`）。
- DLL 不在 exe 旁时：状态栏显示「缺少 StarRailStub.dll（应位于 …）」，**不注入任何东西**，
  游戏进程保持干净。
- **星铁的帧率解锁不依赖本模块**：走 `src/Host/StarRailFpsRegistry.cs` 直接改注册表，
  所以缺模块只影响上面那三项画面效果。

## 二、实现前为什么不塞一个空壳 DLL

星铁进程里有 `mhypbase.dll` 反作弊。往这种进程里注入，只有**真的能干活**才值得冒暴露风险：
空壳 DLL 收益为零、暴露面照旧。所以在本模块具备真实功能前，保持「缺失即不注入」。

## 三、目标产物

| 项 | 约定 |
| --- | --- |
| 文件名 | `StarRailStub.dll`（与 `FpsUnlockerStub.dll` 并列放在 exe 旁） |
| 位数 / 运行库 | x64，静态 CRT（`/MT`），MinHook 直接编入（与 `src/Stub` 一致） |
| IPC | 复用 `src/Common/IpcData.h`（保留历史兼容映射名，实际值见源码） |
| Host 写入 | `AntiBlurPerspective`、`AntiBlurDof`、`HideUid`（外加协议里已有的其它字段） |
| Stub 写入 | `Status`（Waiting / Ready / Error / Exiting）、`AntiBlurState`（bit0 反角色虚化就绪 / bit3 景深就绪）、`HideUidState`（bit0 就绪 / bit1 生效中）、`LastError` |
| 状态机 | 首轮定位全部成功才置 `Ready`；任何一步失败置 `Error` + 错误码，**绝不半开**（避免游戏侧出现「一半功能生效」） |

宿主不需要再改：DLL 到位即可用；缺失时的提示、退避重试、托盘就绪标记都已实现。

## 四、定位策略：动态特征码唯一命中

> 2026-09-19 用 `im-remi/HSRGlobalMetadata`（测试版本 `OSPRODWin4.5.0`）对 4.5.0
> 生成了完整 `dump.cs`（238 万行 / 128 MB）与 `stringliterals.json`（11 MB），
> 用来确认类名、字段偏移与函数身份。**RVA 只作对照，不再写进代码** ——
> 2026-09-28 新版重编译后所有 RVA 整体平移，写死的地址必然失效。
>
> 两条死路先记下来：`GameAssembly.dll` 只导出 `il2cpp_get_api_table` 一个符号
> （RVA `0x644ba0`，ImageBase `0x180000000`）；`global-metadata.dat` 是**分区段加密**的
> （文件头是 `MHY\0`，标准 Il2CppDumper 直接报 "Metadata file not found or encrypted"）。

1. **只认特征码**：每个目标一条长结构特征码，通配符只打在版本间会变的字节上
   （rip 相对位移、rel32 调用目标、字段偏移立即数），其余是结构常量。IL2CPP 的
   引擎方法在模块里是 `jmp [rip+off]` 跳转桩，调用桩等于调用真实实现。
2. **唯一命中才接受**：必需目标要求全模块唯一命中，多命中一律判失败并报 `Error`。
   宁可让宿主提示「特征码未命中，等待版本适配」，也不把无关函数当成目标
   （参考实现的多候选硬试会调用到无关函数，有崩游戏的风险）。
3. **多命中目标用相邻结构校验**：`Graphic.SetVerticesDirty` 天然 2 命中，用紧邻的
   孪生函数 `Graphic.SetLayoutDirty`（字段偏移各 +1）在 `0x80` 窗口内二次校验，
   恰好一个候选满足才接受。
4. 定位结果必须落在 `GameAssembly.dll` 映像内且可执行，所有读写都过
   `VirtualQuery` 权限校验 + SEH。

每条特征码的字节来源、两版命中数与候选身份见同目录 `SIGNATURES.md`；
版本更新后的重推流程（命中数比对 + 相似性比对 + 反汇编复核）也记在那里。
离线比对工具是 `tools/starrail-signatures/check_signatures.py`（纯 Python 无依赖）。

## 五、功能 1：隐藏 UID（路径查找 + 文本识别，两条路一起上）

UID 水印有两个来源可能：一是挂在固定层级节点上的 `Graphic`（`UI.Text` / `Image` /
Sprite 都可能），二是运行时动态创建的文本组件。任何单一路径都有覆盖不到的角落，
所以这里**两条路同时启用，互为兜底**：

### A. 路径查找（4.5.0 旧方式，兜底路径）

在主线程 tick（`RPGApplication.OnUpdate`，每 15 帧一次）里按固定层级路径找
`GameObject`，再取它上面的 `UnityEngine.UI.Graphic`，直接写 `m_Color.a = 0`：

```
/UIRoot/AboveDialog/BetaHintDialog(Clone)/Contents/VersionText
/UIRoot/Page/MobilePhoneMainPage(Clone)/Content/Content/LeftPlane/Tittle/UID/NumText
```

这条路**不读文本、不看组件类型** —— 水印无论用 `UI.Text`、TMP 还是 `Image` /
`Sprite`，只要挂在节点上就能抓到，是文本识别失效时唯一可靠的兜底。

`GameObject.Find` / `GameObject.GetComponent(string)` 的特征码是 IL2CPP 的 icall
转发桩，模块里天然多命中（Find 旧版 9 / 新版 8 处，GetComponent 两版各 8 处）。
**不能逐个候选硬试**：多出来的命中大多是无关函数，反复调用会误触发副作用
（4.6 实测：开启遮挡 UID 后打开任意页面出问题、转视角卡顿）。因此：

- `Find` 只调用文档记录的那个下标（`Il2CppBridge::kPreferredFindIndex`，4.5.0 与
  4.6 都是第 4 个命中），不再遍历；
- `GetComponent` 在有效 `GameObject` 上试，用类名（含 `Graphic`）或对象头 + `m_Color`
  四分量合法来挑真身，确认后缓存。

全部调用都套 SEH；路径当前不存在就下次重试。

### B. 文本识别（主路径）

在 UI 文本重建的必经点上拿 `Graphic` 实例，读文本内容判定，**不依赖任何 UI 节点名**：

1. Hook 三个入口：
   - `UnityEngine.UI.Graphic.SetVerticesDirty`（唯一命中 + 相邻孪生校验），覆盖
     `UI.Text` 及其子类；
   - `TMPro.TMP_Text.SetVerticesDirty`（唯一命中），TMP_Text 覆写了基类实现，
     不挂它就会漏掉全部 TMP 文本；
   - `TMPro.TextMeshProUGUI` 的两个同构 Dirty 入口（`SetVerticesDirty` 与
     `SetMaterialDirty` 编译成了同样的指令序列，静态无法区分，两个都挂）；
2. **不按类名过滤**：4.6 起 `ObjectClassName` 对部分对象返回 null，「拿不到类名就
   跳过」会让识别彻底失效；而且读类名每次要做几次 `VirtualQuery`，放在
   `SetVerticesDirty` 热路径会拖慢 UI。直接按文本字段判定，命中后才读类名写诊断；
3. 同时尝试两个文本字段偏移，取真正是 il2cpp string 的那个：
   - `UnityEngine.UI.Text.m_Text` `+0xF8`（dump.cs 实测）；
   - `TMPro.TMP_Text.m_text` `+0xF0`（dump.cs 实测）；
4. 判定规则：
   - 文本含 `UID`（忽略大小写）且带 6~12 位连续数字 → 判定为 UID 水印，
     同时把这串数字记为「已知 UID」；
   - 文本本身就是 6~12 位纯数字，且与「已知 UID」完全一致 → 判定为 UID
     （覆盖只显示数字的资料页）；
5. 命中后把 `Graphic.m_Color` 的 alpha 写 0
   （**偏移已复核**：`UnityEngine.UI.Graphic.m_Color // Offset: 0x20`，alpha 在
   `+0x20+0x0C`），并记录原值以便关闭开关时还原。

`RPGApplication.OnUpdate` 只作为主线程入口做路径查找、关闭时的还原与状态上报。
所有读写都先做可读 / 可写校验再套 SEH；还原前重新校验类名，避免对象被 GC 回收后
误写无关对象。状态位 `IpcHideUidState::PathReady` 反映路径查找是否已探测到有效
入口，便于 Host 区分「两条路都就绪」与「只有文本识别就绪」。

不要用「关掉 `s_UICamera`」那种做法（Pipsi 的 `hide_ui.cpp`）：会把整个 HUD 一起藏掉。

### 4.6 适配记录

4.6 上 `ObjectClassName` 对部分对象返回 null，路径查找的多候选硬试还会误调无关的
il2cpp 函数（表现为「开关已开但水印还在」+ 开任意页面出问题 / 转视角卡顿）。适配
时曾临时加过 `starrail-stub-diag.log` 运行时日志（`[resolve]` / `[path]` /
`[graphic]` / `[uid]` 等），问题定位并修好后已随本次收尾一并移除。

## 六、功能 2 / 3：两项反虚化

「2 个反虚化」指两条互相独立的链路：

- **反角色虚化**：相机靠近角色时的半透明 Dither（下面第一小节）；
- **反场景景深虚化**：背景景深模糊后处理（下面第二小节）。

### 反角色虚化（相机 Dither）

早期实现曾 Hook `VCameraDOFEffectOverride`，但那是**场景景深 DOF**，不是
「角色靠近镜头变半透明」的机制；所以 UI 显示已开启，实际镜头拉近仍会透明。
Windows 侧实测也确认：同一 DLL 的隐藏 UID Hook 正常，排除注入与反作弊拦截。

真正的链路是 `RPG.Client.BaseShaderPropertyTransition` 的相机 Dither。
反汇编确认（dump.cs，4.5.0；下面的 RVA 只作对照，代码里不写死）：

```
public enum DitherSourcePriority {
    Default = 0,
    Camera  = 1,  // 相机碰撞 / 靠近角色虚化
    Logic   = 2,  // 剧情 / 任务淡入淡出
}

public class BaseShaderPropertyTransition : UnityEngine.MonoBehaviour {
    public float TargetDitherAlpha        // Offset: 0x20
    public DitherSourcePriority CurrentControlSource // Offset: 0x28
    public float ElevationDitherAlpha     // Offset: 0x2C
    public float DistanceDitherAlpha      // Offset: 0x30

    // 私有汇合入口：value / priority / force
    private bool HBPKIAAKMPE(float, DitherSourcePriority, bool) // 4.5.0 RVA 0x19F1BE00
    public void SetDistanceDitherAlphaValue(float, bool)        // 4.5.0 RVA 0x19F1C0E0
    public void SetElevationDitherAlphaValue(float)             // 4.5.0 RVA 0x19F1BD70
    public void ClearCameraDitherAlpha()                        // 4.5.0 RVA 0x19F1C810
}
```

- 首选：Hook 私有汇合入口 `HBPKIAAKMPE`。所有距离 / 高度相机 Dither 都会汇入这里；
  仅当 `priority == Camera(1)` 且用户开关开启时，把 `value` 改成 `1.0`，
  再调用原函数。`Logic(2)` 与默认来源不受影响。
- 兜底：私有入口定位失败时，Hook `SetDistanceDitherAlphaValue` 与
  `SetElevationDitherAlphaValue`，同样只在开关开启时把入参改成 `1.0`。
- 关闭开关时不改写任何参数，完整保留游戏原始表现。

### 反场景景深虚化（DOF）

`RPG.CustomRP.RPGDepthOfField.IsActiveImpl`（4.5.0 RVA `0x1858CCF0`）是景深后处理的
总开关。Hook 后：开关开启时直接返回 false（后处理不参与渲染），关闭时转调原函数，
画面与未注入完全一致。这一项与「角色靠近镜头半透明」是不同链路，两者互不影响。

## 七、验证清单（需要 Windows + 星铁）

1. 不放 DLL：宿主状态栏显示「缺少 StarRailStub.dll」，游戏内无任何变化。
2. 放好 DLL + 只开「隐藏 UID」：水印消失，关闭开关能恢复；游戏退出后 DLL 不在进程里。
3. 只开「反角色虚化」：镜头拉近角色不再透明化；切场景后仍有效。
4. 只开「反场景景深虚化」：背景景深模糊消失，关闭开关能恢复。
5. 三项同时开：互不干扰，运行状态卡三项都显示「已生效」。
6. 版本更新后再跑：定位失败要变成 `Error` 而不是崩游戏（宿主会显示错误码并按退避重试）。
7. 全程不修改游戏目录里的任何文件（只读 + 内存操作）。

## 八、风险与边界

- 星铁有 `mhypbase.dll` 反作弊；请仅在单机环境下使用本模块。
- 默认关闭，只在用户显式开启时注入；失败即卸载，不做兜底 patch。
- 本模块只做「反角色虚化 / 反场景景深 / 隐藏 UID」三项，不碰帧率（帧率归注册表），
  也不碰存档与网络。

## 九、继续推进需要什么（一步采集）

需要一次可运行的 Windows + 星铁环境，采到目标版本的 IL2CPP 材料：
导出符号是否存在、类 / 字段真实偏移、调用点、UI 节点名。

静态部分已经采过一轮（落到工作区 `hsr-capture\`）：Unity 2019.4.34.11513818、
`StarRail.exe` 684 KB、`GameAssembly.dll` 536 MB、`global-metadata.dat` 100 MB
（两个大文件都已复制到 `bin\`，SHA256 与 `info.txt` 记录一致）、
`mhypbase.dll` 1.0.1.40399480（26 MB，在游戏根目录，不在 `StarRail_Data\Plugins` 下）。
注册表 `GraphicsSettings_Model_h2986158309` 实测是 `REG_BINARY` + UTF-8 JSON，当前 `"FPS":120`。

**模块基址采不到，而且不需要采**：游戏运行时实测，`Process.Modules` 返回空集合，
Toolhelp32 的 `CreateToolhelp32Snapshot` 报 `win32 error 5 (Access is denied)` ——
`HoYoProtect`（`C:\WINDOWS\system32\HoYoKProtect.sys`）内核驱动在运行，跨进程模块查询
一律被拒，提权也大概率无效。Stub 在游戏进程内用 `GetModuleHandle(L"GameAssembly.dll")`
自己取基址即可，不依赖这份数据。

**dump.cs 也已经拿到**（2026-09-19）：`global-metadata.dat` 被米哈游分区段加密，标准
Il2CppDumper 解不了（报 "Metadata file not found or encrypted"）；改用
`im-remi/HSRGlobalMetadata` 静态提取，它的测试版本 `OSPRODWin4.5.0` 与本机 4.5.0 正好对上，
产出 238 万行 / 128 MB 的 `dump.cs` 与 11 MB 的 `stringliterals.json`，
产物在工作区 `hsr-game-view/dump/`。

版本更新后重新提取（全程 WSL 内，不需要启动游戏）：

```bash
# 1) 装 .NET 10 SDK 到工作区（不需要 root）
./dotnet-install.sh --channel 10.0 --install-dir <tools>/dotnet

# 2) 把游戏目录映射成 WSL 侧视图，必须包含：
#    GameAssembly.dll
#    StarRail_Data/il2cpp_data/Metadata/global-metadata.dat
#    StarRail_Data/il2cpp_data/Metadata/startup-metadata.dat   <- 提取器两个都要

# 3) 跑提取器
dotnet run -c Release --no-build <game-view-dir>
# 产物：<game-view-dir>/dump/dump.cs、stringliterals.json
```

至此写 Stub 需要的材料已经齐了：目标偏移（`EnableDOF` 0x18、`m_Color` 0x20）、
目标方法 RVA、两条 UID 节点路径，以及两条特征码各自该选第几个命中。

采集用仓库自带的只读脚本，**不需要安装任何东西**（只用 Win10/11 自带的 Windows PowerShell 5.1）：

```
双击：tools\hsr-capture.cmd
或：  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<仓库>\tools\hsr-capture.ps1"
```

推荐先把游戏开到有 UID 水印的界面，再运行一次（这样能采到模块基址）。脚本只读，
不改游戏目录、不写注册表、不注入进程，结果落到仓库上一级的 `hsr-capture\`：

| 产物 | 用途 |
| --- | --- |
| `bin\GameAssembly.dll`、`bin\global-metadata.dat` | 离线分析：`objdump -p` 确认导出（只有 `il2cpp_get_api_table`）；Il2CppDumper 出 `dump.cs` 拿类 / 字段偏移与方法地址 |
| `modules.txt` | 模块列表（被 `HoYoProtect` 拒绝时记录 win32 错误码与提权状态）+ 反作弊驱动状态 |
| `registry.txt` | `GraphicsSettings_Model_h*` 的真实值名与 `FPS` 字段（核对注册表解锁实现） |
| `unity-log-tail.txt` | 确认 Unity 日志目录与 Unity 版本 |
| `info.txt` | 各文件版本 / 大小 / SHA256，用于判断适配的目标版本 |

实现顺序（已完成）：按第四节把动态特征码定位打通 → 隐藏 UID → 反角色虚化 →
反场景景深 → 按第七节清单在 Windows 上验收。

在那之前，「模块缺失即不注入」就是最稳的状态：帧率解锁照常可用，画面效果保持未就绪提示。
