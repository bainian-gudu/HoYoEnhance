// =============================================================================
// 星穹铁道隐藏 UID 水印实现。
//
// 主线程入口：Hook RPG.Client.RPGApplication.OnUpdate（动态特征码唯一命中），
// 只用来做关闭时的还原与状态上报。
//
// 实际隐藏点：Hook UnityEngine.UI.Graphic.SetVerticesDirty（动态特征码 +
// 相邻孪生函数校验）。它是 UI 颜色 / 文本变化的必经点，在这里能直接拿到
// Graphic 实例，不必再用 GameObject.Find 走层级路径 —— 路径随版本改动是旧实现
// 失效的直接原因。
//
// 组件判定：类名含 "Text" 的 Graphic 才处理。星铁 UI 的文本组件有多个类
// （Text / LocalizedText / SRText / HoYoText ...），水印不一定是基类 "Text"；
// 只比较 "Text" 会把用子类的对象整个跳过，这是「反虚化生效、UID 不生效」
// 最可能的原因。
//
// 判定规则（不依赖任何 UI 节点名）：
//   a) 文本含 "UID"（忽略大小写）且带 6~12 位连续数字 → 判定为 UID 水印，
//      同时把这串数字记为「已知 UID」；
//   b) 文本本身就是 6~12 位纯数字，且与「已知 UID」完全一致 → 判定为 UID
//      （覆盖只显示数字的资料页）。
//
// 命中后把 Graphic.m_Color.a（+0x20+0x0C）写 0，并记录原值以便关闭开关时还原。
// 所有读写都先做可读 / 可写校验再套 SEH；还原前重新校验类名，避免 GC 回收后
// 误写无关对象。
// =============================================================================

#include "HideUid.h"

#include <atomic>
#include <cstdint>
#include <cstring>

#include "Il2CppBridge.h"
#include "MinHook.h"

namespace
{
    // UnityEngine.UI.Graphic.m_Color // Offset: 0x20（dump.cs 实测）
    constexpr size_t kGraphicColorOffset = 0x20;
    constexpr size_t kGraphicColorAlphaOffset = kGraphicColorOffset + 3 * sizeof(float);

    // UnityEngine.UI.Text.m_Text // Offset: 0xF8（dump.cs 实测）
    constexpr size_t kTextTextOffset = 0xF8;

    // System.String::m_Length // Offset: 0x10
    constexpr size_t kStringLengthOffset = 0x10;

    // UID 文本很短；超过这个长度一定不是水印，直接跳过，避免读到长文本。
    constexpr int32_t kMaxUidTextLength = 63;
    constexpr int32_t kUidDigitsMin = 6;
    constexpr int32_t kUidDigitsMax = 12;

    constexpr size_t kMaxHidden = 32;
    constexpr DWORD kRestoreWaitMs = 250;

    struct HiddenEntry
    {
        void* graphic;
        float originalAlpha;
    };

    void* g_boundIpc = nullptr;
    void* g_originalSetVerticesDirty = nullptr;
    void* g_originalOnUpdate = nullptr;
    bool g_setVerticesDirtyReady = false;
    bool g_onUpdateReady = false;

    // 以下状态只在游戏主线程访问（SetVerticesDirty / OnUpdate 都在主线程）。
    HiddenEntry g_hidden[kMaxHidden]{};
    size_t g_hiddenCount = 0;
    wchar_t g_knownUid[16]{};

    std::atomic_bool g_hidAnyObject{false};
    std::atomic_bool g_restoreRequested{false};
    std::atomic_bool g_faulted{false};

    using SetVerticesDirtyFn = void (*)(void*);
    using OnUpdateFn = void (*)(void*);

    bool IsHideEnabled()
    {
        IpcData* ipc = static_cast<IpcData*>(g_boundIpc);
        return ipc && ipc->HideUid != 0;
    }

    bool IsDigit(wchar_t c)
    {
        return c >= L'0' && c <= L'9';
    }

    /// <summary>
    /// 对象是否是 UI 文本组件：类名里含 "Text"。
    /// 星铁 UI 的文本组件有 Text / LocalizedText / SRText / HoYoText /
    /// HyperTextLink / DialogueText / DevUIText 等多个类，都继承
    /// UnityEngine.UI.Text；只比较基类名 "Text" 会把用子类的对象整个跳过。
    /// 用子串判断能覆盖这些子类，同时排除 Image / RawImage 等非文本 Graphic。
    /// </summary>
    bool IsTextComponentName(const char* name)
    {
        return name && std::strstr(name, "Text") != nullptr;
    }

