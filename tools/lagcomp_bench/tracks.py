"""Track extraction: contiguous flight segments per player slot.

A segment is a maximal run of frames where the ship is present AND alive,
split whenever the time gap between consecutive kept frames exceeds
``max_gap`` seconds, the position jumps more than ``max_jump`` meters
(respawn/teleport), or time goes backwards. Segments shorter than
``min_duration`` seconds are discarded.
"""

try:
    import numpy as np
except ImportError:
    np = None

DEFAULT_MAX_GAP = 0.5      # seconds
DEFAULT_MAX_JUMP = 20.0    # meters
DEFAULT_MIN_DURATION = 2.0  # seconds


class Segment(object):
    """One contiguous flight of one player. All arrays share length N."""

    __slots__ = ("file", "player", "slot", "t", "pos", "vel", "rot", "vrot",
                 "cache")

    def __init__(self, file, player, slot, t, pos, vel, rot, vrot):
        self.file = file
        self.player = player
        self.slot = slot
        self.t = t        # (N,)  f32 seconds
        self.pos = pos    # (N,3) f32 m
        self.vel = vel    # (N,3) f32 m/s
        self.rot = rot    # (N,4) f32 quaternion xyzw
        self.vrot = vrot  # (N,3) f32 rad/s
        self.cache = None  # lazily-created dict: predictor state memoization

    @property
    def duration(self):
        return float(self.t[-1] - self.t[0])

    def __len__(self):
        return len(self.t)

    def __repr__(self):
        return "Segment(%r, slot=%d, n=%d, %.1fs)" % (
            self.player, self.slot, len(self.t), self.duration)


def extract_tracks(replay, max_gap=DEFAULT_MAX_GAP, max_jump=DEFAULT_MAX_JUMP,
                   min_duration=DEFAULT_MIN_DURATION):
    """Return a list of Segment for all slots of a parsed olr.Replay."""
    if np is None:
        raise RuntimeError("tracks.py requires numpy (pip install numpy)")

    segs = []
    t_all = np.asarray(replay.t, dtype=np.float64)
    for slot, sd in enumerate(replay.slots):
        name = replay.players[slot].name
        mask = np.asarray(sd.present) & np.asarray(sd.alive)
        keep = np.flatnonzero(mask)
        if keep.size < 2:
            continue
        t = t_all[keep]
        pos = sd.pos[keep]
        vel = sd.vel[keep]
        rot = sd.rot[keep]
        vrot = sd.vrot[keep]

        dt = np.diff(t)
        jump = np.linalg.norm(np.diff(pos, axis=0), axis=1)
        cut = (dt > max_gap) | (dt <= 0) | (jump > max_jump)
        bounds = np.flatnonzero(cut) + 1
        starts = np.concatenate(([0], bounds))
        ends = np.concatenate((bounds, [keep.size]))
        for a, b in zip(starts, ends):
            if b - a < 3:
                continue
            if t[b - 1] - t[a] < min_duration:
                continue
            segs.append(Segment(
                replay.path, name, slot,
                t[a:b].astype(np.float64),
                np.ascontiguousarray(pos[a:b]),
                np.ascontiguousarray(vel[a:b]),
                np.ascontiguousarray(rot[a:b]),
                np.ascontiguousarray(vrot[a:b]),
            ))
    return segs
