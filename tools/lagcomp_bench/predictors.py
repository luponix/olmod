"""Extrapolation predictors for the lag-compensation benchmark.

Interface: a Predictor has a ``name`` and a vectorized

    predict(seg, idx, h) -> (len(idx), 3) float array

which forecasts the ship position at ``seg.t[i] + h`` for every i in ``idx``
using ONLY the snapshot state at index i (and, for future predictors, any
history at indices <= i). ``h`` is the horizon in seconds.

Register additional predictors with ``register()`` (or just append to
``default_predictors()``'s result in bench.py via --predictors).
"""

import numpy as np

RAD2DEG = 57.29577951308232  # matches UnityEngine.Mathf.Rad2Deg


# ---------------------------------------------------------------------------
# Unity-convention quaternion math (xyzw layout, q1*q2 applies q2 first).
# ---------------------------------------------------------------------------

def quat_mul(a, b):
    """Hamilton product a*b for (...,4) xyzw arrays (Unity convention)."""
    ax, ay, az, aw = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    bx, by, bz, bw = b[..., 0], b[..., 1], b[..., 2], b[..., 3]
    return np.stack([
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by + ay * bw + az * bx - ax * bz,
        aw * bz + az * bw + ax * by - ay * bx,
        aw * bw - ax * bx - ay * by - az * bz,
    ], axis=-1)


def quat_conj(q):
    out = q.copy()
    out[..., :3] *= -1
    return out


def quat_rotate(q, v):
    """Rotate (...,3) vectors v by (...,4) unit quaternions q (q*v*q^-1)."""
    u = q[..., :3]
    w = q[..., 3:4]
    t = 2.0 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


def unity_euler_quat(deg):
    """UnityEngine.Quaternion.Euler for (...,3) degree vectors.

    Unity applies the rotations around world axes in Z, X, Y order,
    i.e. q = qy * qx * qz in Unity's multiplication convention.
    """
    half = np.radians(deg) * 0.5
    sx, cx = np.sin(half[..., 0]), np.cos(half[..., 0])
    sy, cy = np.sin(half[..., 1]), np.cos(half[..., 1])
    sz, cz = np.sin(half[..., 2]), np.cos(half[..., 2])
    zeros = np.zeros_like(sx)
    qx = np.stack([sx, zeros, zeros, cx], axis=-1)
    qy = np.stack([zeros, sy, zeros, cy], axis=-1)
    qz = np.stack([zeros, zeros, sz, cz], axis=-1)
    return quat_mul(qy, quat_mul(qx, qz))


# ---------------------------------------------------------------------------
# Predictors
# ---------------------------------------------------------------------------

class Predictor(object):
    name = "base"

    def predict(self, seg, idx, h):
        raise NotImplementedError


class Hold(Predictor):
    """No extrapolation: show the last received position (pure interpolation)."""

    name = "hold"

    def predict(self, seg, idx, h):
        return seg.pos[idx].astype(np.float64)


class ConstantVelocity(Predictor):
    """olmod's current extrapolation: pos + vel * h.

    Matches MPClientExtrapolation.extrapolatePlayer (mode 0/1 position path):
        newPos = Vector3.LerpUnclamped(pos, pos + vel, t)
    """

    name = "cv"

    def predict(self, seg, idx, h):
        return seg.pos[idx].astype(np.float64) + seg.vel[idx].astype(np.float64) * h


class MotionArc(Predictor):
    """olmod "Motion Arc" mode (mms_lag_compensation_prediction_mode == 2).

    Faithful replication of MPClientExtrapolation.extrapolatePlayer
    (GameMod/MPClientExtrapolation.cs:488) at rotation strength 100%
    (rotScale = 1.0, arcRotScale = 0.34):

        newRot = Quaternion.Euler(vrot * Rad2Deg * t * rotScale) * rot
        newPos = pos + vel * t
        rotPos = pos + ((Inverse(rot) * newRot) * vel) * t
        result = Lerp(newPos, rotPos, arcRotScale)
    """

    name = "cv_arc"

    def __init__(self, rot_scale=1.0, arc_blend=0.34):
        self.rot_scale = rot_scale
        self.arc_blend = arc_blend

    def predict(self, seg, idx, h):
        pos = seg.pos[idx].astype(np.float64)
        vel = seg.vel[idx].astype(np.float64)
        rot = seg.rot[idx].astype(np.float64)
        vrot = seg.vrot[idx].astype(np.float64)

        deg = vrot * (RAD2DEG * h * self.rot_scale)
        q_delta = unity_euler_quat(deg)
        new_rot = quat_mul(q_delta, rot)
        rel = quat_mul(quat_conj(rot), new_rot)  # Inverse(rot) * newRot
        vel_arc = quat_rotate(rel, vel)

        lin = pos + vel * h
        arc = pos + vel_arc * h
        return lin + (arc - lin) * self.arc_blend


