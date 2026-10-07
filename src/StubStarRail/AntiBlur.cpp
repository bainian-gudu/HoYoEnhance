// =============================================================================
// 星穹铁道两项反虚化实现。
//
// ① 反角色虚化（相机 Dither）
//    4.5.0 dump.cs：
//      BaseShaderPropertyTransition.HBPKIAAKMPE                 RVA 0x19F1BE00
//      BaseShaderPropertyTransition.SetDistanceDitherAlphaValue  RVA 0x19F1C0E0
//      BaseShaderPropertyTransition.SetElevationDitherAlphaValue RVA 0x19F1BD70
//    反汇编确认：距离与高度入口最终都会调用 HBPKIAAKMPE(value, priority, force)。
//    相机碰撞 / 靠近角色的虚化使用 DitherSourcePriority.Camera(1)，剧情与逻辑
//    淡入淡出使用其它优先级。因此只改写 Camera 来源，不会吞掉剧情显隐。
//    优先挂 HBPKIAAKMPE，覆盖所有相机 Dither 路径；私有入口定位失败时退回
//    距离 / 高度两个公开入口。
//
// ② 反场景景深虚化（DOF）
//    4.5.0 dump.cs：RPG.CustomRP.RPGDepthOfField.IsActiveImpl  RVA 0x1858CCF0
//    CustomVolumeComponent 的激活判定；返回 false 时该后处理不参与渲染。
//    与 ① 完全独立：① 管角色靠近镜头的半透明，② 管背景景深模糊。
// =============================================================================

#include "AntiBlur.h"

#include <cstddef>
#include <cstdint>

#include "MinHook.h"

namespace
{
    // RPG.Client.DitherSourcePriority.Camera
    constexpr int32_t kDitherSourceCamera = 1;
    constexpr float kVisibleDitherAlpha = 1.0f;

    using DitherSetAlphaFn = bool (*)(void* self, float alpha, int32_t priority, bool force);
    using DitherSetDistanceFn = void (*)(void* self, float alpha, bool force);
    using DitherSetElevationFn = void (*)(void* self, float alpha);
    using DofIsActiveFn = bool (*)(void* self);

    void* g_boundIpc = nullptr;
    void* g_originalSetAlpha = nullptr;
    void* g_originalSetDistanceAlpha = nullptr;
    void* g_originalSetElevationAlpha = nullptr;
    void* g_originalDofIsActive = nullptr;
    bool g_setAlphaReady = false;
    bool g_setDistanceReady = false;
    bool g_setElevationReady = false;
    bool g_dofReady = false;

    bool IsOverrideEnabled()
    {
        IpcData* ipc = static_cast<IpcData*>(g_boundIpc);
        return ipc && ipc->AntiBlurPerspective != 0;
    }

    bool HookSetDitherAlpha(void* self, float alpha, int32_t priority, bool force)
    {
        if (!g_originalSetAlpha)
        {
            return false;
        }

        if (IsOverrideEnabled() && priority == kDitherSourceCamera)
        {
            alpha = kVisibleDitherAlpha;
        }
        return reinterpret_cast<DitherSetAlphaFn>(g_originalSetAlpha)(self, alpha, priority, force);
    }

    void HookSetDistanceDitherAlpha(void* self, float alpha, bool force)
    {
        if (!g_originalSetDistanceAlpha)
        {
            return;
        }

        if (IsOverrideEnabled())
        {
            alpha = kVisibleDitherAlpha;
        }
        reinterpret_cast<DitherSetDistanceFn>(g_originalSetDistanceAlpha)(self, alpha, force);
    }

    void HookSetElevationDitherAlpha(void* self, float alpha)
    {
        if (!g_originalSetElevationAlpha)
        {
            return;
        }

        if (IsOverrideEnabled())
        {
            alpha = kVisibleDitherAlpha;
        }
        reinterpret_cast<DitherSetElevationFn>(g_originalSetElevationAlpha)(self, alpha);
    }

    bool IsDofOverrideEnabled()
    {
        IpcData* ipc = static_cast<IpcData*>(g_boundIpc);
        return ipc && ipc->AntiBlurDof != 0;
    }

    /// <summary>
    /// 景深总开关：开启「反场景景深虚化」时直接返回 false，让后处理跳过；
    /// 关闭时转调原函数，画面与未注入完全一致。
    /// </summary>
    bool HookDofIsActive(void* self)
    {
        if (IsDofOverrideEnabled())
        {
            return false;
        }
        return g_originalDofIsActive
                   ? reinterpret_cast<DofIsActiveFn>(g_originalDofIsActive)(self)
                   : false;
    }
}

namespace AntiBlur
{
    bool Initialize(IpcData* ipc, void* ditherSetAlphaValue, void* ditherSetDistanceAlpha,
                    void* ditherSetElevationAlpha, void* dofIsActiveImpl)
    {
        if (!ipc)
        {
            return false;
        }

        g_boundIpc = ipc;

        if (!g_setAlphaReady && ditherSetAlphaValue)
        {
            g_setAlphaReady =
                MH_CreateHook(ditherSetAlphaValue, reinterpret_cast<void*>(&HookSetDitherAlpha),
                              &g_originalSetAlpha) == MH_OK;
        }

        // 私有汇合入口不可用时才挂公开入口，避免同一路径被重复 Hook。
        if (!g_setAlphaReady)
        {
            if (!g_setDistanceReady && ditherSetDistanceAlpha)
            {
                g_setDistanceReady =
                    MH_CreateHook(ditherSetDistanceAlpha,
                                  reinterpret_cast<void*>(&HookSetDistanceDitherAlpha),
                                  &g_originalSetDistanceAlpha) == MH_OK;
            }
            if (!g_setElevationReady && ditherSetElevationAlpha)
            {
                g_setElevationReady =
                    MH_CreateHook(ditherSetElevationAlpha,
                                  reinterpret_cast<void*>(&HookSetElevationDitherAlpha),
                                  &g_originalSetElevationAlpha) == MH_OK;
            }
        }

        if (!g_dofReady && dofIsActiveImpl)
        {
            g_dofReady = MH_CreateHook(dofIsActiveImpl, reinterpret_cast<void*>(&HookDofIsActive),
                                       &g_originalDofIsActive) == MH_OK;
        }

        const bool perspectiveReady =
            g_setAlphaReady || g_setDistanceReady || g_setElevationReady;
        ipc->AntiBlurState =
            (perspectiveReady ? static_cast<int32_t>(IpcAntiBlurState::PerspectiveReady) : 0) |
            (g_dofReady ? static_cast<int32_t>(IpcAntiBlurState::DofReady) : 0);
        return perspectiveReady && g_dofReady;
    }

    void Shutdown(IpcData* ipc)
    {
        // Hook 由 dllmain 统一 MH_DisableHook(MH_ALL_HOOKS)；这里保留 g_original*
        // 与 ready 标志，使同一进程内的重试保持幂等。
        if (ipc)
        {
            ipc->AntiBlurState = static_cast<int32_t>(IpcAntiBlurState::None);
        }
    }
}
