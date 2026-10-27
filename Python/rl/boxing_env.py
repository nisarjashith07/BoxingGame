"""
boxing_env.py
=============
Gymnasium environment simulating a boxing match for RL training.
Agent (AI) vs Scripted Opponent - no Unity required.

Observation Space: Box(18,) float32
  [distance, selfHealth, selfStamina, oppHealth, oppStamina,
   selfGuard, oppGuard, relX, relZ, relVX, relVZ,
   oppAttackIntent, incomingAttack, oppRecovering,
   selfCanAttack, selfCanDodge, selfCanBlock, healthAdvantage]

Action Space: Discrete(12)
  0-Idle, 1-MoveForward, 2-MoveBackward, 3-StrafeLeft, 4-StrafeRight,
  5-Jab, 6-Cross, 7-HookLeft, 8-HookRight,
  9-Block, 10-DodgeLeft, 11-DodgeRight
"""

from __future__ import annotations

import numpy as np
import gymnasium as gym
from gymnasium import spaces
from typing import Optional, Tuple, Dict, Any

# --- Constants ----------------------------------------------------------------

MAX_HP          = 100.0
MAX_STAMINA     = 100.0
MAX_STEPS       = 300          # 1 round
ARENA_HALF      = 5.0          # half the ring size
IDEAL_RANGE     = 1.5          # optimal punching distance

# Punch definitions: (stamina_cost, base_damage, range, startup_frames)
PUNCH_STATS = {
    5:  dict(cost=8.0,  dmg=10.0, rng=1.8, startup=2),   # Jab
    6:  dict(cost=12.0, dmg=15.0, rng=1.6, startup=3),   # Cross
    7:  dict(cost=14.0, dmg=18.0, rng=1.4, startup=4),   # HookLeft
    8:  dict(cost=14.0, dmg=18.0, rng=1.4, startup=4),   # HookRight
}

MOVE_SPEED      = 0.25   # units per step
DODGE_DURATION  = 3      # steps of invulnerability
DODGE_COOLDOWN  = 8      # steps before next dodge allowed
BLOCK_REDUCTION = 0.35   # damage multiplier when blocking (block absorbs 65%)
STAMINA_REGEN   = 3.0    # per step
STAMINA_BLOCK   = 1.5    # stamina drained per step while blocking
ATTACK_COOLDOWN = 6      # steps between attacks

# --- Fighter State -------------------------------------------------------------

class FighterState:
    """Mutable state for one fighter."""

    def __init__(self, x: float = 0.0, z: float = 0.0):
        self.hp: float          = MAX_HP
        self.stamina: float     = MAX_STAMINA
        self.x: float           = x
        self.z: float           = z
        self.vx: float          = 0.0
        self.vz: float          = 0.0
        self.attack_cd: int     = 0    # steps until next attack allowed
        self.dodge_cd: int      = 0    # steps until next dodge allowed
        self.dodge_active: int  = 0    # steps of active invulnerability remaining
        self.is_blocking: bool  = False
        self.is_recovering: bool= False
        self.recover_timer: int = 0    # steps of recovery remaining
        self.guard: float       = 1.0  # guard integrity [0,1]
        self.guard_regen: int   = 0    # steps until guard starts regenerating

    @property
    def alive(self) -> bool:
        return self.hp > 0.0

    @property
    def can_attack(self) -> bool:
        return (self.attack_cd == 0
                and not self.is_recovering
                and self.stamina >= 8.0)

    @property
    def can_dodge(self) -> bool:
        return (self.dodge_cd == 0
                and not self.is_recovering
                and self.stamina >= 10.0)

    @property
    def can_block(self) -> bool:
        return not self.is_recovering and self.stamina >= 5.0

    def reset(self, x: float = 0.0, z: float = 0.0):
        self.__init__(x, z)


# --- Scripted Opponent ---------------------------------------------------------

