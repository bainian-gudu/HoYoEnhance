import { describe, expect, it } from 'vitest';
import { createDefaultConfig } from './config';
import { mergePendingPatch, mergeQueuedPatch } from './configPatch';

describe('mergePendingPatch', () => {
  it('keeps local top-level and per-game changes while host state arrives', () => {
    const config = createDefaultConfig();
    const merged = mergePendingPatch(config, {
      masterEnabled: false,
      games: { starRail: { hideUid: true } },
      unknownKey: true,
    });

    expect(merged.masterEnabled).toBe(false);
    expect(merged.games.starRail.hideUid).toBe(true);
    expect((merged as unknown as Record<string, unknown>).unknownKey).toBeUndefined();
    expect(config.masterEnabled).toBe(true);
    expect(config.games.starRail.hideUid).toBe(false);
  });
});

describe('mergeQueuedPatch', () => {
  it('deep-merges game patches and lets the latest shared key win', () => {
    const first = mergeQueuedPatch({}, {
      masterEnabled: false,
      games: { genshin: { targetFps: 60 } },
    });
    const second = mergeQueuedPatch(first, {
      masterEnabled: true,
      games: {
        genshin: { enabled: false },
        starRail: { hideUid: true },
      },
    });

    expect(second.masterEnabled).toBe(true);
    expect(second.games).toEqual({
      genshin: { targetFps: 60, enabled: false },
      starRail: { hideUid: true },
    });
    expect(first.games).toEqual({ genshin: { targetFps: 60 } });
  });
});