# ---------------------------------------------------------------------------
# Causal state-estimation helpers.
#
# Everything below is semantically a per-frame incremental update (EWMA /
# exponential decay / hysteresis / event counters) exactly as a real-time C#
# client would run it on each arriving snapshot. It is merely *evaluated*
# vectorized here, via a chunked linear scan, for speed. No helper ever looks
# at samples after the current index.
# ---------------------------------------------------------------------------

DRAG_K = 2.5  # Overload rigidbody linear drag (s^-1)

_SCAN_BLOCK = 64
_WORLD_UP = np.array([0.0, 1.0, 0.0])
_WORLD_X = np.array([1.0, 0.0, 0.0])


def _scan(e, u, carry):
    """Linear recurrence y_i = e_i * y_{i-1} + u_i, y_{-1} = carry.

    e: (N,) decay factors in (~1e-4, 1]; u: (N,) or (N,D); carry scalar/(D,).
    Chunked so the cumprod trick never underflows.
    """
    u = np.asarray(u, np.float64)
    n = e.shape[0]
    vec = u.ndim == 2
    y = np.empty_like(u)
    carry = np.asarray(carry, np.float64)
    for s in range(0, n, _SCAN_BLOCK):
        eb = e[s:s + _SCAN_BLOCK]
        ub = u[s:s + _SCAN_BLOCK]
        cp = np.cumprod(eb)
        inv = 1.0 / cp
        if vec:
            acc = np.cumsum(ub * inv[:, None], axis=0)
            yb = cp[:, None] * (carry[None, :] + acc)
        else:
            acc = np.cumsum(ub * inv, axis=0)
            yb = cp * (carry + acc)
        y[s:s + _SCAN_BLOCK] = yb
        carry = yb[-1]
    return y


def _decays(t, tau):
    """Per-sample decay factors exp(-dt/tau); dt[0] = 0 (factor 1)."""
    dt = np.empty(len(t))
    dt[0] = 0.0
    dt[1:] = np.diff(t)
    np.clip(dt, 0.0, None, out=dt)
    e = np.exp(-dt / max(tau, 1e-6))
    np.clip(e, 1e-4, 1.0, out=e)  # keeps the chunked cumprod finite
    return e


def ewma_time(t, x, tau):
    """Irregular-dt EWMA: y_i = e*y_{i-1} + (1-e)*x_i, e = exp(-dt/tau).

    y_0 = x_0. Equivalent per-frame C#: state += (1-exp(-dt/tau))*(x-state).
    """
    x = np.asarray(x, np.float64)
    e = _decays(t, tau)
    a = 1.0 - e
    u = a[:, None] * x if x.ndim == 2 else a * x
    return _scan(e, u, x[0])


def _ffill_idx(mask):
    """For each i: index of the most recent True at or before i (-1 if none)."""
    idx = np.where(mask, np.arange(mask.size), -1)
    return np.maximum.accumulate(idx)


def _hyst_sign(c, dead):
    """Sign of c with a +/-dead hysteresis band: inside the band the last
    committed sign is held (0 until the first excursion)."""
    defined = np.abs(c) > dead
    li = _ffill_idx(defined)
    s = np.sign(c)
    return np.where(li >= 0, s[np.maximum(li, 0)], 0.0)


def _sign_flips(s):
    """True where a committed sign reverses (+1 -> -1 or -1 -> +1)."""
    prev = np.concatenate(([0.0], s[:-1]))
    return (s != 0) & (prev != 0) & (s != prev)


def _unit(v, eps=1e-9):
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, eps)


def _lateral_frame(dir_smooth):
    """Velocity-orthogonal lateral axes from a smoothed direction.

    u = travel direction; e1 = normalize(u x worldUp) (u x worldX when nearly
    vertical); e2 = u x e1. All unit, mutually orthogonal.
    NOTE: the C# client will use local-player LOS instead; this is the causal
    offline equivalent (no local player exists in a replay).
    """
    u = _unit(dir_smooth)
    c1 = np.cross(u, _WORLD_UP)
    n1 = np.linalg.norm(c1, axis=-1, keepdims=True)
    alt = np.cross(u, _WORLD_X)
    c1 = np.where(n1 > 0.2, c1, alt)
    e1 = _unit(c1)
    e2 = np.cross(u, e1)
    return u, e1, e2


