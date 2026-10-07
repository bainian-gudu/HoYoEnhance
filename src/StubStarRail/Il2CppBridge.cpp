// =============================================================================
// StarRailStub 专用的 il2cpp 定位与内存访问实现。
//
// 定位策略：**只认特征码，不认 RVA**。每个目标一条长结构特征码，通配符只打在
// 版本间必然变化的字节上（rip 相对位移、rel32 调用目标、立即数），其余字节都是
// 结构常量（函数头、静态字段判空、字段偏移访问、跳转条件）。这样版本更新后特征
// 仍然命中，而写死的 RVA 一定失效。
//
// 安全边界：
//   - 必需目标要求「全模块唯一命中」，多命中一律判失败 —— 宁可让宿主提示未适配，
//     也不把无关函数当成目标（参考实现的多候选硬试会调用到 AssetBundle.LoadFromFile
//     / ImageConversion.LoadImage 这类函数，有崩游戏的风险）；
//   - 少数特征码天然多命中的目标（Graphic.SetVerticesDirty）用「相邻结构」二次校验，
//     不按「第几个命中」挑选；
//   - 所有定位只做只读扫描，不调用任何 il2cpp / Unity 接口，可在工作线程执行。
//
// 各特征码的字节来源、命中数与候选身份见同目录 SIGNATURES.md。
// =============================================================================

#include "Il2CppBridge.h"

#include <Psapi.h>

#include <cstring>
#include <new>

#include "Scanner.h"

#pragma comment(lib, "Psapi.lib")

namespace
{
    // ---- 特征码 -----------------------------------------------------------
    // 通配符只覆盖版本间会变的位移；其余字节是跨版本稳定的结构常量。

    // RPG.Client.RPGApplication.OnUpdate
    //   4.5.0  RVA 0x1802FBF0
    //   2026-09-28 RVA 0x0DE0AD30
    // 两个版本都唯一命中。类内字段偏移（0x18 / 0xC7 之类）会随版本变，所以
    // `cmp byte [rsi+off], 0` / `cmp byte [rcx+off], 0` 的 off 字节留通配。
    constexpr const char* kRpgApplicationOnUpdatePattern =
        "56 57 48 83 EC 48 0F 29 7C 24 30 0F 29 74 24 20 48 89 CE 80 3D ?? ?? ?? ?? 00 "
        "0F 85 ?? ?? ?? ?? 80 7E ?? 00 0F 84 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? "
        "80 B9 ?? 00 00 00 00 0F 84 ?? ?? ?? ??";

    // BaseShaderPropertyTransition 私有相机 Dither 汇合入口（4.5.0 RVA 0x19F1BE00，唯一命中）
    constexpr const char* kDitherMergePattern =
        "41 56 56 57 55 53 48 83 EC 50 0F 29 7C 24 40 0F 29 74 24 30 44 89 CD 44 89 C7 "
        "0F 28 F9 48 89 CE 80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ?? "
        "80 3D ?? ?? ?? ?? 00 0F 85 ?? ?? ?? ??";

    // BaseShaderPropertyTransition.SetDistanceDitherAlphaValue（4.5.0 RVA 0x19F1C0E0，唯一命中）
    // 结构锚点：先写 +0x30（DistanceDitherAlpha）再读 +0x2C（ElevationDitherAlpha）。
    constexpr const char* kDitherSetDistancePattern =
        "56 53 48 83 EC 38 0F 29 74 24 20 44 89 C3 0F 28 F1 48 89 CE 80 3D ?? ?? ?? ?? 00 "
        "75 ?? 80 7E 36 00 74 ?? 0F 57 C0 F3 0F 5F C6 F3 0F 10 0D ?? ?? ?? ?? "
        "F3 0F 5D C8 F3 0F 11 4E 30 F3 0F 59 4E 2C";

