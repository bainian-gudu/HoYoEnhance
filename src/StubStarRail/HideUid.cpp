// =============================================================================
// 星穹铁道隐藏 UID 水印实现。
//
// 两条路一起上，互为兜底：
//
//   A) 路径查找（4.5.0 旧方式，主路径）
//      在主线程 tick 里按 UI 层级路径 GameObject.Find → GetComponent(
//      "UnityEngine.UI.Graphic") 直接取到 Graphic 并写 m_Color.a = 0。
//      这条路对组件类型免疫 —— 水印无论用 Text、TMP 还是 Image / Sprite，
//      只要挂在节点上就能抓到，也是「文本识别不生效」时唯一可靠的兜底。
//      Find / GetComponent 的地址由 Il2CppBridge 用运行时探测确定（特征码
//      天然多命中，不能按序号挑）。
//
//   B) 文本识别（辅助路径）
//      1) Hook UnityEngine.UI.Graphic.SetVerticesDirty（唯一命中 + 相邻孪生校验），
//         它是 UI.Text 系文本变化的必经点；
//      2) 额外 Hook TMPro.TMP_Text.SetVerticesDirty（唯一命中）与
//         TMPro.TextMeshProUGUI 的两个同构 Dirty 入口 —— TMP_Text 覆写了
//         Graphic.SetVerticesDirty，只挂基类会漏掉全部 TMP 文本；
//      3) 类名含 "Text" 的组件才处理（Text / LocalizedText / TMP_Text /
//         TextMeshProUGUI ...）；
//      4) 同时尝试 UI.Text.m_Text（+0xF8）与 TMP_Text.m_text（+0xF0）两个偏移，
//         取看起来真的是 il2cpp string 的那个；
//      5) Hook RPG.Client.RPGApplication.OnUpdate 作为主线程 tick：跑路径查找、
//         处理关闭时的还原、上报状态位。
//
// 文本判定规则（不依赖任何 UI 节点名）：
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
#include <cstdarg>
#include <cstdio>
#include <cstdint>
#include <cstring>

#include "Il2CppBridge.h"
#include "MinHook.h"

namespace
{
    // UnityEngine.UI.Graphic.m_Color // Offset: 0x20（dump.cs 实测）
    constexpr size_t kGraphicColorOffset = 0x20;
    constexpr size_t kGraphicColorAlphaOffset = kGraphicColorOffset + 3 * sizeof(float);

    // 文本字段偏移（dump.cs 实测）：
    //   UnityEngine.UI.Text.m_Text  +0xF8
    //   TMPro.TMP_Text.m_text       +0xF0
    // 两个偏移互相落在对方的其它字段上，所以不能按类名硬选，改为两个都读、
    // 用「length 合法 + 内容是 UID」筛出真正有效的那个。
    constexpr size_t kUiTextTextOffset = 0xF8;
    constexpr size_t kTmpTextTextOffset = 0xF0;
    constexpr size_t kTextTextOffsets[] = {kUiTextTextOffset, kTmpTextTextOffset};

    // System.String::m_Length // Offset: 0x10
    constexpr size_t kStringLengthOffset = 0x10;

    // UID 文本很短；超过这个长度一定不是水印，直接跳过，避免读到长文本。
    constexpr int32_t kMaxUidTextLength = 63;
    constexpr int32_t kUidDigitsMin = 6;
    constexpr int32_t kUidDigitsMax = 12;

    constexpr size_t kMaxHidden = 32;
    constexpr DWORD kRestoreWaitMs = 250;

    // 路径查找的候选节点。前两条来自 4.5.0 参考实现；路径随版本会变，任何一条
    // 失效都只是少一个兜底点，不影响文本识别。
    constexpr const char* kUidPaths[] = {
        "/UIRoot/AboveDialog/BetaHintDialog(Clone)/Contents/VersionText",
        "/UIRoot/Page/MobilePhoneMainPage(Clone)/Content/Content/LeftPlane/Tittle/UID/NumText",
    };

    // 路径查找不需要每帧跑：水印只在切界面时重建，0.25s 一次足够，也能把
    // GameObject.Find 的场景遍历开销摊薄。
    constexpr int32_t kPathLookupIntervalTicks = 15;

    struct HiddenEntry
    {
        void* graphic;
        float originalAlpha;
    };

    void* g_boundIpc = nullptr;
    void* g_originalSetVerticesDirty = nullptr;
    void* g_originalTmpTextSetVerticesDirty = nullptr;
    void* g_originalTmpUguiDirtyA = nullptr;
    void* g_originalTmpUguiDirtyB = nullptr;
    void* g_originalOnUpdate = nullptr;
    bool g_setVerticesDirtyReady = false;
    bool g_tmpTextSetVerticesDirtyReady = false;
    bool g_tmpUguiDirtyAReady = false;
    bool g_tmpUguiDirtyBReady = false;
    bool g_onUpdateReady = false;
    int32_t g_pathLookupCountdown = 0;

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