def _drag_step(pos, vel, thrust, h, k=DRAG_K):
    """Closed-form ship kinematics under linear drag k and constant thrust:

        p(h) = p0 + v0*(1-e^{-kh})/k + (T/k)*(h - (1-e^{-kh})/k)

    h may be a scalar or per-row array (for hazard-damped horizons).
    """
    h = np.asarray(h, np.float64)
    E = -np.expm1(-k * h)          # 1 - e^{-kh}
    c1 = E / k
    c2 = (h - c1) / k
    if h.ndim:
        c1 = c1[:, None]
        c2 = c2[:, None]
    return pos + vel * c1 + thrust * c2


def _seg_cache(seg):
    if seg.cache is None:
        seg.cache = {}
    return seg.cache


def clear_seg_caches(segs):
    for s in segs:
        s.cache = None


# ---------------------------------------------------------------------------
# ballistic: drag closed form with thrust held for a commit window
# ---------------------------------------------------------------------------

class Ballistic(Predictor):
    """Drag closed-form prediction; thrust estimate T = acc + k*vel decays
    with horizon as exp(-h/t_commit) (players re-decide, don't hold forever).
    """

    name = "ballistic"

    def __init__(self, name=None, tau_v=0.001, tau_a=0.005, t_commit=3.5,
                 t_max=60.0, k=DRAG_K):
        if name:
            self.name = name
        self.tau_v = tau_v
        self.tau_a = tau_a
        self.t_commit = t_commit
        self.t_max = t_max
        self.k = k

    # --- causal per-frame state (horizon-independent, cached per segment) ---
    def _base_state(self, seg):
        key = ("ball", self.tau_v, self.tau_a, self.t_max, self.k)
        cache = _seg_cache(seg)
        st = cache.get(key)
        if st is not None:
            return st
        t = seg.t
        vel = seg.vel.astype(np.float64)
        vel_s = ewma_time(t, vel, self.tau_v)
        dt = np.empty(len(t))
        dt[0] = 1.0
        dt[1:] = np.maximum(np.diff(t), 1e-4)
        acc_raw = np.empty_like(vel_s)
        acc_raw[0] = 0.0
        acc_raw[1:] = np.diff(vel_s, axis=0) / dt[1:, None]
        acc = ewma_time(t, acc_raw, self.tau_a)
        thrust = acc + self.k * vel_s
        tn = np.linalg.norm(thrust, axis=1, keepdims=True)
        scale = np.minimum(1.0, self.t_max / np.maximum(tn, 1e-9))
        thrust = thrust * scale
        st = {"vel_s": vel_s, "acc": acc, "thrust": thrust}
        cache[key] = st
        return st

    def predict(self, seg, idx, h):
        st = self._base_state(seg)
        pos = seg.pos[idx].astype(np.float64)
        vel = seg.vel[idx].astype(np.float64)
        T = st["thrust"][idx] * np.exp(-h / self.t_commit)
        return _drag_step(pos, vel, T, h, self.k)


# ---------------------------------------------------------------------------
# ballistic_hazard: + reversal-hazard horizon damping
# ---------------------------------------------------------------------------

