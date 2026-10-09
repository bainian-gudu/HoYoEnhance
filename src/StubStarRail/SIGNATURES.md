# StarRailStub 动态特征码规格（SIGNATURES）

本文件记录 `Il2CppBridge.cpp` 里每条特征码的字节来源、跨版本命中数与候选身份，
以及游戏版本更新后重新推导特征码的方法论。定位层**只认特征码、不认 RVA**：
IL2CPP 每次重编译都会整体平移函数 RVA，写死 RVA 必然失效。

## 一、方法论：命中数比对 + 相似性比对

特征码只保留跨版本稳定的结构字节（函数头、静态字段判空、字段偏移访问、跳转条件），
通配符只打在版本间会变的字节上（rip 相对位移、rel32 调用目标、立即数偏移）。

重新推导流程（工具：`tools/starrail-signatures/check_signatures.py`，纯 Python 无依赖）：

1. **命中数比对**：用旧特征码扫新 `GameAssembly.dll`。全模块唯一命中 → 直接可用，
   不用改；0 命中 / 多命中 → 进入相似性比对。
2. **相似性比对**：取旧特征码里一段长固定字节（去掉通配符）作锚点，扫新 DLL 得到
   候选集；对每个候选计算「固定字节命中率」（固定位置相同数 / 固定位置总数），
   按分数降序排列。得分最高的候选通常就是同一函数在新版本里的落点 —— 这正是
   「新旧特征是不是同一个函数」的定量回答。
3. **反汇编复核**：对候选函数反汇编，核对函数头、调用结构、字段偏移访问是否与旧版
   语义一致（例如 Dither 汇合入口的三个参数、DOF 的两个返回分支），确认身份。
4. **重推特征码**：在确认的候选里重新选一段跨版本稳定的字节，通配符只打变化的位移，
   回填 C++（`Il2CppBridge.cpp`）与工具（`check_signatures.py`）两侧，再对新旧两版
   各扫一遍，要求两版都唯一命中。

## 二、特征码清单

两个基线：

- **旧版（4.5.0）**：`GameAssembly.dll` 536,139,040 字节，2026-09-04 构建，
  SHA256 `6CE23AE2...3D27`。
- **新版**：`GameAssembly.dll` 543,524,648 字节，2026-09-28 构建，
  SHA256 `8CC73800...F1D3`（采集报告 `hsr-capture-latest/info.txt`）。

| 目标 | 4.5.0 RVA | 新版 RVA | 命中数（旧/新） | 说明 |
| --- | --- | --- | --- | --- |
| `RPGApplication.OnUpdate` | `0x1802FBF0` | `0x0DE0AD30` | 1 / 1 | UID 隐藏的主线程入口；跨版本字段偏移会变，立即数留通配 |
| `Dither.Merge` | `0x19F1BE00` | `0x0C7EBC40` | 1 / 1 | 相机 Dither 私有汇合入口，优先挂点 |
| `Dither.SetDistance` | `0x19F1C0E0` | `0x0C7EBF20` | 1 / 1 | 距离 Dither 公开入口，汇合入口失效时兜底 |
| `Dither.SetElevation` | `0x19F1BD70` | `0x0C7EBBB0` | 1 / 1 | 高度 Dither 公开入口，兜底 |
| `DOF.IsActiveImpl` | `0x1858CCF0` | `0x1CC15630` | 1 / 1 | 景深后处理总开关，返回 false 即跳过 |
| `Graphic.SetVerticesDirty` | `0x1B78C0C0` | `0x1F4C79B0` | 2 / 2 | UI 重建通知；多命中，需相邻孪生校验 |
| `TMP_Text.SetVerticesDirty` | `0x134E9970` | `0x1F2F77A0` | 1 / 1 | TMP 文本重建通知；TMP_Text 覆写了 Graphic 的实现 |
| `TextMeshProUGUI.Dirty` | `0x134EACD0` / `0x1352A360` | `0x1F2F8A80` / `0x1F337E50` | 2 / 2 | SetVerticesDirty 与 SetMaterialDirty 同构，两个都挂 |
| `GameObject.Find` | `0x1DEDE300` | `0x1F3C1130` | 9 / 8 | 路径查找兜底；多命中，按相对距离动态配对 |
| `GameObject.GetComponent` | `0x1DEDDE30` | `0x1F3C0C60` | 8 / 8 | 路径查找兜底；多命中，按相对距离动态配对 |