    // ---- 诊断（排查 UID 组件类型 / 节点路径）-----------------------------
    // 记录 hook 到过的每个 Graphic 类名（去重、限量），便于判断 UID 水印到底用
    // 的是 UI.Text、TMP 还是 Image；只在出现新类名时写一行日志。
    constexpr size_t kMaxDiagClassNames = 32;
    char g_diagClassNames[kMaxDiagClassNames][64]{};
    size_t g_diagClassNameCount = 0;

    void DiagLogLinef(const char* format, ...)
    {
        char buffer[256]{};
        va_list args;
        va_start(args, format);
        std::vsnprintf(buffer, sizeof(buffer), format, args);
        va_end(args);
        Il2CppBridge::DiagLog(buffer);
    }

    void DiagRememberClassName(const char* name)
    {
        if (!name || *name == '\0')
        {
            return;
        }
        for (size_t i = 0; i < g_diagClassNameCount; ++i)
        {
            if (std::strcmp(g_diagClassNames[i], name) == 0)
            {
                return;
            }
        }
        if (g_diagClassNameCount >= kMaxDiagClassNames)
        {
            return;
        }

        std::strncpy(g_diagClassNames[g_diagClassNameCount], name, 63);
        g_diagClassNames[g_diagClassNameCount][63] = '\0';
        ++g_diagClassNameCount;

        DiagLogLinef("[graphic] class=%s", name);
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

    /// <summary>
    /// 按给定字段偏移取出文本并判定 UID。
    /// 偏移不对时会读到别的字段，用「指针非空 + length 合法」先把绝大多数假指针
    /// 挡掉（例如 UI.Text 的 +0xF0 是 m_FontData，TMP 的 +0xF8 是 bool）。
    /// </summary>
    bool MatchUidTextAtOffset(void* self, size_t offset)
    {
        void* text = nullptr;
        if (!Il2CppBridge::ReadBytesRaw(static_cast<uint8_t*>(self) + offset, &text, sizeof(text)) ||
            !text)
        {
            return false;
        }

        // 先读长度：长文本一律不可能是 UID 水印，直接跳过。
        int32_t rawLength = 0;
        if (!Il2CppBridge::ReadBytesRaw(
                static_cast<uint8_t*>(text) + kStringLengthOffset, &rawLength, sizeof(rawLength)) ||
            rawLength <= 0 || rawLength > kMaxUidTextLength)
        {
            return false;
        }

        wchar_t buffer[64]{};
        int32_t length = 0;
        if (!Il2CppBridge::ReadStringRaw(text, buffer, 64, length))
        {
            return false;
        }
        return MatchUidText(buffer, length);
    }

    /// <summary>把一个已确认的 Graphic 置为全透明，并记录原 alpha 以便还原。</summary>
    void HideGraphic(void* graphic)
    {
        if (!graphic)
        {
            return;
        }

        float alpha = 1.0f;
        if (!Il2CppBridge::ReadFloat(
                static_cast<uint8_t*>(graphic) + kGraphicColorAlphaOffset, alpha) ||
            alpha <= 0.0f)
        {
            return; // 已经是透明的，不重复记录
        }

        RememberHidden(graphic, alpha);
        Il2CppBridge::WriteFloat(static_cast<uint8_t*>(graphic) + kGraphicColorAlphaOffset, 0.0f);
    }

    /// <summary>SetVerticesDirty 内的文本判定与隐藏路径。</summary>
    void HideIfUidText(void* self)
    {
        if (!self)
        {
            return;
        }

        // 不再用类名当门槛：4.6 起 ObjectClassName 对部分对象返回 null，
        // 「拿不到类名就整个跳过」会让文本识别彻底失效。也不在热路径读类名 ——
        // ObjectClassName 每次要做几次 VirtualQuery，SetVerticesDirty 每帧被调很多
        // 次，放在这里会拖慢 UI。直接按文本字段判定，命中后才读类名写诊断。
        // UI.Text 与 TMP_Text 的文本字段偏移不同，两个都试一遍。
        for (size_t offset : kTextTextOffsets)
        {
            if (MatchUidTextAtOffset(self, offset))
            {
                // 只在「真的新隐藏了一个对象」时记日志：UID 文本可能每帧重建，
                // 每次匹配都写日志 + FlushFileBuffers 会把 UI 拖卡。
                const size_t before = g_hiddenCount;
                HideGraphic(self);
                if (g_hiddenCount != before)
                {
                    const char* className = Il2CppBridge::ObjectClassName(self);
                    DiagRememberClassName(className);
                    DiagLogLinef("[uid] matched text: class=%s offset=0x%zX",
                                 className ? className : "(null)", offset);
                }
                return;
            }
        }
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
            // 路径查找可能隐藏的是 Image / Sprite 等非文本 Graphic，因此这里只校验
            // 对象仍然存活（类名可读），不要求类名含 "Text"。
            if (!Il2CppBridge::ObjectClassName(graphic))
            {
                continue;
            }
            Il2CppBridge::WriteFloat(static_cast<uint8_t*>(graphic) + kGraphicColorAlphaOffset,
                                     g_hidden[i].originalAlpha);
        }
        g_hiddenCount = 0;
    }

