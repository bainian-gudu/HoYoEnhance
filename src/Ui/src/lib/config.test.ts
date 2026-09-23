import { describe, expect, it } from 'vitest';
import { createDefaultConfig, DEFAULT_GAME_ID, GAME_CATALOG, GAME_IDS, GAME_META, isValidGamePath, parseConfig } from './config';

describe('game metadata contract', () => {
  it('uses the generated catalog for shared game capabilities', () => {
    expect(DEFAULT_GAME_ID).toBe('genshin');
    expect(GAME_IDS).toEqual(['genshin', 'starRail']);
    expect(GAME_META.genshin.name).toBe(GAME_CATALOG.genshin.displayName);
    expect(GAME_META.starRail.injection.module).toBe(GAME_CATALOG.starRail.stubFileName);
    expect(GAME_META.starRail.fpsLock?.value).toBe(GAME_CATALOG.starRail.lockedFps);
    expect(GAME_META.genshin.injection.features.map(({ key }) => key)).toContain('antiBlurDiveMosaic');
    expect(GAME_META.starRail.injection.features.map(({ key }) => key)).not.toContain('antiBlurDiveMosaic');
  });
});

describe('parseConfig', () => {
  it('rejects values that are not JSON objects', () => {
    expect(() => parseConfig(null)).toThrow('配置文件必须是一个 JSON 对象');
    expect(() => parseConfig([])).toThrow('配置文件必须是一个 JSON 对象');
  });

  it('accepts valid shared fields and ignores unknown keys', () => {
    const config = parseConfig({
      activeGame: 'starRail',
      masterEnabled: false,
      pollIntervalMs: 2500,
      logLevel: 'Warn',
      unknownKey: 'ignored',
    });

    expect(config.activeGame).toBe('starRail');
    expect(config.masterEnabled).toBe(false);
    expect(config.pollIntervalMs).toBe(2500);
    expect(config.logLevel).toBe('Warn');
    expect((config as unknown as Record<string, unknown>).unknownKey).toBeUndefined();
  });

  it('keeps background monitoring enabled when loading a legacy disabled value', () => {
    expect(parseConfig({ autoWatch: false }).autoWatch).toBe(true);
  });

  it('rejects invalid booleans and ranges', () => {
    expect(() => parseConfig({ masterEnabled: 'yes' })).toThrow('masterEnabled 必须为 true 或 false');
    expect(() => parseConfig({ pollIntervalMs: 10 })).toThrow('pollIntervalMs 必须是 200 至 10000 之间的整数');
    expect(() => parseConfig({ logRetainDays: 0 })).toThrow('logRetainDays 必须是 1 至 90 之间的整数');
    expect(() => parseConfig({ logLevel: 'Verbose' })).toThrow('不支持的日志级别');
  });

  it('normalizes a registry-locked game to its fixed FPS', () => {
    const config = parseConfig({ games: { starRail: { targetFps: 60 } } });
    expect(config.games.starRail.targetFps).toBe(120);
  });

  it('validates game paths against the selected game', () => {
    const config = parseConfig({
      games: {
        genshin: { gamePath: 'C:\\Games\\Genshin Impact\\Genshin Impact Game\\YuanShen.exe' },
      },
    });
    expect(config.games.genshin.gamePath).toBe('C:\\Games\\Genshin Impact\\Genshin Impact Game\\YuanShen.exe');
    expect(isValidGamePath('C:\\Games\\Star Rail\\Game\\StarRail.exe', 'starRail')).toBe(true);
    expect(() => parseConfig({ games: { starRail: { gamePath: 'C:\\Games\\Genshin Impact\\YuanShen.exe' } } }))
      .toThrow('游戏路径无效');
  });

  it('creates independent default game profiles', () => {
    const first = createDefaultConfig();
    const second = createDefaultConfig();
    first.games.genshin.hideUid = true;
    expect(second.games.genshin.hideUid).toBe(false);
  });
});