两版 RVA 均由 `check_signatures.py` 扫描得到，与旧版 `dump.cs` 里 dump 出的 RVA
逐条对齐；工具对两版分别运行都报告「全部目标唯一命中」。

表中后四项里，`TextMeshProUGUI.Dirty`、`GameObject.Find`、`GameObject.GetComponent`
是**预期的多命中目标**：工具的判定列为「多命中(预期)」，只校验命中数落在预期区间，
不要求唯一。它们的真身选择见 2.6 / 2.7 / 2.8。

### 2.1 `RPGApplication.OnUpdate`

```
56 57 48 83 EC 48 0F 29 7C 24 30 0F 29 74 24 20 48 89 CE 80 3D ?? ?? ?? ?? 00
0F 85 ?? ?? ?? ?? 80 7E ?? 00 0F 84 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ??
80 B9 ?? 00 00 00 00 0F 84 ?? ?? ?? ??
```

函数头 `56 57 48 83 EC 48` 与两条 `static field == 0` 判空跳转是稳定结构；
`80 3D ?? ?? ?? ?? 00`（rip 相对静态字段）与 `80 7E ?? 00` / `80 B9 ?? 00 00 00 00`
（实例字段偏移）里的位移、偏移立即数随版本变，留通配。

4.5.0 推导时字段偏移写死（`+0x18` / `+0xC7`），新版偏移改成 `+0x22` / `+0xB7`，
导致旧特征码 0 命中；重推时把这些立即数改成通配后，两版都唯一命中。

### 2.2 `Dither.Merge`（`BaseShaderPropertyTransition` 私有汇合入口）

```
41 56 56 57 55 53 48 83 EC 50 0F 29 7C 24 40 0F 29 74 24 30 44 89 CD 44 89 C7
0F 28 F9 48 89 CE 80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ??
80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ??
```

寄存器保存序列 `41 56 56 57 55 53` + 两处 `static field == 0` 判空构成唯一结构。

### 2.3 `Dither.SetDistance` / `Dither.SetElevation`

```
（距离）56 53 48 83 EC 38 0F 29 74 24 20 44 89 C3 0F 28 F1 48 89 CE 80 3D ?? ?? ?? ?? 00
      75 ?? 80 7E 36 00 74 ?? 0F 57 C0 F3 0F 5F C6 F3 0F 10 0D ?? ?? ?? ??
      F3 0F 5D C8 F3 0F 11 4E 30 F3 0F 59 4E 2C
（高度）56 48 83 EC 30 0F 29 74 24 20 0F 28 F1 48 89 CE 80 3D ?? ?? ?? ?? 00
      75 ?? 80 7E 36 00 74 ?? 0F 57 C0 F3 0F 5F C6 F3 0F 10 0D ?? ?? ?? ??
      F3 0F 5D C8 F3 0F 11 4E 2C F3 0F 59 4E 30
```

结构锚点：距离入口先写 `+0x30`（DistanceDitherAlpha）再读 `+0x2C`
（ElevationDitherAlpha），高度入口正好相反。这一对互逆的字段访问是区分两个
孪生函数的关键，`+0x36` 字段判空保持固定（未随版本变）。

### 2.4 `DOF.IsActiveImpl`（`RPG.CustomRP.RPGDepthOfField`）

```
48 83 EC 28 80 79 18 00 74 ?? 48 8B 0D ?? ?? ?? ?? 80 B9 ?? 00 00 00 00 74 ??
48 8B 05 ?? ?? ?? ?? 48 8B 80 ?? ?? 00 00 48 85 C0 74 ?? 80 78 ?? 00 0F 95 C0
48 83 C4 28 C3 31 C0 48 83 C4 28 C3
```