    // BaseShaderPropertyTransition.SetElevationDitherAlphaValue（4.5.0 RVA 0x19F1BD70，唯一命中）
    // 结构锚点：先写 +0x2C（ElevationDitherAlpha）再读 +0x30（DistanceDitherAlpha）。
    constexpr const char* kDitherSetElevationPattern =
        "56 48 83 EC 30 0F 29 74 24 20 0F 28 F1 48 89 CE 80 3D ?? ?? ?? ?? 00 "
        "75 ?? 80 7E 36 00 74 ?? 0F 57 C0 F3 0F 5F C6 F3 0F 10 0D ?? ?? ?? ?? "
        "F3 0F 5D C8 F3 0F 11 4E 2C F3 0F 59 4E 30";

    // RPG.CustomRP.RPGDepthOfField.IsActiveImpl
    //   4.5.0  RVA 0x1858CCF0
    //   2026-09-28 RVA 0x1CC15630
    // 两个版本都唯一命中。字段偏移（0xB7 / 0x2A70 / 0x10）随版本变，留通配；
    // 尾部的 `... C4 28 C3 31 C0 ... C4 28 C3` 是返回分支结构，用来拉开区分度。
    constexpr const char* kDofIsActivePattern =
        "48 83 EC 28 80 79 18 00 74 ?? 48 8B 0D ?? ?? ?? ?? 80 B9 ?? 00 00 00 00 74 ?? "
        "48 8B 05 ?? ?? ?? ?? 48 8B 80 ?? ?? 00 00 48 85 C0 74 ?? 80 78 ?? 00 0F 95 C0 "
        "48 83 C4 28 C3 31 C0 48 83 C4 28 C3";

    // UnityEngine.UI.Graphic.SetVerticesDirty（4.5.0 RVA 0x1B78C0C0，命中 2 处）
    // 另一处命中是 SRDebugger 的 ConsoleLogControl.Update，两者字节几乎一致，
    // 因此不能只看这一条 —— 必须配合下面的「相邻结构」二次校验。
    constexpr const char* kGraphicSetVerticesDirtyPattern =
        "56 48 83 EC 20 48 89 CE FF 15 ?? ?? ?? ?? 84 C0 74 ?? "
        "C6 86 99 00 00 00 01 EB ?? 48 89 F1 FF 15 ?? ?? ?? ?? 84 C0 74 ?? "
        "C6 46 58 01 48 89 F1 E8 ?? ?? ?? ?? 48 8B 46 68 48 85 C0 74 ?? "
        "4C 8B 40 18 48 8B 50 28 48 8B 48 40 48 83 C4 20 5E 49 FF E0 90";

    // 紧跟在 Graphic.SetVerticesDirty 之后的 Graphic.SetLayoutDirty：结构完全相同，
    // 只是两个字段偏移各 +1（0x99→0x9A、0x58→0x59）。这一对「孪生函数」是
    // Graphic.SetVerticesDirty 独有的相邻结构，ConsoleLogControl.Update 后面没有。
    constexpr const char* kGraphicSetLayoutDirtyTwinPattern =
        "56 48 83 EC 20 48 89 CE FF 15 ?? ?? ?? ?? 84 C0 74 09 "
        "C6 86 9A 00 00 00 01";

    /// 孪生函数的搜索窗口：4.5.0 实测距离 0x60，留一倍余量。
    constexpr uintptr_t kGraphicTwinWindow = 0x80;

    // TMPro.TMP_Text.SetVerticesDirty
    //   4.5.0  RVA 0x134E9970
    //   2026-09-28 RVA 0x1F2F77A0
    // 两个版本都唯一命中。TMP_Text 覆写了 Graphic.SetVerticesDirty，只挂基类
    // 会漏掉全部 TMP 文本；这一条覆盖 TMP_Text 及其未再覆写的子类。
    constexpr const char* kTmpTextSetVerticesDirtyPattern =
        "56 57 53 48 83 EC 20 48 89 CF FF 15 ?? ?? ?? ?? 84 C0 0F 84 ?? ?? ?? ?? "
        "48 8B B7 ?? ?? ?? ?? 48 85 F6 0F 84 ?? ?? ?? ?? 48 83 7E 10 00";