    /// <summary>
    /// 主线程 tick 里的路径查找兜底（4.5.0 旧方式）。
    /// 只在开关打开且到达间隔帧时执行，避免每帧遍历整个场景。
    /// </summary>
    void PathLookupTick()
    {
        if (--g_pathLookupCountdown > 0)
        {
            return;
        }
        g_pathLookupCountdown = kPathLookupIntervalTicks;

        for (const char* path : kUidPaths)
        {
            void* graphic = Il2CppBridge::FindGraphicByPath(path);
            if (graphic)
            {
                DiagRememberClassName(Il2CppBridge::ObjectClassName(graphic));
                HideGraphic(graphic);
            }
        }
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

        if (ipc->HideUid != 0)
        {
            PathLookupTick();
        }

        const bool hidden = g_hiddenCount > 0;
        g_hidAnyObject.store(hidden, std::memory_order_relaxed);
        ipc->HideUidState =
            static_cast<int32_t>(IpcHideUidState::Ready) |
            (hidden ? static_cast<int32_t>(IpcHideUidState::Active) : 0) |
            (Il2CppBridge::IsPathLookupReady()
                 ? static_cast<int32_t>(IpcHideUidState::PathReady)
                 : 0);
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

    /// <summary>
    /// 所有文本重建入口共用的处理：先判定 / 隐藏，再转调原函数。
    /// UI.Text 与 TMP 的 SetVerticesDirty 签名都是 void(void* this)。
    /// </summary>
    void HandleTextDirty(void* self, void* original)
    {
        if (IsHideEnabled())
        {
            HideIfUidTextSafe(self);
        }
        if (original)
        {
            reinterpret_cast<SetVerticesDirtyFn>(original)(self);
        }
    }

    void HookSetVerticesDirty(void* self)
    {
        HandleTextDirty(self, g_originalSetVerticesDirty);
    }

    void HookTmpTextSetVerticesDirty(void* self)
    {
        HandleTextDirty(self, g_originalTmpTextSetVerticesDirty);
    }

    void HookTmpUguiDirtyA(void* self)
    {
        HandleTextDirty(self, g_originalTmpUguiDirtyA);
    }

    void HookTmpUguiDirtyB(void* self)
    {
        HandleTextDirty(self, g_originalTmpUguiDirtyB);
    }
}

namespace HideUid
{
    bool Initialize(IpcData* ipc, void* rpgApplicationOnUpdate, void* graphicSetVerticesDirty,
                    void* tmpTextSetVerticesDirty, void* tmpUguiDirtyA, void* tmpUguiDirtyB)
    {
        if (!ipc || !rpgApplicationOnUpdate || !graphicSetVerticesDirty)
        {
            return false;
        }

        g_boundIpc = ipc;
        // 新一轮会话开始时清除上一轮的 tick 故障标志；否则 Host 重试后
        // 会立刻被旧的 faulted 状态再次判为 Error。
        g_faulted.store(false, std::memory_order_relaxed);
        g_pathLookupCountdown = 0;

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

        // TMP / TextMeshProUGUI 的 Dirty 入口是可选增强：TMP_Text 覆写了
        // Graphic.SetVerticesDirty，挂上它们才能覆盖 TMP 系文本；定位失败只降级为
        // 「文本识别只覆盖 UI.Text 系」，路径查找兜底仍然有效，不阻塞整个模块。
        if (!g_tmpTextSetVerticesDirtyReady && tmpTextSetVerticesDirty)
        {
            g_tmpTextSetVerticesDirtyReady =
                MH_CreateHook(tmpTextSetVerticesDirty,
                              reinterpret_cast<void*>(&HookTmpTextSetVerticesDirty),
                              reinterpret_cast<LPVOID*>(&g_originalTmpTextSetVerticesDirty)) == MH_OK;
        }

        if (!g_tmpUguiDirtyAReady && tmpUguiDirtyA)
        {
            g_tmpUguiDirtyAReady =
                MH_CreateHook(tmpUguiDirtyA, reinterpret_cast<void*>(&HookTmpUguiDirtyA),
                              reinterpret_cast<LPVOID*>(&g_originalTmpUguiDirtyA)) == MH_OK;
        }

        if (!g_tmpUguiDirtyBReady && tmpUguiDirtyB && tmpUguiDirtyB != tmpUguiDirtyA)
        {
            g_tmpUguiDirtyBReady =
                MH_CreateHook(tmpUguiDirtyB, reinterpret_cast<void*>(&HookTmpUguiDirtyB),
                              reinterpret_cast<LPVOID*>(&g_originalTmpUguiDirtyB)) == MH_OK;
        }

        const bool ready = g_onUpdateReady && g_setVerticesDirtyReady;
        ipc->HideUidState =
            ready ? static_cast<int32_t>(IpcHideUidState::Ready) : 0;
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