class ScriptedOpponent:
    """
    Rule-based opponent with three difficulty profiles.
    Mimics the Easy/Medium/Hard C# profiles.
    """

    PROFILES = {
        'easy': dict(
            reaction_prob=0.30,    # prob of reacting to incoming attack
            attack_prob=0.25,      # prob of attacking when in range
            dodge_prob=0.35,       # prob of dodging incoming
            block_prob=0.45,
            aggression=0.3,
            retreat_hp=0.0,        # never retreats
        ),
        'medium': dict(
            reaction_prob=0.55,
            attack_prob=0.50,
            dodge_prob=0.55,
            block_prob=0.60,
            aggression=0.55,
            retreat_hp=0.25,
        ),
        'hard': dict(
            reaction_prob=0.85,
            attack_prob=0.80,
            dodge_prob=0.75,
            block_prob=0.80,
            aggression=0.80,
            retreat_hp=0.15,
        ),
    }

    def __init__(self, difficulty: str = 'medium', rng: np.random.Generator = None):
        self.profile = self.PROFILES[difficulty]
        self.rng     = rng if rng is not None else np.random.default_rng()

    def choose_action(
        self,
        self_state:  FighterState,
        agent_state: FighterState,
        agent_attacking: bool,
        distance: float,
        step: int,
    ) -> int:
        """Return a BoxingAction int for the scripted opponent."""
        p = self.profile
        r = self.rng.random

        # -- Recovery: forced idle ------------------------------------------
        if self_state.is_recovering:
            return 0  # Idle

        # -- React to incoming attack ---------------------------------------
        if agent_attacking and r() < p['reaction_prob']:
            if self_state.can_dodge and r() < p['dodge_prob']:
                return self.rng.choice([10, 11])  # DodgeLeft / DodgeRight
            if self_state.can_block and r() < p['block_prob']:
                return 9  # Block
            # Retreat
            return 2  # MoveBackward

        # -- Low HP retreat -------------------------------------------------
        if self_state.hp / MAX_HP < p['retreat_hp']:
            if distance < 2.0:
                return 2  # MoveBackward

        # -- In range -> attack ----------------------------------------------
        if distance < IDEAL_RANGE * 1.2 and self_state.can_attack:
            if r() < p['attack_prob']:
                # Weight toward jab (fast), sometimes hooks
                attack_choice = self.rng.choice([5, 5, 6, 7, 8],
                                                p=[0.35, 0.25, 0.20, 0.10, 0.10])
                return int(attack_choice)

        # -- Close the gap -------------------------------------------------
        if distance > IDEAL_RANGE:
            if r() < p['aggression']:
                return 1  # MoveForward
            # Slight lateral movement
            return int(self.rng.choice([3, 4]))

        # -- Default: idle or lateral ---------------------------------------
        return int(self.rng.choice([0, 3, 4], p=[0.5, 0.25, 0.25]))


# --- Environment --------------------------------------------------------------