class BallisticHazard(Ballistic):
    """Ballistic plus a reversal hazard built from the decaying Poisson rate
    of lateral-thrust sign flips; the hazard shortens the effective horizon
    (h_eff = h*(1-c_damp*hazard)) and/or the thrust commit window."""

    name = "ballistic_hazard"

    def __init__(self, name=None, tau_v=0.001, tau_a=0.005, t_commit=3.5,
                 t_max=60.0, k=DRAG_K,
                 dead=30.0, tau_flip=5.0, tau_dir=0.8,
                 floor=0.0, opp_dot=-0.3, opp_vmin=2.0, opp_tmin=8.0,
                 commit_dot=0.7, commit_mult=0.1, t_recent=0.2,
                 c_damp=0.15, c_tc=0.0):
        Ballistic.__init__(self, name or self.name, tau_v, tau_a, t_commit,
                           t_max, k)
        self.dead = dead
        self.tau_flip = tau_flip
        self.tau_dir = tau_dir
        self.floor = floor
        self.opp_dot = opp_dot
        self.opp_vmin = opp_vmin
        self.opp_tmin = opp_tmin
        self.commit_dot = commit_dot
        self.commit_mult = commit_mult
        self.t_recent = t_recent
        self.c_damp = c_damp
        self.c_tc = c_tc

    def _hazard_state(self, seg):
        key = ("haz", self.tau_v, self.tau_a, self.t_max, self.k,
               self.dead, self.tau_flip, self.tau_dir,
               self.opp_dot, self.opp_vmin, self.opp_tmin,
               self.commit_dot, self.t_recent)
        cache = _seg_cache(seg)
        st = cache.get(key)
        if st is not None:
            return st
        base = self._base_state(seg)
        t = seg.t
        thrust = base["thrust"]
        vel = seg.vel.astype(np.float64)

        dir_s = ewma_time(t, vel, self.tau_dir)
        u, e1, e2 = _lateral_frame(dir_s)
        c1 = np.einsum("ij,ij->i", thrust, e1)
        c2 = np.einsum("ij,ij->i", thrust, e2)
        f1 = _sign_flips(_hyst_sign(c1, self.dead))
        f2 = _sign_flips(_hyst_sign(c2, self.dead))
        imp = (f1.astype(np.float64) + f2.astype(np.float64)) / self.tau_flip
        rate = _scan(_decays(t, self.tau_flip), imp, 0.0)

        anyflip = f1 | f2
        lf = _ffill_idx(anyflip)
        t_lf = np.where(lf >= 0, t[np.maximum(lf, 0)], -1e9)
        tsf = t - t_lf

        dot_tv = np.einsum("ij,ij->i", thrust, vel)
        nt = np.linalg.norm(thrust, axis=1)
        nv = np.linalg.norm(vel, axis=1)
        opposed = ((dot_tv < self.opp_dot * nt * nv)
                   & (nv > self.opp_vmin) & (nt > self.opp_tmin))
        committed = (dot_tv > self.commit_dot * nt * nv) & (tsf > self.t_recent)

        st = {"rate": rate, "opposed": opposed, "committed": committed,
              "u": u, "e1": e1, "e2": e2}
        cache[key] = st
        return st

    def _hazard(self, seg, idx, h):
        st = self._hazard_state(seg)
        haz = -np.expm1(-st["rate"][idx] * h)
        haz = np.where(st["opposed"][idx], np.maximum(haz, self.floor), haz)
        haz = np.where(st["committed"][idx], haz * self.commit_mult, haz)
        return np.clip(haz, 0.0, 1.0)

    def _coeffs(self, seg, idx, h):
        """(h_eff, thrust_eff) after hazard damping, for reuse by jukephase."""
        base = self._base_state(seg)
        haz = self._hazard(seg, idx, h)
        h_eff = h * (1.0 - self.c_damp * haz)
        tc_eff = np.maximum(self.t_commit * (1.0 - self.c_tc * haz), 1e-3)
        T = base["thrust"][idx] * np.exp(-h / tc_eff)[:, None]
        return h_eff, T

    def predict(self, seg, idx, h):
        pos = seg.pos[idx].astype(np.float64)
        vel = seg.vel[idx].astype(np.float64)
        h_eff, T = self._coeffs(seg, idx, h)
        return _drag_step(pos, vel, T, h_eff, self.k)


# ---------------------------------------------------------------------------
# jukephase: sinusoidal lateral-velocity model when a regular juke is detected
# ---------------------------------------------------------------------------

