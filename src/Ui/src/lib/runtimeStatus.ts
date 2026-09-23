interface RuntimeFpsTextInput {
  running: boolean;
  attached: boolean;
  registryBased: boolean;
  registryFps: number | null | undefined;
  currentFps: number;
  targetFps: number;
}

/** 星铁展示注册表配置的帧率上限，不把它冒充为游戏实时 FPS。 */
export function runtimeFpsText({
  running,
  attached,
  registryBased,
  registryFps,
  currentFps,
  targetFps,
}: RuntimeFpsTextInput): string {
  if (registryBased) return typeof registryFps !== 'number' || !Number.isFinite(registryFps)
    ? '注册表帧率尚未确认'
    : `注册表当前设置 ${registryFps} FPS`;
  if (!running) return '等待游戏启动';
  if (!attached) return '未注入';
  return currentFps > 0 ? `${currentFps} → ${targetFps} FPS` : `目标 ${targetFps} FPS`;
}