    // TMPro.TextMeshProUGUI 的 SetVerticesDirty / SetMaterialDirty。
    //   4.5.0  RVA 0x134EACD0 / 0x1352A360
    //   2026-09-28 RVA 0x1F2F8A80 / 0x1F337E50
    // 两个版本都恰好命中 2 处，且两条候选的固定字节完全相同（IL2CPP 把这两个
    // 同构方法编译成了同样的指令序列），静态无法区分。TextMeshProUGUI 又覆写了
    // TMP_Text 的实现，所以两个候选都交给 HideUid 挂上：真身是文本重建通知点，
    // 另一个只是多一次判定，没有副作用。
    constexpr const char* kTmpUguiDirtyPattern =
        "56 57 48 83 EC 28 48 85 C9 74 ?? 48 89 CE 48 83 79 10 00 74 ?? "
        "48 89 F1 FF 15 ?? ?? ?? ?? 84 C0 74 ?? 48 8B 05 ?? ?? ?? ?? "
        "48 8B B8 ?? ?? ?? ??";

    // GameObject.Find(string)（路径查找兜底，4.5.0 旧方式）
    //   4.5.0  第 4 个命中 = 0x1DEDE300
    //   2026-09-28 第 4 个命中 = 0x1F3C1130
    // 两版都命中 8~9 处，且「第几个」不稳定（GetComponent 两版序号就不同），
    // 因此不按序号挑，交给主线程运行时探测。
    constexpr const char* kGameObjectFindPattern =
        "48 FF ?? ?? ?? ?? ?? 66 0F 1F 84 00 00 00 00 00 48 83 EC 28 C7 44 24 20";

    // GameObject.GetComponent(string)（路径查找兜底，4.5.0 旧方式）
    //   4.5.0  第 1 个命中 = 0x1DEDDE30
    //   2026-09-28 第 3 个命中 = 0x1F3C0C60
    // 两版都命中 8 处。同样交给运行时探测。
    constexpr const char* kGetComponentStringPattern =
        "48 8B 05 ?? ?? ?? ?? 48 FF E0 66 0F 1F 44 00 00 "
        "48 8B 05 ?? ?? ?? ?? 45 31 C0 48 FF E0 0F 1F 00";

    // ---- IL2CPP 对象布局（跨版本长期稳定）--------------------------------
    constexpr size_t kIl2CppClassOffset = 0x00;       // Il2CppObject::klass
    constexpr size_t kIl2CppClassNameOffset = 0x10;   // Il2CppClass::name
    constexpr size_t kStringLengthOffset = 0x10;      // System.String::m_Length
    constexpr size_t kStringCharsOffset = 0x14;       // System.String::m_Chars

    Il2CppBridge::Functions g_functions{};

    // ---- 路径查找运行时状态（只在游戏主线程访问）--------------------------
    void* g_findResolved = nullptr;          // 探测确定的 GameObject.Find
    void* g_getComponentResolved = nullptr;  // 探测确定的 GameObject.GetComponent(string)
    bool g_pathLookupProbed = false;         // 是否已成功探测（成功后才置位）

    // 自建 IL2CPP 字符串：星铁没有可用的 il2cpp_string_new 导出，参考实现同样
    // 手工拼 length + chars。只在 Find / GetComponent 调用期间有效，调用后立即
    // 释放；不交给会长期持有引用的游戏代码。
    struct Il2CppString
    {
        void* klass;
        void* monitor;
        int32_t length;
        wchar_t chars[1];
    };