class JukePhase(BallisticHazard):
    """Detects regular lateral-velocity oscillation (zero-crossing intervals
    with low variance) per lateral axis; when confident, replaces that axis's
    ballistic displacement with an integrated sinusoid. Longitudinal and
    non-confident axes stay ballistic_hazard."""

    name = "jukephase"

    def __init__(self, name=None, dead_l=0.5, tau_cross=1.5, min_count=3.0,
                 cv_max=0.25, amp_min=1.5, half_min=0.15, half_max=0.9,
                 overdue=1.2, a_fit_max=2.0, **kw):
        BallisticHazard.__init__(self, name or self.name, **kw)
        self.dead_l = dead_l
        self.tau_cross = tau_cross
        self.min_count = min_count
        self.cv_max = cv_max
        self.amp_min = amp_min
        self.half_min = half_min
        self.half_max = half_max
        self.overdue = overdue
        self.a_fit_max = a_fit_max

    def _juke_state(self, seg):
        key = ("juke", self.tau_v, self.tau_a, self.t_max, self.k,
               self.tau_dir, self.dead_l, self.tau_cross)
        cache = _seg_cache(seg)
        st = cache.get(key)
        if st is not None:
            return st
        base = self._base_state(seg)
        haz = self._hazard_state(seg)
        t = seg.t
        n = len(t)
        vel_s = base["vel_s"]
        axes = []
        for e_ax in (haz["e1"], haz["e2"]):
            l = np.einsum("ij,ij->i", vel_s, e_ax)
            flips = _sign_flips(_hyst_sign(l, self.dead_l))
            ci = np.flatnonzero(flips)
            # per-crossing decayed stats (incremental, loop over crossings only)
            m1 = np.zeros(ci.size)     # mean half-period
            var = np.zeros(ci.size)
            amp = np.zeros(ci.size)
            cnt = np.zeros(ci.size)    # decayed crossing count
            W = S = S2 = SA = 0.0
            prev_i = 0
            prev_t = t[0]
            first = True
            absl = np.abs(l)
            for j, c in enumerate(ci):
                dt_c = t[c] - prev_t
                if first:
                    # first crossing: no interval yet, just arm the tracker
                    first = False
                else:
                    d = np.exp(-dt_c / self.tau_cross)
                    a_seg = absl[prev_i:c + 1].max()
                    W = W * d + 1.0
                    S = S * d + dt_c
                    S2 = S2 * d + dt_c * dt_c
                    SA = SA * d + a_seg
                prev_i = c
                prev_t = t[c]
                if W > 0:
                    m = S / W
                    m1[j] = m
                    var[j] = max(S2 / W - m * m, 0.0)
                    amp[j] = SA / W
                    cnt[j] = W
            if ci.size == 0:
                z = np.zeros(n)
                axes.append({"conf": np.zeros(n, bool),
                             "omega": np.full(n, np.pi / 1e-6), "amp": z})
                continue
            # broadcast last-crossing state to frames
            lc = _ffill_idx(flips)
            has = lc >= 0
            # map frame -> ordinal of its last crossing
            ordmap = np.full(n, -1, np.int64)
            ordmap[ci] = np.arange(ci.size)
            ordmap = np.maximum.accumulate(ordmap)
            safe = np.maximum(ordmap, 0)
            f_m1 = np.where(has, m1[safe], 0.0)
            f_var = np.where(has, var[safe], 0.0)
            f_amp = np.where(has, amp[safe], 0.0)
            f_cnt = np.where(has, cnt[safe], 0.0)
            f_tlc = np.where(has, t[np.maximum(lc, 0)], -1e9)
            with np.errstate(divide="ignore", invalid="ignore"):
                rel_sd = np.where(f_m1 > 0, np.sqrt(f_var) / f_m1, 1e9)
            conf = ((f_cnt >= self.min_count)
                    & (f_m1 >= self.half_min) & (f_m1 <= self.half_max)
                    & (rel_sd <= self.cv_max)
                    & (f_amp >= self.amp_min)
                    & ((t - f_tlc) <= self.overdue * np.maximum(f_m1, 1e-6)))
            omega = np.pi / np.maximum(f_m1, 1e-6)
            axes.append({"conf": conf, "omega": omega, "amp": f_amp})
        st = {"ax": axes}
        cache[key] = st
        return st

    def predict(self, seg, idx, h):
        base = self._base_state(seg)
        haz = self._hazard_state(seg)
        jk = self._juke_state(seg)
        pos = seg.pos[idx].astype(np.float64)
        vel = seg.vel[idx].astype(np.float64)
        h_eff, T = self._coeffs(seg, idx, h)
        pred = _drag_step(pos, vel, T, h_eff, self.k)

        # drag closed-form coefficients (per-row h_eff) for axis subtraction
        E = -np.expm1(-self.k * h_eff)
        c1 = E / self.k
        c2 = (h_eff - c1) / self.k

        vel_s = base["vel_s"][idx]
        acc = base["acc"][idx]
        for a, e_all in zip(jk["ax"], (haz["e1"], haz["e2"])):
            conf = a["conf"][idx]
            if not conf.any():
                continue
            e_ax = e_all[idx]
            omega = a["omega"][idx]
            amp_est = a["amp"][idx]
            l0 = np.einsum("ij,ij->i", vel_s, e_ax)
            dl0 = np.einsum("ij,ij->i", acc, e_ax)
            phi = np.arctan2(l0 * omega, dl0)
            A = np.sqrt(l0 * l0 + (dl0 / omega) ** 2)
            A = np.minimum(A, self.a_fit_max * amp_est)
            d_sin = (A / omega) * (np.cos(phi) - np.cos(omega * h + phi))
            # ballistic displacement this axis would otherwise contribute
            v_a = np.einsum("ij,ij->i", vel, e_ax)
            t_a = np.einsum("ij,ij->i", T, e_ax)
            d_ball = v_a * c1 + t_a * c2
            corr = np.where(conf, d_sin - d_ball, 0.0)
            pred = pred + corr[:, None] * e_ax
        return pred


