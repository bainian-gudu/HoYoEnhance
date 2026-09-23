import { describe, expect, it } from 'vitest';
import { runtimeFpsText } from './runtimeStatus';

describe('runtimeFpsText', () => {
  it('星铁附着画面效果时仍显示已核对到的注册表帧率', () => {
    expect(runtimeFpsText({
      running: true,
      attached: true,
      registryBased: true,
      registryFps: 120,
      currentFps: 0,
      targetFps: 120,
    })).toBe('注册表当前设置 120 FPS');
  });

  it('星铁注册表检查失败或未完成时不伪报固定目标帧率', () => {
    expect(runtimeFpsText({
      running: true,
      attached: true,
      registryBased: true,
      registryFps: null,
      currentFps: 0,
      targetFps: 120,
    })).toBe('注册表帧率尚未确认');
  });

  it('兼容宿主状态缺少注册表帧率字段', () => {
    expect(runtimeFpsText({
      running: true,
      attached: true,
      registryBased: true,
      registryFps: undefined,
      currentFps: 0,
      targetFps: 120,
    })).toBe('注册表帧率尚未确认');
  });

  it('星铁注册表实际不是 120 时显示实际值', () => {
    expect(runtimeFpsText({
      running: true,
      attached: false,
      registryBased: true,
      registryFps: 60,
      currentFps: 0,
      targetFps: 120,
    })).toBe('注册表当前设置 60 FPS');
  });

  it('原神附着时显示 Stub 的实时反馈', () => {
    expect(runtimeFpsText({
      running: true,
      attached: true,
      registryBased: false,
      registryFps: null,
      currentFps: 144,
      targetFps: 144,
    })).toBe('144 → 144 FPS');
  });

  it('未附着且没有注册表帧率时显示未注入', () => {
    expect(runtimeFpsText({
      running: true,
      attached: false,
      registryBased: false,
      registryFps: null,
      currentFps: 0,
      targetFps: 120,
    })).toBe('未注入');
  });
});
