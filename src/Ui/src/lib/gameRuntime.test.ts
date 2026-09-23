import { describe, expect, it } from 'vitest';
import { gameRuntimeState } from './gameRuntime';

describe('gameRuntimeState', () => {
  it('uses the per-game process snapshot even when another game is selected', () => {
    expect(gameRuntimeState('starRail', {
      runningGame: 'genshin',
      runningPids: { genshin: 123, starRail: 456 },
      attachedGame: null,
      attachedPid: 0,
    })).toEqual({ running: true, attached: false, pid: 456 });
  });

  it('uses the attached PID when the process snapshot has not caught up yet', () => {
    expect(gameRuntimeState('genshin', {
      runningGame: null,
      runningPids: { genshin: 0, starRail: 0 },
      attachedGame: 'genshin',
      attachedPid: 321,
    })).toEqual({ running: true, attached: true, pid: 321 });
  });

  it('does not borrow another game PID or attachment', () => {
    expect(gameRuntimeState('starRail', {
      runningGame: 'genshin',
      runningPids: { genshin: 123, starRail: 0 },
      attachedGame: 'genshin',
      attachedPid: 123,
    })).toEqual({ running: false, attached: false, pid: 0 });
  });
});