尾部两个返回分支（`48 83 C4 28 C3` 与 `31 C0 48 83 C4 28 C3`）是 `IsActiveImpl`
「读静态配置 → 读实例开关 → 返回 bool」的独有结构，用来拉开区分度。字段偏移
（`+0xB7` / `+0x2A70` / `+0x10`）随版本变，全部留通配；函数头
`48 83 EC 28 80 79 18 00` 里的 `+0x18` 也是实例字段偏移。

> 注意：`80 79 18 00` 中的 `18` 是实例字段偏移，理论上也会变；本次新版恰好仍是
> `+0x18`，所以暂时保留为固定字节以提高区分度。若后续版本 0 命中，优先把这里的
> `18` 改成 `??` 再扫。

### 2.5 `Graphic.SetVerticesDirty`（多命中，相邻孪生校验）

```
56 48 83 EC 20 48 89 CE FF 15 ?? ?? ?? ?? 84 C0 74 ??
C6 86 99 00 00 00 01 EB ?? 48 89 F1 FF 15 ?? ?? ?? ?? 84 C0 74 ??
C6 46 58 01 48 89 F1 E8 ?? ?? ?? ?? 48 8B 46 68 48 85 C0 74 ??
4C 8B 40 18 48 8B 50 28 48 8B 48 40 48 83 C4 20 5E 49 FF E0 90
```

这条在模块里天然 2 命中：另一处是 SRDebugger 的 `ConsoleLogControl.Update`，
两者字节几乎一致（都调用虚函数并写 `m_VerticesDirty` 之类的标志）。不能按
「第几个命中」挑选，改用**相邻孪生函数**二次校验：

```
（孪生）56 48 83 EC 20 48 89 CE FF 15 ?? ?? ?? ?? 84 C0 74 09 C6 86 9A 00 00 00 01
```

紧跟在 `Graphic.SetVerticesDirty` 之后的是 `Graphic.SetLayoutDirty`，结构完全相同，
只有两个字段偏移各 `+1`（`0x99→0x9A`、`0x58→0x59`）。这一对孪生函数是
`Graphic.SetVerticesDirty` 独有的相邻结构，`ConsoleLogControl.Update` 后面没有。

校验规则：候选后面 `0x80` 字节（4.5.0 实测距离 `0x60`，留一倍余量）内必须出现
孪生特征；恰好一个候选满足才接受，多于一个仍判失败。

### 2.6 `TMP_Text.SetVerticesDirty`

```
56 57 53 48 83 EC 20 48 89 CF FF 15 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ??
48 8B B7 ?? ?? ?? ?? 48 85 F6 0F 84 ?? ?? ?? ?? 48 83 7E 10 00
```

`TMPro.TMP_Text` 覆写了 `Graphic.SetVerticesDirty`，只挂基类会漏掉全部 TMP 文本。
两版都唯一命中。`48 8B B7 ?? ?? ?? ??` / `48 83 7E 10 00` 里的字段偏移随版本变，
留通配；函数头 `56 57 53 48 83 EC 20 48 89 CF` 与调用虚函数后的 `84 C0` 判空
是稳定结构。

### 2.7 `TextMeshProUGUI.Dirty`（多命中，两个都挂）

```
56 57 48 83 EC 28 48 85 C9 74 ?? 48 89 CE 48 83 79 10 00 74 ??
48 89 F1 FF 15 ?? ?? ?? ?? 84 C0 74 ?? 48 8B 05 ?? ?? ?? ??
48 8B B8 ?? ?? ?? ??
```

`TMPro.TextMeshProUGUI` 又覆写了 `TMP_Text` 的实现，因此必须单独挂它自己的入口。
两版都恰好命中 2 处：`SetVerticesDirty` 与同构的 `SetMaterialDirty` —— IL2CPP 把
这两个「判空 → 置脏标志 → 通知 Canvas」的方法编译成了同样的指令序列，固定字节
完全相同，静态无法区分。

