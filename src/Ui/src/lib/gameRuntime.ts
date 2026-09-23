import type { GameId } from './config';

export interface GameRuntimeSnapshot {
  runningGame: GameId | null;
  runningPids: Record<GameId, number>;
  attachedGame: GameId | null;
  attachedPid: number;
}

export interface GameRuntimeState {
  running: boolean;
  attached: boolean;
  pid: number;
}

export function gameRuntimeState(game: GameId, snapshot: GameRuntimeSnapshot): GameRuntimeState {
  const pid = snapshot.runningPids[game] > 0
    ? snapshot.runningPids[game]
    : snapshot.attachedGame === game ? snapshot.attachedPid : 0;
  return {
    running: pid > 0 || snapshot.runningGame === game,
    attached: snapshot.attachedGame === game,
    pid,
  };
}