# ---------------------------------------------------------------------------
# select: online model selection via a causal prediction ledger
# ---------------------------------------------------------------------------

class Select(Predictor):
    """Runs several models, files every prediction in a ledger, scores each
    prediction when its target time has arrived (target <= now: strictly
    causal), keeps a per-model EWMA of scored residuals, and renders with the
    lowest-residual model (mode='hard') or a residual-weighted blend
    (mode='blend'). Exactly what the C# client will do online."""

    name = "select"

    def __init__(self, name=None, models=None, tau_sel=2.0, mode="hard",
                 blend_p=8.0, blend_eps=0.01, default=1):
        if name:
            self.name = name
        self.models = models or [get("cv"), get("ballistic_hazard"),
                                 get("jukephase")]
        self.tau_sel = tau_sel
        self.mode = mode
        self.blend_p = blend_p
        self.blend_eps = blend_eps
        self.default = default

    def _ledger(self, seg, h):
        key = ("sel", tuple(m.name for m in self.models), self.tau_sel, h)
        cache = _seg_cache(seg)
        st = cache.get(key)
        if st is not None:
            return st
        t = seg.t
        n_valid = int(np.searchsorted(t, t[-1] - h, side="right"))
        all_idx = np.arange(n_valid)
        preds = []
        resid = []
        if n_valid > 0:
            tq = t[:n_valid] + h
            gt = np.stack([np.interp(tq, t, seg.pos[:, k]) for k in range(3)],
                          axis=1)
            for m in self.models:
                p = m.predict(seg, all_idx, h)
                preds.append(p)
                err = np.linalg.norm(p - gt, axis=1)
                # scored in arrival order of the target sample; EWMA over time
                resid.append(ewma_time(t[:n_valid], err, self.tau_sel))
        # ledger lookup: last frame whose target time (t_j + h) <= t_i
        j = np.searchsorted(t, t - h, side="right") - 1
        j = np.minimum(j, n_valid - 1)
        st = {"preds": preds, "resid": resid, "j": j, "n_valid": n_valid}
        cache[key] = st
        return st

    def predict(self, seg, idx, h):
        st = self._ledger(seg, h)
        nm = len(self.models)
        j = st["j"][idx]
        have = j >= 0
        # residual matrix (len(idx), n_models); inf where no score yet
        R = np.full((len(idx), nm), np.inf)
        js = np.maximum(j, 0)
        for m in range(nm):
            if st["n_valid"] > 0:
                R[:, m] = np.where(have, st["resid"][m][js], np.inf)
        P = np.stack([st["preds"][m][idx] if st["n_valid"] > 0 and
                      len(st["preds"][m]) > 0 else
                      self.models[m].predict(seg, idx, h)
                      for m in range(nm)], axis=0)  # (nm, len(idx), 3)
        if self.mode == "blend":
            with np.errstate(divide="ignore"):
                w = 1.0 / np.power(R + self.blend_eps, self.blend_p)
            w[~np.isfinite(w)] = 0.0
            none = w.sum(axis=1) <= 0
            w[none, :] = 0.0
            w[none, self.default] = 1.0
            w = w / w.sum(axis=1, keepdims=True)
            return np.einsum("mif,im->if", P, w)
        choice = np.argmin(R, axis=1)
        choice = np.where(have, choice, self.default)
        return P[choice, np.arange(len(idx))]


_REGISTRY = {}


def register(pred):
    _REGISTRY[pred.name] = pred
    return pred


def get(name):
    return _REGISTRY[name]


register(Hold())
register(ConstantVelocity())
register(MotionArc())
register(Ballistic())
register(BallisticHazard())
register(JukePhase())
register(Select())
register(Select(name="select_blend", mode="blend"))


def default_predictors():
    """Predictors evaluated by bench.py by default, in display order."""
    return [_REGISTRY["hold"], _REGISTRY["cv"], _REGISTRY["cv_arc"]]


def all_predictors():
    return list(_REGISTRY.values())