    class ScopedIl2CppString
    {
    public:
        explicit ScopedIl2CppString(const char* utf8)
        {
            if (!utf8 || *utf8 == '\0')
            {
                return;
            }

            const int wideLength = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, nullptr, 0);
            if (wideLength <= 1)
            {
                return;
            }

            // 多留一个 wchar_t 给结尾 NUL：IL2CPP 的 length 不含 NUL，
            // 但底层字符串缓冲区按惯例以 NUL 结尾，避免转换时越界写入。
            const size_t bytes = offsetof(Il2CppString, chars) +
                                 static_cast<size_t>(wideLength) * sizeof(wchar_t);
            auto* value = static_cast<Il2CppString*>(::operator new[](bytes, std::nothrow));
            if (!value)
            {
                return;
            }

            std::memset(value, 0, bytes);
            value->klass = nullptr;
            value->monitor = nullptr;
            value->length = wideLength - 1;
            MultiByteToWideChar(CP_UTF8, 0, utf8, -1, value->chars, wideLength);
            _value = value;
        }

        ~ScopedIl2CppString()
        {
            ::operator delete[](_value);
        }

        ScopedIl2CppString(const ScopedIl2CppString&) = delete;
        ScopedIl2CppString& operator=(const ScopedIl2CppString&) = delete;

        bool Valid() const { return _value != nullptr; }
        void* Get() const { return _value; }

