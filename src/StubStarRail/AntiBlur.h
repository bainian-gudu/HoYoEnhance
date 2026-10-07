#pragma once

// =============================================================================
// 星穹铁道两项反虚化注入功能。
//
// ① 反角色虚化：RPG.Client.BaseShaderPropertyTransition 的相机 Dither 链路。
//    开启时只把 DitherSourcePriority.Camera 的透明值改成 1.0；剧情 / 逻辑来源
//    的淡入淡出保持原样。三个入口都按动态特征码定位（唯一命中），私有汇合入口
//    优先，距离 / 高度公开入口兜底。
//    4.5.0 基线：汇合入口 0x19F1BE00 / SetDistance 0x19F1C0E0 / SetElevation 0x19F1BD70。
//
// ② 反场景景深虚化：RPG.CustomRP.RPGDepthOfField.IsActiveImpl 是景深后处理的
//    总开关，开启时直接返回 false（不执行景深），关闭时转调原函数。
//    4.5.0 基线：0x1858CCF0。
//    注意这一项与「角色靠近镜头半透明」不是同一链路 —— 后者走 ①，两者互不影响。
//
// 与原神 Stub 的 AntiBlur 实现无耦合。
// =============================================================================

#include <Windows.h>

#include "../Common/IpcData.h"

namespace AntiBlur
{
    /// <summary>
    /// 创建两项反虚化 Hook。
    /// 反角色虚化：优先挂私有相机 Dither 汇合入口；不可用时退回距离 / 高度两个
    /// 公开入口，三个地址至少一个有效。
    /// 反场景景深：挂 RPGDepthOfField.IsActiveImpl，地址必须有效。
    /// 两类 Hook 都创建成功才返回 true。幂等：同一进程内重复调用不会重复创建。
    /// </summary>
    bool Initialize(IpcData* ipc, void* ditherSetAlphaValue, void* ditherSetDistanceAlpha,
                    void* ditherSetElevationAlpha, void* dofIsActiveImpl);

    /// <summary>清空状态掩码；已创建的 Hook 由 dllmain 统一 MH_DisableHook。</summary>
    void Shutdown(IpcData* ipc);
}
