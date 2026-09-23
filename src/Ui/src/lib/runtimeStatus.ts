interface RuntimeFpsTextInput {
  running: boolean;
  attached: boolean;
  registryBased: boolean;
  registryFps: number | null | undefined;
  currentFps: number;
  targetFps: number;
}

/** 运行状态卡的帧率文案。注册表游戏只展示最近一次实际核对到的值。 */
export function runtimeFpsText({
  running,
  attached,
  registryBased,
  registryFps,
  currentFps,
  targetFps,
}: RuntimeFpsTextInput): string {
  if (!running) return '等待游戏启动';
  if (registryBased) return typeof registryFps !== 'number' || !Number.isFinite(registryFps)
    ? '注册表帧率尚未确认'
    : `注册表当前设置 ${registryFps} FPS`;
  if (!attached) return '未注入';
  return currentFps > 0 ? `${currentFps} → ${targetFps} FPS` : `目标 ${targetFps} FPS`;
}