    private:
        Il2CppString* _value = nullptr;
    };

    using FindFn = void* (*)(void*);
    using GetComponentFn = void* (*)(void*, void*);

    /// <summary>
    /// 带 SEH 的 GameObject.Find 调用。函数内不能出现需要析构的 C++ 对象
    /// （MSVC C2712），字符串构造放在调用方。
    /// </summary>
    void* CallFindRaw(void* find, void* str)
    {
        if (!find || !str)
        {
            return nullptr;
        }
#if defined(_MSC_VER)
        __try
        {
            return reinterpret_cast<FindFn>(find)(str);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
#else
        return reinterpret_cast<FindFn>(find)(str);
#endif
    }

    /// <summary>带 SEH 的 GameObject.GetComponent(string) 调用。</summary>
    void* CallGetComponentRaw(void* getComponent, void* gameObject, void* typeName)
    {
        if (!getComponent || !gameObject || !typeName)
        {
            return nullptr;
        }
#if defined(_MSC_VER)
        __try
        {
            return reinterpret_cast<GetComponentFn>(getComponent)(gameObject, typeName);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
#else
        return reinterpret_cast<GetComponentFn>(getComponent)(gameObject, typeName);
#endif
    }

    /// <summary>地址是否落在模块映像内。</summary>
    bool IsInModule(HMODULE module, const void* address, size_t size)
    {
        if (!module || !address)
        {
            return false;
        }

        MODULEINFO mi{};
        if (!GetModuleInformation(GetCurrentProcess(), module, &mi, sizeof(mi)))
        {
            return false;
        }

        const uintptr_t base = reinterpret_cast<uintptr_t>(mi.lpBaseOfDll);
        const uintptr_t end = base + mi.SizeOfImage;
        const uintptr_t addr = reinterpret_cast<uintptr_t>(address);
        return addr >= base && size <= end - addr;
    }

    /// <summary>地址是否可读（已提交、非 GUARD、含读权限）。</summary>
    bool IsReadable(const void* address, size_t size)
    {
        if (!address || size == 0)
        {
            return false;
        }

        MEMORY_BASIC_INFORMATION mbi{};
        if (!VirtualQuery(address, &mbi, sizeof(mbi)))
        {
            return false;
        }
        if (mbi.State != MEM_COMMIT || (mbi.Protect & PAGE_GUARD) != 0)
        {
            return false;
        }
        const DWORD readable = PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY |
                               PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY;
        if ((mbi.Protect & readable) == 0)
        {
            return false;
        }
        const uintptr_t start = reinterpret_cast<uintptr_t>(address);
        const uintptr_t regionEnd = reinterpret_cast<uintptr_t>(mbi.BaseAddress) + mbi.RegionSize;
        return size <= regionEnd - start;
    }

    /// <summary>地址是否可写。</summary>
    bool IsWritable(const void* address, size_t size)
    {
        if (!IsReadable(address, size))
        {
            return false;
        }

        MEMORY_BASIC_INFORMATION mbi{};
        if (!VirtualQuery(address, &mbi, sizeof(mbi)))
        {
            return false;
        }
        return (mbi.Protect & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE |
                               PAGE_EXECUTE_WRITECOPY)) != 0;
    }

    /// <summary>地址是否可执行（用于确认定位结果真的是函数入口）。</summary>
    bool IsExecutable(const void* address, size_t size)
    {
        if (!IsReadable(address, size))
        {
            return false;
        }

        MEMORY_BASIC_INFORMATION mbi{};
        if (!VirtualQuery(address, &mbi, sizeof(mbi)))
        {
            return false;
        }
        return (mbi.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE |
                               PAGE_EXECUTE_WRITECOPY)) != 0;
    }

    /// <summary>
    /// 唯一的必需目标：特征码必须全模块唯一命中，且结果落在模块内且可执行。
    /// </summary>
    void* ResolveUnique(HMODULE module, const char* pattern)
    {
        const auto hits = Scanner::ScanModuleAll(module, pattern);
        if (hits.size() != 1)
        {
            return nullptr;
        }
        return IsInModule(module, hits.front(), 1) && IsExecutable(hits.front(), 1)
                   ? hits.front()
                   : nullptr;
    }

    /// <summary>
    /// 多命中目标：候选后面 kGraphicTwinWindow 字节内必须出现「孪生函数」特征。
    /// 仍然要求唯一 —— 只有恰好一个候选满足时才接受。
    /// </summary>
    void* ResolveByAdjacentTwin(HMODULE module, const char* pattern, const char* twinPattern)
    {
        const auto candidates = Scanner::ScanModuleAll(module, pattern);
        if (candidates.empty())
        {
            return nullptr;
        }
        const auto twins = Scanner::ScanModuleAll(module, twinPattern);
        if (twins.empty())
        {
            return nullptr;
        }

        void* match = nullptr;
        for (void* candidate : candidates)
        {
            if (!IsInModule(module, candidate, 1) || !IsExecutable(candidate, 1))
            {
                continue;
            }

            const uintptr_t start = reinterpret_cast<uintptr_t>(candidate);
            for (void* twin : twins)
            {
                const uintptr_t addr = reinterpret_cast<uintptr_t>(twin);
                if (addr <= start || addr - start > kGraphicTwinWindow)
                {
                    continue;
                }
                if (match)
                {
                    return nullptr; // 多于一个候选满足 → 宁可失败
                }
                match = candidate;
                break;
            }
        }
        return match;
    }

    /// <summary>
    /// 收集多命中目标的全部候选，过滤掉模块外 / 不可执行的项。
    /// 候选的真身由主线程运行时探测决定（见 FindGraphicByPath）。
    /// </summary>
    void CollectCandidates(HMODULE module, const char* pattern, void** out, size_t& count,
                           size_t capacity)
    {
        count = 0;
        if (!module || !pattern || !out || capacity == 0)
        {
            return;
        }

        const auto hits = Scanner::ScanModuleAll(module, pattern);
        for (void* hit : hits)
        {
            if (count >= capacity)
            {
                break;
            }
            if (IsInModule(module, hit, 1) && IsExecutable(hit, 1))
            {
                out[count++] = hit;
            }
        }
    }
}