class BoxingEnv(gym.Env):
    """
    Boxing RL environment.

    Parameters
    ----------
    difficulty : str
        'easy' | 'medium' | 'hard' - controls the scripted opponent.
    render_mode : str | None
        Only 'human' (text) supported.
    seed : int | None
        RNG seed.
    """

    metadata = {"render_modes": ["human"]}

    def __init__(
        self,
        difficulty: str = 'medium',
        render_mode: Optional[str] = None,
        seed: Optional[int] = None,
    ):
        super().__init__()
        self.difficulty  = difficulty
        self.render_mode = render_mode
        self._seed       = seed

        # Spaces
        self.observation_space = spaces.Box(
            low=-np.inf, high=np.inf, shape=(18,), dtype=np.float32
        )
        self.action_space = spaces.Discrete(12)

        # Internal state (allocated once, reset per episode)
        self._rng:     np.random.Generator = np.random.default_rng(seed)
        self._agent:   FighterState        = FighterState()
        self._opp:     FighterState        = FighterState()
        self._opponent = ScriptedOpponent(difficulty, self._rng)
        self._step:    int                 = 0
        self._agent_attacking: bool        = False
        self._opp_attacking:   bool        = False

    # -- Gymnasium API ---------------------------------------------------------

    def reset(
        self,
        *,
        seed: Optional[int] = None,
        options: Optional[Dict] = None,
    ) -> Tuple[np.ndarray, Dict]:
        super().reset(seed=seed)
        if seed is not None:
            self._rng = np.random.default_rng(seed)
            self._opponent = ScriptedOpponent(self.difficulty, self._rng)

        self._agent.reset(x=0.0, z=-1.5)
        self._opp.reset(x=0.0, z=1.5)
        self._step           = 0
        self._agent_attacking = False
        self._opp_attacking   = False

        return self._get_obs(), {}

    def step(self, action: int) -> Tuple[np.ndarray, float, bool, bool, Dict]:
        agent = self._agent
        opp   = self._opp

        # -- Tick cooldowns & timers ----------------------------------------
        self._tick_timers(agent)
        self._tick_timers(opp)

        # -- Scripted opponent chooses action ------------------------------
        distance = self._distance()
        opp_action = self._opponent.choose_action(
            opp, agent,
            agent_attacking=self._agent_attacking,
            distance=distance,
            step=self._step,
        )

        # -- Apply actions -------------------------------------------------
        reward = 0.0

        # Agent action
        agent_punch_damage = 0.0
        self._agent_attacking = False
        agent_blocked_hit     = False
        agent_dodged_hit      = False

        if action in (5, 6, 7, 8):  # Punch
            self._agent_attacking = True
            if agent.can_attack:
                stats = PUNCH_STATS[action]
                if agent.stamina >= stats['cost']:
                    agent.stamina    = max(0, agent.stamina - stats['cost'])
                    agent.attack_cd  = ATTACK_COOLDOWN
                    agent.is_recovering = True
                    agent.recover_timer = stats['startup']
                    if distance <= stats['rng']:
                        raw_dmg = stats['dmg'] * agent.guard
                        if opp.dodge_active > 0:
                            agent_punch_damage = 0.0          # Dodged
                        elif opp.is_blocking and opp.can_block:
                            agent_punch_damage = raw_dmg * BLOCK_REDUCTION
                            opp.guard = max(0.0, opp.guard - 0.15)
                            opp.stamina = max(0, opp.stamina - 5.0)
                        else:
                            agent_punch_damage = raw_dmg
                            if not opp.is_blocking:
                                opp.guard = max(0.0, opp.guard - 0.10)
                        opp.hp = max(0.0, opp.hp - agent_punch_damage)

        elif action == 9:  # Block
            if agent.can_block:
                agent.is_blocking = True
                agent.stamina     = max(0, agent.stamina - STAMINA_BLOCK)
            else:
                agent.is_blocking = False

        elif action == 10:  # DodgeLeft
            if agent.can_dodge:
                agent.stamina      = max(0, agent.stamina - 10.0)
                agent.dodge_active = DODGE_DURATION
                agent.dodge_cd     = DODGE_COOLDOWN
                agent.x           -= MOVE_SPEED * 2

        elif action == 11:  # DodgeRight
            if agent.can_dodge:
                agent.stamina      = max(0, agent.stamina - 10.0)
                agent.dodge_active = DODGE_DURATION
                agent.dodge_cd     = DODGE_COOLDOWN
                agent.x           += MOVE_SPEED * 2

        elif action == 1:  # MoveForward (toward opp)
            sign = np.sign(opp.z - agent.z) or 1.0
            agent.z = np.clip(agent.z + sign * MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        elif action == 2:  # MoveBackward
            sign = np.sign(agent.z - opp.z) or 1.0
            agent.z = np.clip(agent.z + sign * MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        elif action == 3:  # StrafeLeft
            agent.x = np.clip(agent.x - MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        elif action == 4:  # StrafeRight
            agent.x = np.clip(agent.x + MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        # Non-block actions reset blocking state
        if action != 9:
            agent.is_blocking = False

        # -- Apply opponent action -----------------------------------------
        self._opp_attacking = False
        opp_punch_damage    = 0.0

        if opp_action in (5, 6, 7, 8):
            self._opp_attacking = True
            if opp.can_attack:
                stats = PUNCH_STATS[opp_action]
                if opp.stamina >= stats['cost']:
                    opp.stamina     = max(0, opp.stamina - stats['cost'])
                    opp.attack_cd   = ATTACK_COOLDOWN
                    opp.is_recovering = True
                    opp.recover_timer = stats['startup']
                    dist_now = self._distance()
                    if dist_now <= stats['rng']:
                        raw_dmg = stats['dmg'] * opp.guard
                        if agent.dodge_active > 0:
                            opp_punch_damage = 0.0
                            agent_dodged_hit = True
                        elif agent.is_blocking and agent.can_block:
                            opp_punch_damage = raw_dmg * BLOCK_REDUCTION
                            agent.guard = max(0.0, agent.guard - 0.15)
                            agent.stamina = max(0, agent.stamina - 5.0)
                            agent_blocked_hit = True
                        else:
                            opp_punch_damage = raw_dmg
                            if not agent.is_blocking:
                                agent.guard = max(0.0, agent.guard - 0.10)
                        agent.hp = max(0.0, agent.hp - opp_punch_damage)

        elif opp_action == 9:
            if opp.can_block:
                opp.is_blocking = True
                opp.stamina     = max(0, opp.stamina - STAMINA_BLOCK)
            else:
                opp.is_blocking = False

        elif opp_action == 10:
            if opp.can_dodge:
                opp.stamina      = max(0, opp.stamina - 10.0)
                opp.dodge_active = DODGE_DURATION
                opp.dodge_cd     = DODGE_COOLDOWN
                opp.x           -= MOVE_SPEED * 2

        elif opp_action == 11:
            if opp.can_dodge:
                opp.stamina      = max(0, opp.stamina - 10.0)
                opp.dodge_active = DODGE_DURATION
                opp.dodge_cd     = DODGE_COOLDOWN
                opp.x           += MOVE_SPEED * 2

        elif opp_action == 1:
            sign = np.sign(agent.z - opp.z) or 1.0
            opp.z = np.clip(opp.z + sign * MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        elif opp_action == 2:
            sign = np.sign(opp.z - agent.z) or 1.0
            opp.z = np.clip(opp.z + sign * MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        elif opp_action == 3:
            opp.x = np.clip(opp.x - MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        elif opp_action == 4:
            opp.x = np.clip(opp.x + MOVE_SPEED, -ARENA_HALF, ARENA_HALF)

        if opp_action != 9:
            opp.is_blocking = False

        # -- Stamina regeneration ------------------------------------------
        if action not in (5, 6, 7, 8, 9):
            agent.stamina = min(MAX_STAMINA, agent.stamina + STAMINA_REGEN)
        if opp_action not in (5, 6, 7, 8, 9):
            opp.stamina = min(MAX_STAMINA, opp.stamina + STAMINA_REGEN)

        # Guard regeneration (slow)
        if agent.guard < 1.0 and not self._opp_attacking:
            agent.guard = min(1.0, agent.guard + 0.02)
        if opp.guard < 1.0 and not self._agent_attacking:
            opp.guard = min(1.0, opp.guard + 0.02)

        # Update relative velocities (simple)
        agent.vx = float(np.clip(opp.x - agent.x, -1, 1))
        agent.vz = float(np.clip(opp.z - agent.z, -1, 1))

        self._step += 1

        # -- Termination ---------------------------------------------------
        agent_won  = opp.hp <= 0.0
        agent_lost = agent.hp <= 0.0
        time_out   = self._step >= MAX_STEPS
        terminated = agent_won or agent_lost
        truncated  = time_out and not terminated

        # -- Reward shaping ------------------------------------------------
        # Punch landed
        if agent_punch_damage > 0:
            reward += 5.0 * (agent_punch_damage / 18.0)   # scaled ~2.8-5
        # Punch received
        if opp_punch_damage > 0:
            reward -= 3.0 * (opp_punch_damage / 18.0)
        # Block success
        if agent_blocked_hit:
            reward += 2.0
        # Dodge success
        if agent_dodged_hit:
            reward += 1.5
        # Idle with incoming attack opportunity
        if action == 0 and self._opp_attacking and agent.can_attack:
            reward -= 0.5
        # Health advantage per step
        health_adv = (agent.hp - opp.hp) / MAX_HP
        reward += 0.1 * health_adv
        # Living penalty
        reward -= 0.01

        # Win/loss terminal rewards
        if terminated or truncated:
            if agent_won:
                reward += 15.0
            elif agent_lost:
                reward -= 10.0
            elif time_out:
                # Partial reward for health difference
                reward += 5.0 * health_adv

        obs  = self._get_obs()
        info = {
            'agent_hp':  agent.hp,
            'opp_hp':    opp.hp,
            'win':       agent_won,
            'loss':      agent_lost,
            'time_out':  time_out,
            'step':      self._step,
        }

        if self.render_mode == 'human':
            self._render_text(action, opp_action, reward, info)

        return obs, reward, terminated, truncated, info

    # -- Helpers ---------------------------------------------------------------

    def _distance(self) -> float:
        dx = self._agent.x - self._opp.x
        dz = self._agent.z - self._opp.z
        return float(np.sqrt(dx * dx + dz * dz))

    def _tick_timers(self, f: FighterState):
        if f.attack_cd > 0:
            f.attack_cd -= 1
        if f.dodge_cd > 0:
            f.dodge_cd -= 1
        if f.dodge_active > 0:
            f.dodge_active -= 1
        if f.is_recovering:
            f.recover_timer -= 1
            if f.recover_timer <= 0:
                f.is_recovering = False

    def _get_obs(self) -> np.ndarray:
        a = self._agent
        o = self._opp
        dist = self._distance()
        rel_x = float(o.x - a.x)
        rel_z = float(o.z - a.z)
        health_adv = float((a.hp - o.hp) / MAX_HP)

        obs = np.array([
            dist / (ARENA_HALF * 2),              # distance (normalised)
            a.hp        / MAX_HP,                 # selfHealth01
            a.stamina   / MAX_STAMINA,            # selfStamina01
            o.hp        / MAX_HP,                 # opponentHealth01
            o.stamina   / MAX_STAMINA,            # opponentStamina01
            a.guard,                              # selfGuard01
            o.guard,                              # opponentGuard01
            float(np.clip(rel_x / ARENA_HALF, -1, 1)),  # relativeX
            float(np.clip(rel_z / ARENA_HALF, -1, 1)),  # relativeZ
            float(np.clip(a.vx, -1, 1)),          # relativeVelocityX
            float(np.clip(a.vz, -1, 1)),          # relativeVelocityZ
            float(self._opp_attacking),           # opponentAttackIntent01
            float(self._opp_attacking),           # incomingAttack (bool)
            float(o.is_recovering),               # opponentRecovering
            float(a.can_attack),                  # selfCanAttack
            float(a.can_dodge),                   # selfCanDodge
            float(a.can_block),                   # selfCanBlock
            health_adv,                           # healthAdvantage (extra feature)
        ], dtype=np.float32)
        return obs

    def _render_text(self, agent_act: int, opp_act: int, reward: float, info: dict):
        ACT_NAMES = [
            'Idle','MvFwd','MvBck','StrafeL','StrafeR',
            'Jab','Cross','HookL','HookR','Block','DodgeL','DodgeR'
        ]
        a, o = self._agent, self._opp
        print(
            f"[{self._step:3d}] "
            f"AgentHP={a.hp:5.1f} OppHP={o.hp:5.1f} "
            f"Dist={self._distance():.2f} "
            f"AgentAct={ACT_NAMES[agent_act]:8s} OppAct={ACT_NAMES[opp_act]:8s} "
            f"Reward={reward:+.3f}"
        )

    def render(self):
        if self.render_mode == 'human':
            pass  # output is inline in step()

    def close(self):
        pass

    def set_difficulty(self, difficulty: str):
        """Hot-swap difficulty without resetting."""
        self.difficulty  = difficulty
        self._opponent   = ScriptedOpponent(difficulty, self._rng)


# --- Quick smoke-test ----------------------------------------------------------

if __name__ == '__main__':
    env = BoxingEnv(difficulty='medium', render_mode='human')
    obs, _ = env.reset(seed=42)
    print(f"Obs shape: {obs.shape}, dtype: {obs.dtype}")
    total_r = 0.0
    for _ in range(300):
        action = env.action_space.sample()
        obs, r, term, trunc, info = env.step(action)
        total_r += r
        if term or trunc:
            print(f"Episode ended: {info}, total_reward={total_r:.2f}")
            break
    env.close()

