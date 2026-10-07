#pragma once

// =============================================================================
// 星穹铁道「隐藏 UID 水印」注入功能。
//
// 两条路一起上，互为兜底：
//
//   A) 路径查找（4.5.0 旧方式，主路径）
//      在主线程 tick 里按 UI 层级路径 GameObject.Find → GetComponent("
//      UnityEngine.UI.Graphic") 直接取到 Graphic 并写 m_Color.a = 0。
//      这条路对组件类型免疫 —— 水印无论用 Text、TMP 还是 Image / Sprite，
//      只要挂在节点上就能抓到，也是「文本识别不生效」时唯一可靠的兜底。
//      Find / GetComponent 的地址由 Il2CppBridge 用运行时探测确定。
//
//   B) 文本识别（辅助路径）
//      1) Hook `UnityEngine.UI.Graphic.SetVerticesDirty`（动态特征码唯一命中 +
//         相邻孪生函数校验），它是 UI.Text 系文本变化的必经点；
//      2) 额外 Hook `TMPro.TMP_Text.SetVerticesDirty`（唯一命中）与
//         `TMPro.TextMeshProUGUI` 的两个同构 Dirty 入口 —— TMP_Text 覆写了
//         Graphic.SetVerticesDirty，只挂基类会漏掉全部 TMP 文本；
//      3) 在 Hook 内读对象类名（Il2CppClass::name），类名含 "Text" 的组件才处理
//         （Text / LocalizedText / TMP_Text / TextMeshProUGUI ...）；
//      4) 同时尝试 UI.Text.m_Text（+0xF8）与 TMP_Text.m_text（+0xF0）两个偏移，
//         取看起来真的是 il2cpp string 的那个，判定是否 UID 文本；
//      5) Hook `RPGApplication.OnUpdate` 只作为主线程 tick：跑路径查找、处理
//         关闭时的还原、上报状态位。
//
// 关掉开关时按记录的原 alpha 还原；对象在还原前会重新校验类名，避免 GC 回收后
// 误写无关对象。全部读写都经过 Il2CppBridge 的可读 / 可写校验 + SEH。
// =============================================================================

#include <Windows.h>

#include "../Common/IpcData.h"

namespace HideUid
{
    /// <summary>
    /// 创建 UID 隐藏 Hook。
    /// <paramref name="graphicSetVerticesDirty"/> 是实际隐藏点，
    /// <paramref name="rpgApplicationOnUpdate"/> 是主线程 tick。
    /// 两个地址都必须有效，任一为空返回 false。
    /// 幂等：同一进程内重复调用不会重复创建 Hook。
    /// </summary>
    bool Initialize(IpcData* ipc, void* rpgApplicationOnUpdate, void* graphicSetVerticesDirty,
                    void* tmpTextSetVerticesDirty, void* tmpUguiDirtyA, void* tmpUguiDirtyB);

    /// <summary>清空状态掩码；已创建的 Hook 由 dllmain 统一 MH_DisableHook。</summary>
    void Shutdown(IpcData* ipc);

    /// <summary>主线程 tick 是否踩到结构化异常（宿主据此报 0xE109）。</summary>
    bool HasFaulted();
}
