import { describe, expect, it } from 'vitest';
import { runtimeFpsText } from './runtimeStatus';

describe('runtimeFpsText', () => {
  it('星铁附着画面效果时仍显示注册表解锁状态', () => {
    expect(runtimeFpsText({
      running: true,
      attached: true,
      registryFps: 120,
      currentFps: 0,
      targetFps: 120,
    })).toBe('由注册表解锁 120 FPS');
  });

  it('原神附着时显示 Stub 的实时反馈', () => {
    expect(runtimeFpsText({
      running: true,
      attached: true,
      currentFps: 144,
      targetFps: 144,
    })).toBe('144 → 144 FPS');
  });

  it('未附着且没有注册表帧率时显示未注入', () => {
    expect(runtimeFpsText({
      running: true,
      attached: false,
      currentFps: 0,
      targetFps: 120,
    })).toBe('未注入');
  });
});
