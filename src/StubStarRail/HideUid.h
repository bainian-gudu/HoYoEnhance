#pragma once

// =============================================================================
// 星穹铁道「隐藏 UID 水印」注入功能。
//
// 旧实现按写死的 UI 层级路径 GameObject.Find + GetComponent 取 Graphic，路径一改
// 就整体失效（4.5.0 起 `MobilePhoneMainPage/.../UID/NumText` 已经不存在）。新实现
// 不再依赖任何 UI 路径：
//
//   1) Hook `UnityEngine.UI.Graphic.SetVerticesDirty`（动态特征码唯一命中 +
//      相邻孪生函数校验），它是所有 UI 颜色 / 文本变化的必经点；
//   2) 在 Hook 内读对象类名（Il2CppClass::name），只处理 `Text`；
//   3) 读 `Text.m_Text`（+0xF8）判定是否 UID 文本，命中就把
//      `Graphic.m_Color.a`（+0x20+0x0C）写 0；
//   4) Hook `RPGApplication.OnUpdate` 只作为主线程 tick：处理关闭时的还原、
//      上报状态位，不再用它去找对象。
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
    bool Initialize(IpcData* ipc, void* rpgApplicationOnUpdate, void* graphicSetVerticesDirty);

    /// <summary>清空状态掩码；已创建的 Hook 由 dllmain 统一 MH_DisableHook。</summary>
    void Shutdown(IpcData* ipc);

    /// <summary>主线程 tick 是否踩到结构化异常（宿主据此报 0xE109）。</summary>
    bool HasFaulted();
}
