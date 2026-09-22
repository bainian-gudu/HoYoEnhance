import type { GameId, GameProfile, UnlockerConfig } from './config';
import { isGameId } from './config';

export type ConfigPatch = Record<string, unknown>;

/**
 * 把「还没下发到宿主的本地改动」叠加到宿主推来的配置上。
 *
 * 宿主的 state 是它此刻知道的配置，pendingPatch 是用户刚改、还没生效的意图。节流窗口
 * （帧率 350ms、其它 80ms）内到达的状态推送必须让后者赢，否则帧率滑块与开关会被打回
 * 旧值、几百毫秒后再跳回来。只保护 activeGame 是不够的 —— 那只是其中一种字段。
 */
export function mergePendingPatch(config: UnlockerConfig, patch: ConfigPatch): UnlockerConfig {
  const keys = Object.keys(patch).filter((key) => key !== 'games' && key in config);
  const hasGames = patch.games !== undefined;
  if (!keys.length && !hasGames) return config;

  const next: Record<string, unknown> = { ...config };
  for (const key of keys) next[key] = patch[key];

  if (hasGames) {
    const games: Record<GameId, GameProfile> = { ...config.games };
    const incoming = patch.games;
    if (typeof incoming === 'object' && incoming !== null) {
      for (const [id, values] of Object.entries(incoming as Record<string, unknown>)) {
        if (!isGameId(id) || typeof values !== 'object' || values === null) continue;
        games[id] = { ...games[id], ...(values as Partial<GameProfile>) };
      }
    }
    next.games = games;
  }
  return next as unknown as UnlockerConfig;
}

/** 合并两次尚未下发的 patch：共享键直接覆盖，games[game] 深合并。 */
export function mergeQueuedPatch(current: ConfigPatch, patch: ConfigPatch): ConfigPatch {
  const merged: ConfigPatch = { ...current, ...patch };
  if (!patch.games) return merged;

  const previous = (current.games ?? {}) as Record<string, Record<string, unknown>>;
  const incoming = patch.games as Record<string, Record<string, unknown>>;
  const games: Record<string, Record<string, unknown>> = { ...previous };
  for (const [id, values] of Object.entries(incoming)) games[id] = { ...(previous[id] ?? {}), ...values };
  merged.games = games;
  return merged;
}
