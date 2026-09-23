interface RuntimeFpsTextInput {
  running: boolean;
  attached: boolean;
  registryFps?: number;
  currentFps: number;
  targetFps: number;
}

/** 运行状态卡的当前帧率文案。注册表解锁始终优先，即使画面效果让 Stub 处于附着状态。 */
export function runtimeFpsText({
  running,
  attached,
  registryFps,
  currentFps,
  targetFps,
}: RuntimeFpsTextInput): string {
  if (!running) return '等待游戏启动';
  if (registryFps) return `由注册表解锁 ${registryFps} FPS`;
  if (!attached) return '未注入';
  return currentFps > 0 ? `${currentFps} → ${targetFps} FPS` : `目标 ${targetFps} FPS`;
}