namespace Il2CppBridge
{
    ResolveStatus Resolve(HMODULE gameAssembly, Functions& out)
    {
        out = Functions{};
        if (!gameAssembly)
        {
            return ResolveStatus::GameAssemblyMissing;
        }

        // RPGApplication.OnUpdate：UID 隐藏唯一安全的主线程入口。
        out.rpgApplicationOnUpdate = ResolveUnique(gameAssembly, kRpgApplicationOnUpdatePattern);
        if (!out.rpgApplicationOnUpdate)
        {
            return ResolveStatus::MainThreadEntryMissing;
        }

        // 角色相机 Dither：优先私有汇合入口，公开的距离 / 高度入口作为兜底。
        out.ditherSetAlphaValue = ResolveUnique(gameAssembly, kDitherMergePattern);
        if (!out.ditherSetAlphaValue)
        {
            out.ditherSetDistanceAlpha = ResolveUnique(gameAssembly, kDitherSetDistancePattern);
            out.ditherSetElevationAlpha = ResolveUnique(gameAssembly, kDitherSetElevationPattern);
        }
        if (!out.ditherSetAlphaValue && !out.ditherSetDistanceAlpha && !out.ditherSetElevationAlpha)
        {
            return ResolveStatus::DitherEntryMissing;
        }

        // 场景景深（第二项反虚化）：IsActiveImpl 是 DOF 总开关，唯一命中。
        out.dofIsActiveImpl = ResolveUnique(gameAssembly, kDofIsActivePattern);
        if (!out.dofIsActiveImpl)
        {
            return ResolveStatus::DofEntryMissing;
        }

        // UI 重建通知：UID 隐藏的主路径。多命中，用相邻孪生函数二次校验。
        out.graphicSetVerticesDirty =
            ResolveByAdjacentTwin(gameAssembly, kGraphicSetVerticesDirtyPattern,
                                  kGraphicSetLayoutDirtyTwinPattern);
        if (!out.graphicSetVerticesDirty)
        {
            return ResolveStatus::GraphicEntryMissing;
        }

        // TMP 文本重建通知：可选增强。TMP_Text 覆写了 Graphic.SetVerticesDirty，
        // 只挂基类会漏掉 TMP 文本；定位失败不阻塞（UID 仍可走路径查找兜底）。
        out.tmpTextSetVerticesDirty = ResolveUnique(gameAssembly, kTmpTextSetVerticesDirtyPattern);

        // TextMeshProUGUI 的 SetVerticesDirty / SetMaterialDirty 两个同构候选，
        // 静态无法区分，两个都留给 HideUid 挂上。
        {
            const auto candidates = Scanner::ScanModuleAll(gameAssembly, kTmpUguiDirtyPattern);
            for (void* candidate : candidates)
            {
                if (out.tmpUguiDirtyCount >= 2)
                {
                    break;
                }
                if (IsInModule(gameAssembly, candidate, 1) && IsExecutable(candidate, 1))
                {
                    out.tmpUguiDirty[out.tmpUguiDirtyCount++] = candidate;
                }
            }
        }

        // 路径查找兜底（4.5.0 旧方式）：候选多命中，真身由主线程探测。
        CollectCandidates(gameAssembly, kGameObjectFindPattern, out.findCandidates,
                          out.findCandidateCount, kMaxPathCandidates);
        CollectCandidates(gameAssembly, kGetComponentStringPattern, out.getComponentCandidates,
                          out.getComponentCandidateCount, kMaxPathCandidates);

        g_functions = out;
        return ResolveStatus::Ok;
    }