    /// <summary>文本里是否出现 "UID"（忽略大小写）。</summary>
    bool ContainsUidToken(const wchar_t* text, int32_t length)
    {
        for (int32_t i = 0; i + 3 <= length; ++i)
        {
            if ((text[i] == L'U' || text[i] == L'u') &&
                (text[i + 1] == L'I' || text[i + 1] == L'i') &&
                (text[i + 2] == L'D' || text[i + 2] == L'd'))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>是否与已记录的 UID 完全一致。</summary>
    bool EqualsKnownUid(const wchar_t* text, int32_t length)
    {
        if (g_knownUid[0] == L'\0' || length <= 0)
        {
            return false;
        }

        int32_t known = 0;
        while (known < 15 && g_knownUid[known] != L'\0')
        {
            ++known;
        }
        if (known != length)
        {
            return false;
        }
        for (int32_t i = 0; i < known; ++i)
        {
            if (g_knownUid[i] != text[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 判定这段文本是否 UID 水印；命中时把数字串记进 g_knownUid。
    /// 只在主线程调用。
    /// </summary>
    bool MatchUidText(const wchar_t* text, int32_t length)
    {
        if (!text || length <= 0 || length > kMaxUidTextLength)
        {
            return false;
        }

        // 找最长的连续数字串。
        int32_t bestStart = -1;
        int32_t bestLength = 0;
        for (int32_t i = 0; i < length;)
        {
            if (!IsDigit(text[i]))
            {
                ++i;
                continue;
            }
            int32_t j = i;
            while (j < length && IsDigit(text[j]))
            {
                ++j;
            }
            if (j - i > bestLength)
            {
                bestLength = j - i;
                bestStart = i;
            }
            i = j;
        }

        if (bestLength < kUidDigitsMin || bestLength > kUidDigitsMax)
        {
            return false;
        }

        if (ContainsUidToken(text, length))
        {
            const int32_t copy = bestLength < 15 ? bestLength : 15;
            for (int32_t k = 0; k < copy; ++k)
            {
                g_knownUid[k] = text[bestStart + k];
            }
            g_knownUid[copy] = L'\0';
            return true;
        }

        // 整串就是纯数字，且与已知 UID 一致。
        return bestStart == 0 && bestLength == length && EqualsKnownUid(text, length);
    }

    void RememberHidden(void* graphic, float originalAlpha)
    {
        for (size_t i = 0; i < g_hiddenCount; ++i)
        {
            if (g_hidden[i].graphic == graphic)
            {
                return;
            }
        }

        if (g_hiddenCount < kMaxHidden)
        {
            g_hidden[g_hiddenCount].graphic = graphic;
            g_hidden[g_hiddenCount].originalAlpha = originalAlpha;
            ++g_hiddenCount;
            return;
        }

        // 满了：丢掉最早的一条。UI 重建后旧对象本就可能失效，可接受。
        for (size_t i = 1; i < kMaxHidden; ++i)
        {
            g_hidden[i - 1] = g_hidden[i];
        }
        g_hidden[kMaxHidden - 1].graphic = graphic;
        g_hidden[kMaxHidden - 1].originalAlpha = originalAlpha;
    }

    /// <summary>SetVerticesDirty 内的文本判定与隐藏路径。</summary>
    void HideIfUidText(void* self)
    {
        if (!self)
        {
            return;
        }

        // 每次都按类名判断：星铁的文本对象有 Text / LocalizedText 等多个类，
        // 缓存单一 klass 会把后续出现的其它文本类全部挡掉。
        if (!IsTextComponentName(Il2CppBridge::ObjectClassName(self)))
        {
            return;
        }

        void* text = nullptr;
        if (!Il2CppBridge::ReadBytesRaw(
                static_cast<uint8_t*>(self) + kTextTextOffset, &text, sizeof(text)) ||
            !text)
        {
            return;
        }

        // 先读长度：长文本一律不可能是 UID 水印，直接跳过。
        int32_t rawLength = 0;
        if (!Il2CppBridge::ReadBytesRaw(
                static_cast<uint8_t*>(text) + kStringLengthOffset, &rawLength, sizeof(rawLength)) ||
            rawLength <= 0 || rawLength > kMaxUidTextLength)
        {
            return;
        }

        wchar_t buffer[64]{};
        int32_t length = 0;
        if (!Il2CppBridge::ReadString(text, buffer, 64, length))
        {
            return;
        }
        if (!MatchUidText(buffer, length))
        {
            return;
        }

        float alpha = 1.0f;
        if (!Il2CppBridge::ReadFloat(
                static_cast<uint8_t*>(self) + kGraphicColorAlphaOffset, alpha) ||
            alpha <= 0.0f)
        {
            return; // 已经是透明的，不重复记录
        }

        RememberHidden(self, alpha);
        Il2CppBridge::WriteFloat(static_cast<uint8_t*>(self) + kGraphicColorAlphaOffset, 0.0f);
    }

    void HideIfUidTextSafe(void* self)
    {
#if defined(_MSC_VER)
        __try
        {
            HideIfUidText(self);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            // 单次读失败不终止模块：SetVerticesDirty 仍然照常转调原函数。
        }
#else
        HideIfUidText(self);
#endif
    }

    /// <summary>还原所有由本方改过的 alpha；对象失效就跳过。</summary>
    void RestoreAll()
    {
        for (size_t i = 0; i < g_hiddenCount; ++i)
        {
            void* graphic = g_hidden[i].graphic;
            if (!IsTextComponentName(Il2CppBridge::ObjectClassName(graphic)))
            {
                continue;
            }
            Il2CppBridge::WriteFloat(static_cast<uint8_t*>(graphic) + kGraphicColorAlphaOffset,
                                     g_hidden[i].originalAlpha);
        }
        g_hiddenCount = 0;
    }

    void MainThreadTick()
    {
        IpcData* ipc = static_cast<IpcData*>(g_boundIpc);
        if (!ipc)
        {
            return;
        }

        const bool restore = g_restoreRequested.exchange(false, std::memory_order_relaxed);
        if ((restore || ipc->HideUid == 0) && g_hiddenCount > 0)
        {
            RestoreAll();
        }

        const bool hidden = g_hiddenCount > 0;
        g_hidAnyObject.store(hidden, std::memory_order_relaxed);
        ipc->HideUidState = static_cast<int32_t>(IpcHideUidState::Ready) |
                            (hidden ? static_cast<int32_t>(IpcHideUidState::Active) : 0);
    }

    /// <summary>
    /// SEH 版本：tick 内一旦出现访问违例，标记 faulted 并停止后续执行，
    /// 由 dllmain 上报 Error 后统一卸钩。
    /// </summary>
    void MainThreadTickSafe()
    {
        if (g_faulted.load(std::memory_order_relaxed))
        {
            return;
        }
#if defined(_MSC_VER)
        __try
        {
            MainThreadTick();
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            g_faulted.store(true, std::memory_order_relaxed);
        }
#else
        MainThreadTick();
#endif
    }

    void HookOnUpdate(void* self)
    {
        if (g_originalOnUpdate)
        {
            reinterpret_cast<OnUpdateFn>(g_originalOnUpdate)(self);
        }
        MainThreadTickSafe();
    }

    void HookSetVerticesDirty(void* self)
    {
        if (IsHideEnabled())
        {
            HideIfUidTextSafe(self);
        }
        if (g_originalSetVerticesDirty)
        {
            reinterpret_cast<SetVerticesDirtyFn>(g_originalSetVerticesDirty)(self);
        }
    }
}

namespace HideUid
{
    bool Initialize(IpcData* ipc, void* rpgApplicationOnUpdate, void* graphicSetVerticesDirty)
    {
        if (!ipc || !rpgApplicationOnUpdate || !graphicSetVerticesDirty)
        {
            return false;
        }

        g_boundIpc = ipc;
        // 新一轮会话开始时清除上一轮的 tick 故障标志；否则 Host 重试后
        // 会立刻被旧的 faulted 状态再次判为 Error。
        g_faulted.store(false, std::memory_order_relaxed);

        if (!g_onUpdateReady)
        {
            g_onUpdateReady =
                MH_CreateHook(rpgApplicationOnUpdate, reinterpret_cast<void*>(&HookOnUpdate),
                              reinterpret_cast<LPVOID*>(&g_originalOnUpdate)) == MH_OK;
        }

        if (!g_setVerticesDirtyReady)
        {
            g_setVerticesDirtyReady =
                MH_CreateHook(graphicSetVerticesDirty,
                              reinterpret_cast<void*>(&HookSetVerticesDirty),
                              reinterpret_cast<LPVOID*>(&g_originalSetVerticesDirty)) == MH_OK;
        }

        const bool ready = g_onUpdateReady && g_setVerticesDirtyReady;
        ipc->HideUidState = ready ? static_cast<int32_t>(IpcHideUidState::Ready) : 0;
        return ready;
    }

    void Shutdown(IpcData* ipc)
    {
        // 退出注入时把恢复请求交给仍启用的 OnUpdate Hook；等不到主线程出帧
        // 就放弃，绝不在 worker 线程调用 Unity 接口。
        if (g_hidAnyObject.load(std::memory_order_relaxed) && g_onUpdateReady)
        {
            g_restoreRequested.store(true, std::memory_order_relaxed);
            const DWORD deadline = GetTickCount() + kRestoreWaitMs;
            while (g_restoreRequested.load(std::memory_order_relaxed) && GetTickCount() < deadline)
            {
                Sleep(10);
            }
            g_restoreRequested.store(false, std::memory_order_relaxed);
        }

        g_hidAnyObject.store(false, std::memory_order_relaxed);
        if (ipc)
        {
            ipc->HideUidState = static_cast<int32_t>(IpcHideUidState::None);
        }
    }

    bool HasFaulted()
    {
        return g_faulted.load(std::memory_order_relaxed);
    }
}
