#pragma once

// =============================================================================
// StarRailStub 专用的 il2cpp 定位与内存访问封装。
//
// 星铁的 GameAssembly.dll 只导出 il2cpp_get_api_table 一个符号，没有原神那套
// il2cpp_* 导出，因此**不能**按名字解析函数。定位策略改为「动态特征匹配」：
//
//   1) 每个目标一条长结构特征码（函数头 + 结构常量 + 通配的 rip/rel32 位移）；
//   2) 只有全模块唯一命中才接受，多命中一律降级（可选目标）或报错（必需目标）；
//   3) 少数特征码天然多命中的目标（如 Graphic.SetVerticesDirty），再用一条
//      「相邻结构」特征做二次校验，而不是按写死的 RVA 挑第几个命中。
//
// 所有 RVA 只作为注释与回归基线保留，不参与运行时决策 —— 版本一更新就失效的
// 正是 RVA。详见同目录 SIGNATURES.md。
//
// 该文件只服务星穹铁道，与原神 Stub 相互独立：
//   - 不引用 src/Stub 下的任何业务代码；
//   - 只复用 src/Common 的 Scanner / IpcData 这类与游戏无关的基础设施。
// =============================================================================

#include <Windows.h>

#include <cstddef>
#include <cstdint>

namespace Il2CppBridge
{
    /// <summary>
    /// 路径查找候选上限。GameObject.Find / GameObject.GetComponent(string) 的
    /// 特征码是 IL2CPP 的 icall 转发桩，天然多命中（4.5.0 与 2026-09-28 两版都
    /// 各命中 8 处），因此不能用「唯一命中」判定，改由主线程运行时探测挑出真身。
    /// </summary>
    constexpr size_t kMaxPathCandidates = 24;

    /// <summary>已定位的星铁 il2cpp 目标地址。</summary>
    struct Functions
    {
        void* rpgApplicationOnUpdate = nullptr;   // RPG.Client.RPGApplication.OnUpdate
        void* ditherSetAlphaValue = nullptr;      // BaseShaderPropertyTransition 私有相机 Dither 汇合入口
        void* ditherSetDistanceAlpha = nullptr;   // BaseShaderPropertyTransition.SetDistanceDitherAlphaValue
        void* ditherSetElevationAlpha = nullptr;  // BaseShaderPropertyTransition.SetElevationDitherAlphaValue
        void* dofIsActiveImpl = nullptr;          // RPG.CustomRP.RPGDepthOfField.IsActiveImpl
        void* graphicSetVerticesDirty = nullptr;  // Graphic.SetVerticesDirty（UID 隐藏主路径）
        void* tmpTextSetVerticesDirty = nullptr;  // TMPro.TMP_Text.SetVerticesDirty（TMP 文本路径）

        // TMPro.TextMeshProUGUI 覆写了 SetVerticesDirty；它与同构的 SetMaterialDirty
        // 字节几乎完全一致（同一版内两条候选的固定字节相同），静态无法区分，
        // 因此两个候选都交给 HideUid 挂上：命中的那个是真正的文本重建通知点，
        // 另一个只是多一次无害的判定。
        void* tmpUguiDirty[2] = {};
        size_t tmpUguiDirtyCount = 0;

        // 路径查找兜底（4.5.0 旧方式）。候选多命中，运行时探测后缓存真身。
        void* findCandidates[kMaxPathCandidates] = {};
        size_t findCandidateCount = 0;
        void* getComponentCandidates[kMaxPathCandidates] = {};
        size_t getComponentCandidateCount = 0;
    };

    /// <summary>定位结果，用于宿主错误码分级。</summary>
    enum class ResolveStatus
    {
        Ok = 0,
        GameAssemblyMissing,
        MainThreadEntryMissing,
        DitherEntryMissing,
        DofEntryMissing,
        GraphicEntryMissing,
    };

    /// <summary>
    /// 按动态特征码定位全部目标。只在工作线程做只读扫描，不调用任何 il2cpp 接口。
    /// 必需目标（OnUpdate / Dither / DOF / SetVerticesDirty）任一失败即返回对应
    /// 状态，全部唯一命中才返回 ResolveStatus::Ok。
    /// </summary>
    ResolveStatus Resolve(HMODULE gameAssembly, Functions& out);

    /// <summary>
    /// 读取任意地址的字节（先做模块内可读校验，再套 SEH）。
    /// 供 UID 隐藏模块读取 il2cpp 对象头 / 字符串用。
    /// </summary>
    bool ReadBytes(const void* address, void* out, size_t size);

    /// <summary>
    /// 热路径用的裸读：跳过 VirtualQuery，只靠 SEH 兜底。
    /// 只在「调用方保证对象有效」时使用（例如刚被游戏调用的 this 指针）。
    /// </summary>
    bool ReadBytesRaw(const void* address, void* out, size_t size);

    /// <summary>读取 float（含可读校验 + SEH）。</summary>
    bool ReadFloat(const void* address, float& value);

    /// <summary>写入 float（含可写校验 + SEH）。</summary>
    bool WriteFloat(void* address, float value);

    /// <summary>
    /// IL2CPP 对象的类名：对象头 klass 在 +0x0，Il2CppClass::name 在 +0x10。
    /// 返回的指针指向模块内常量区，调用方只做只读比较，不要长期保存。
    /// 任意一步读不到就返回 nullptr。
    /// </summary>
    const char* ObjectClassName(const void* object);

    /// <summary>
    /// 读取 IL2CPP System.String：length 在 +0x10，UTF-16 字符数组在 +0x14。
    /// 超出 capacity 时截断并把 length 写成实际拷贝数。空串 / 非法对象返回 false。
    /// </summary>
    bool ReadString(const void* stringObject, wchar_t* out, size_t capacity, int32_t& length);

    /// <summary>最近一次 Resolve 的地址表（供主线程 tick 读取）。</summary>
    const Functions& Resolved();

    /// <summary>
    /// 主线程：按 UI 层级路径取 UnityEngine.UI.Graphic。
    ///
    /// 这是 4.5.0 旧实现的方式：GameObject.Find(path) → GetComponent("UnityEngine.UI.Graphic")，
    /// 命中后直接写 Graphic.m_Color.a，对组件类型免疫 —— 无论水印是 Text、TMP 还是
    /// Image / Sprite，只要挂在节点上就能抓到，是文本识别失效时的兜底路径。
    ///
    /// Find / GetComponent 的候选天然多命中，但**只调用文档记录的那个 Find 下标**
    /// （见 kPreferredFindIndex）：其余命中是无关的 il2cpp icall 桩，逐个硬试会误调
    /// 有副作用的函数（4.6 实测：开启遮挡 UID 后打开背包出问题）。GetComponent 候选
    /// 在有效 GameObject 上试，用类名或对象头 + m_Color 校验挑真身，成功后缓存。
    /// 路径当前不存在或探测失败返回 nullptr，下次仍会重试。
    /// 所有游戏调用都用 SEH 包住，绝不把异常抛回 Hook。
    /// </summary>
    void* FindGraphicByPath(const char* path);

    /// <summary>路径查找是否已探测到有效的 Find / GetComponent（供状态上报）。</summary>
    bool IsPathLookupReady();

    /// <summary>
    /// 诊断日志：把一行文本追加到 WSL 工作区里的 starrail-stub-diag.log
    /// （优先 \\wsl.localhost\Ubuntu\...，失败退到 %TEMP%）。只在排查 UID 路径
    /// 时使用，正常运行时也只在探测阶段写少量行，不会持续刷盘。
    /// </summary>
    void DiagLog(const char* line);
}