    bool ReadBytes(const void* address, void* out, size_t size)
    {
        if (!out || size == 0 || !IsReadable(address, size))
        {
            return false;
        }
#if defined(_MSC_VER)
        __try
        {
            std::memcpy(out, address, size);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
#else
        std::memcpy(out, address, size);
        return true;
#endif
    }

    bool ReadFloat(const void* address, float& value)
    {
        return ReadBytes(address, &value, sizeof(value));
    }

    bool ReadBytesRaw(const void* address, void* out, size_t size)
    {
        if (!address || !out || size == 0)
        {
            return false;
        }
#if defined(_MSC_VER)
        __try
        {
            std::memcpy(out, address, size);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
#else
        std::memcpy(out, address, size);
        return true;
#endif
    }

    bool WriteFloat(void* address, float value)
    {
        if (!IsWritable(address, sizeof(value)))
        {
            return false;
        }
#if defined(_MSC_VER)
        __try
        {
            *static_cast<float*>(address) = value;
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return false;
        }
#else
        *static_cast<float*>(address) = value;
        return true;
#endif
    }

    const char* ObjectClassName(const void* object)
    {
        if (!object)
        {
            return nullptr;
        }

        void* klass = nullptr;
        if (!ReadBytes(static_cast<const uint8_t*>(object) + kIl2CppClassOffset, &klass,
                       sizeof(klass)) ||
            !klass)
        {
            return nullptr;
        }

        const char* name = nullptr;
        if (!ReadBytes(static_cast<const uint8_t*>(klass) + kIl2CppClassNameOffset, &name,
                       sizeof(name)) ||
            !name || !IsReadable(name, 1))
        {
            return nullptr;
        }
        return name;
    }

    bool ReadString(const void* stringObject, wchar_t* out, size_t capacity, int32_t& length)
    {
        length = 0;
        if (!stringObject || !out || capacity == 0)
        {
            return false;
        }

        int32_t rawLength = 0;
        if (!ReadBytes(static_cast<const uint8_t*>(stringObject) + kStringLengthOffset, &rawLength,
                       sizeof(rawLength)) ||
            rawLength < 0)
        {
            return false;
        }

        // 长度字段是攻击面（对象可能已被 GC 回收）：上限先按容量夹一次。
        const int32_t copyLength = rawLength < static_cast<int32_t>(capacity)
                                       ? rawLength
                                       : static_cast<int32_t>(capacity) - 1;
        if (copyLength > 0)
        {
            if (!ReadBytes(static_cast<const uint8_t*>(stringObject) + kStringCharsOffset, out,
                           static_cast<size_t>(copyLength) * sizeof(wchar_t)))
            {
                return false;
            }
        }
        out[copyLength] = L'\0';
        length = copyLength;
        return true;
    }

    bool ReadStringRaw(const void* stringObject, wchar_t* out, size_t capacity, int32_t& length)
    {
        length = 0;
        if (!stringObject || !out || capacity == 0)
        {
            return false;
        }

        int32_t rawLength = 0;
        if (!ReadBytesRaw(static_cast<const uint8_t*>(stringObject) + kStringLengthOffset,
                          &rawLength, sizeof(rawLength)) ||
            rawLength < 0)
        {
            return false;
        }

        const int32_t copyLength = rawLength < static_cast<int32_t>(capacity)
                                       ? rawLength
                                       : static_cast<int32_t>(capacity) - 1;
        if (copyLength > 0)
        {
            if (!ReadBytesRaw(static_cast<const uint8_t*>(stringObject) + kStringCharsOffset, out,
                              static_cast<size_t>(copyLength) * sizeof(wchar_t)))
            {
                return false;
            }
        }
        out[copyLength] = L'\0';
        length = copyLength;
        return true;
    }

    // UnityEngine.UI.Graphic.m_Color：RGBA 四个 float（分量在 [0,1]）。
    // 类名读不到时用它做兜底校验，见 IsPlausibleGraphic。
    constexpr size_t kGraphicColorOffset = 0x20;

    // GameObject.Find 在 kGameObjectFindPattern 命中集合里的下标。
    //   4.5.0        第 4 个命中 = 0x1DEDE300
    //   2026-09-28   第 4 个命中 = 0x1F3C1130
    // 4.6 实测同样是第 4 个命中。
    // 只信这一个下标：其余命中是无关的 il2cpp icall 桩，逐个硬试会调用到有副作用的
    // 函数（4.6 实测：开启遮挡 UID 后打开背包出问题、转视角卡顿）。下标失效时最多是
    // 「这次不隐藏」，绝不去调其它函数。
    constexpr size_t kPreferredFindIndex = 3;

    /// <summary>
    /// 一个指针是否像 UnityEngine.UI.Graphic。
    /// 首选类名含 "Graphic"；类名读得到但不是 Graphic 时明确排除；类名读不到
    /// （4.6 实测 ObjectClassName 对部分候选返回 null）时退回校验对象头 + m_Color：
    /// 先确认是 IL2CPP 对象（klass 可读），再要求四个颜色分量都能读成 [0,1] 的 float。
    /// </summary>
    bool IsPlausibleGraphic(const void* graphic)
    {
        const char* name = ObjectClassName(graphic);
        if (name)
        {
            return std::strstr(name, "Graphic") != nullptr;
        }

        void* klass = nullptr;
        if (!ReadBytes(static_cast<const uint8_t*>(graphic) + kIl2CppClassOffset, &klass,
                       sizeof(klass)) ||
            !klass)
        {
            return false;
        }

        float color[4]{};
        if (!ReadBytes(static_cast<const uint8_t*>(graphic) + kGraphicColorOffset, color,
                       sizeof(color)))
        {
            return false;
        }
        for (float component : color)
        {
            if (!(component >= 0.0f && component <= 1.0f))
            {
                return false;
            }
        }
        return true;
    }

    void* FindGraphicByPath(const char* path)
    {
        const Functions& functions = g_functions;
        if (!path || functions.findCandidateCount == 0 ||
            functions.getComponentCandidateCount == 0)
        {
            return nullptr;
        }

        ScopedIl2CppString pathString(path);
        if (!pathString.Valid())
        {
            return nullptr;
        }

        // 探测成功后只用缓存的真身，避免每次把全部候选都调一遍。
        if (g_pathLookupProbed)
        {
            void* gameObject = CallFindRaw(g_findResolved, pathString.Get());
            if (!gameObject)
            {
                return nullptr;
            }
            ScopedIl2CppString graphicName("UnityEngine.UI.Graphic");
            if (!graphicName.Valid())
            {
                return nullptr;
            }
            return CallGetComponentRaw(g_getComponentResolved, gameObject, graphicName.Get());
        }

        // 未探测：只调用文档记录的 Find 候选（kPreferredFindIndex），拿到 GameObject
        // 后再在有效对象上试 GetComponent 候选（用类名或 m_Color 校验挑真身）。
        // 不再遍历 Find 候选：那些 icall 桩里多数不是 GameObject.Find，硬试会误调
        // 有副作用的函数。
        if (functions.findCandidateCount <= kPreferredFindIndex)
        {
            return nullptr;
        }

        ScopedIl2CppString graphicName("UnityEngine.UI.Graphic");
        if (!graphicName.Valid())
        {
            return nullptr;
        }

        void* findCandidate = functions.findCandidates[kPreferredFindIndex];
        void* gameObject = CallFindRaw(findCandidate, pathString.Get());
        if (!gameObject)
        {
            // 路径当前不存在（UI 尚未加载 / 该界面未打开），下次再试。
            return nullptr;
        }

        for (size_t g = 0; g < functions.getComponentCandidateCount; ++g)
        {
            void* getComponentCandidate = functions.getComponentCandidates[g];
            void* graphic = CallGetComponentRaw(getComponentCandidate, gameObject, graphicName.Get());
            if (!graphic || !IsPlausibleGraphic(graphic))
            {
                continue;
            }

            g_findResolved = findCandidate;
            g_getComponentResolved = getComponentCandidate;
            g_pathLookupProbed = true;
            return graphic;
        }
        return nullptr;
    }

    bool IsPathLookupReady()
    {
        return g_pathLookupProbed;
    }

    const Functions& Resolved()
    {
        return g_functions;
    }
}