处理方式：**两个候选都挂**。命中的那个是真正的文本重建通知点；另一个只是多一次
无害的判定，不会改变原函数行为。

### 2.8 `GameObject.Find` / `GameObject.GetComponent(string)`（路径查找兜底）

```
（Find）       48 FF ?? ?? ?? ?? ?? 66 0F 1F 84 00 00 00 00 00 48 83 EC 28 C7 44 24 20
（GetComponent）48 8B 05 ?? ?? ?? ?? 48 FF E0 66 0F 1F 44 00 00
               48 8B 05 ?? ?? ?? ?? 45 31 C0 48 FF E0 0F 1F 00
```

这是 4.5.0 旧实现的路径查找方式：`GameObject.Find(path)` →
`GetComponent("UnityEngine.UI.Graphic")`，命中后直接写 `Graphic.m_Color.a`。它对
组件类型免疫 —— 水印无论用 `UI.Text`、TMP 还是 `Image` / `Sprite`，只要挂在节点上
就能抓到，是文本识别不生效时唯一可靠的兜底。

两条特征码都是 IL2CPP 的 icall 转发桩（`jmp qword [rip+...]` / `mov rax,[rip]; jmp rax`），
模块里天然多命中：Find 旧版 9 / 新版 8 处，GetComponent 两版各 8 处。多出来的命中
大多是无关函数，**逐个候选硬试会误触发副作用**（4.6 实测：开启遮挡 UID 后打开任意
页面出问题、转视角卡顿）。

两版实测 `GameObject.Find(string)` 与 `GameObject.GetComponent(string)` 的相对距离
都是 `0x4D0`，但两者在各自候选集合里的序号会变（Find 两版都是第 4 个；GetComponent
旧版第 1 个、新版第 3 个）。运行时因此按「地址差 `0x4D0`」配对：

- 候选集合里恰好只有一对满足该距离时才采用；
- 配对失败 / 出现多对时保持禁用，不调用任何候选；
- 配对成功后只调用这一对真身，返回对象再用对象头 + `m_Color` 四分量做一次校验。

路径查找隐藏和关闭还原后，都会主动调用 MinHook 保存的原版
`Graphic.SetVerticesDirty` 跳板标脏。主界面 UID 不会随开关自动重建文本，只写
`m_Color.a` 不会刷新已生成的网格；标脏后才会立即隐藏 / 恢复。

全部游戏调用都套 SEH，路径当前不存在时返回空、下次继续重试。

## 三、新增 / 更新特征码的落地步骤

1. 采集新版 `GameAssembly.dll`（仓库自带 `tools/hsr-capture.ps1`，产物落在 WSL 内）。
2. 跑 `check_signatures.py --binary <新版> --baseline <旧版>`，看哪些目标不是唯一命中。
3. 对失效目标做相似性比对，反汇编复核候选，按本文档的方法重推特征码。
4. 同步更新 `Il2CppBridge.cpp` 与 `check_signatures.py`（两侧必须一致），
   先跑 `--verify-source src/StubStarRail/Il2CppBridge.cpp` 确认两侧逐字节一致，
   再对新旧两版各跑一遍，全部唯一命中才算通过。
5. 字段偏移（`Graphic.m_Color` `+0x20`、`Text.m_Text` `+0xF8`）在 `dump.cs` 里复核；
   这两项是 Unity 引擎侧字段，跨版本变化概率低，但每次大版本仍应确认。

> 第 4 步里的「全部唯一命中」对**预期多命中**目标（2.7 / 2.8）放宽为「命中数落在
> 预期区间」。新增或调整这类目标时，记得在 `check_signatures.py` 的 `Signature`
> 上标 `expect_multiple=True` 并给出 `min_hits` / `max_hits`，否则会被误判为失败。
> 第 5 步的文本字段偏移现在有两个：`UI.Text.m_Text` `+0xF8` 与
> `TMP_Text.m_text` `+0xF0`，两者在 `HideUid.cpp` 里都试。
