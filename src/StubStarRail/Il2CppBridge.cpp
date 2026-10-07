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

    // ---- IL2CPP 对象布局（跨版本长期稳定）--------------------------------
    constexpr size_t kIl2CppClassOffset = 0x00;       // Il2CppObject::klass
    constexpr size_t kIl2CppClassNameOffset = 0x10;   // Il2CppClass::name
    constexpr size_t kStringLengthOffset = 0x10;      // System.String::m_Length
    constexpr size_t kStringCharsOffset = 0x14;       // System.String::m_Chars

    Il2CppBridge::Functions g_functions{};

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

    const Functions& Resolved()
    {
        return g_functions;
    }
}
