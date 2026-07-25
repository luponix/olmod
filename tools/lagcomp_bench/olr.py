"""Parser for Overload olmod .olr replay files (version 8).

The .olr format is written by .NET BinaryWriter (little-endian). Strings are
7-bit-varint length-prefixed UTF-8. Half floats are IEEE 754 binary16.

Public API:
    load(path, read_events=False, read_chat=False) -> Replay
    read_info(path) -> Header            (header + roster only, cheap)

If numpy is installed (strongly recommended), per-slot frame data is returned
as numpy arrays; otherwise a pure-stdlib fallback returns Python lists with
the same attribute names.
"""

import struct

try:
    import numpy as np
except ImportError:  # pragma: no cover - exercised only on numpy-less installs
    np = None

MAGIC = 0x314C4F52  # "ROL1" little-endian == b"OLR1"
VERSION = 8

# Fixed-size portion of a present ship state (everything after the flags byte).
_SHIP_FMT = "<3f4f3B3e3eBHBBQ"
_SHIP_SIZE = struct.calcsize(_SHIP_FMT)
assert _SHIP_SIZE == 56

if np is not None:
    SHIP_DTYPE = np.dtype(
        [
            ("pos", "<f4", (3,)),
            ("rot", "<f4", (4,)),
            ("weapon", "u1"),
            ("missile", "u1"),
            ("health", "u1"),
            ("vel", "<f2", (3,)),
            ("vrot", "<f2", (3,)),
            ("energy", "u1"),
            ("ammo", "<u2"),
            ("weaponsOwned", "u1"),
            ("missilesOwned", "u1"),
            ("missileAmmo", "<u8"),
        ]
    )
    assert SHIP_DTYPE.itemsize == _SHIP_SIZE


class OlrError(Exception):
    """Malformed .olr file."""


class UnsupportedVersion(OlrError):
    """File has a valid magic but a version other than 8."""

    def __init__(self, path, version):
        super().__init__("%s: unsupported .olr version %d (expected %d)" % (path, version, VERSION))
        self.path = path
        self.version = version


class PlayerInfo(object):
    __slots__ = ("name", "team", "color")

    def __init__(self, name, team, color):
        self.name = name
        self.team = team
        self.color = color  # (r, g, b) floats

    def __repr__(self):
        return "PlayerInfo(%r, team=%d)" % (self.name, self.team)


class SlotData(object):
    """Per-roster-slot frame-aligned arrays (length == frame count).

    pos (F,3) f32, vel (F,3) f32, rot (F,4) f32 quaternion xyzw,
    vrot (F,3) f32 rad/s, health (F,) u8, present/alive (F,) bool.
    Absent frames hold NaN (float arrays) / 0 (health).
    """

    __slots__ = ("pos", "vel", "rot", "vrot", "health", "present", "alive")

    def __init__(self, pos, vel, rot, vrot, health, present, alive):
        self.pos = pos
        self.vel = vel
        self.rot = rot
        self.vrot = vrot
        self.health = health
        self.present = present
        self.alive = alive


class Event(object):
    __slots__ = ("t", "type", "iA", "iB", "iC", "fA", "pos", "rot", "nameA", "nameB")

    def __init__(self, t, type_, iA, iB, iC, fA, pos, rot, nameA, nameB):
        self.t = t
        self.type = type_
        self.iA = iA
        self.iB = iB
        self.iC = iC
        self.fA = fA
        self.pos = pos
        self.rot = rot
        self.nameA = nameA
        self.nameB = nameB


class ChatLine(object):
    __slots__ = ("t", "sender", "team", "kind", "senderName", "message")

    def __init__(self, t, sender, team, kind, senderName, message):
        self.t = t
        self.sender = sender
        self.team = team
        self.kind = kind
        self.senderName = senderName
        self.message = message


class Header(object):
    __slots__ = (
        "path",
        "version",
        "sceneName",
        "levelIdHash",
        "matchMode",
        "tickRateHz",
        "storedDuration",
        "maxHitpoints",
        "matchTimeLimit",
        "matchStartOffset",
        "players",
    )


class Replay(Header):
    __slots__ = ("t", "frameCount", "slots", "events", "chat")


class _Reader(object):
    """Cursor over an in-memory .olr blob."""

    def __init__(self, data, path):
        self.d = data
        self.off = 0
        self.path = path

    def _need(self, n):
        if self.off + n > len(self.d):
            raise OlrError("%s: truncated file (need %d bytes at offset %d, have %d)"
                           % (self.path, n, self.off, len(self.d) - self.off))

    def u8(self):
        self._need(1)
        v = self.d[self.off]
        self.off += 1
        return v

    def u16(self):
        self._need(2)
        v = self.d[self.off] | (self.d[self.off + 1] << 8)
        self.off += 2
        return v

    def i32(self):
        self._need(4)
        v = struct.unpack_from("<i", self.d, self.off)[0]
        self.off += 4
        return v

    def u32(self):
        self._need(4)
        v = struct.unpack_from("<I", self.d, self.off)[0]
        self.off += 4
        return v

    def f32(self):
        self._need(4)
        v = struct.unpack_from("<f", self.d, self.off)[0]
        self.off += 4
        return v

    def string(self):
        # .NET BinaryWriter: 7-bit varint byte length, then UTF-8 bytes.
        n = 0
        shift = 0
        while True:
            b = self.u8()
            n |= (b & 0x7F) << shift
            shift += 7
            if not (b & 0x80):
                break
            if shift > 35:
                raise OlrError("%s: bad varint string length" % self.path)
        self._need(n)
        v = self.d[self.off:self.off + n].decode("utf-8", errors="replace")
        self.off += n
        return v


def _read_header(r, out):
    magic = r.u32()
    if magic != MAGIC:
        raise OlrError("%s: bad magic 0x%08X" % (r.path, magic))
    out.version = r.u8()
    if out.version != VERSION:
        raise UnsupportedVersion(r.path, out.version)
    out.sceneName = r.string()
    out.levelIdHash = r.string()
    out.matchMode = r.i32()
    out.tickRateHz = r.i32()
    out.storedDuration = r.f32()
    out.maxHitpoints = r.f32()
    out.matchTimeLimit = r.f32()
    out.matchStartOffset = r.f32()
    n = r.i32()
    if n < 0 or n > 64:
        raise OlrError("%s: implausible player count %d" % (r.path, n))
    out.players = []
    for _ in range(n):
        name = r.string()
        team = r.i32()
        color = (r.f32(), r.f32(), r.f32())
        out.players.append(PlayerInfo(name, team, color))


def read_info(path):
    """Read header + roster only (cheap; reads the first few KB)."""
    with open(path, "rb") as f:
        data = f.read(65536)
    h = Header()
    h.path = path
    _read_header(_Reader(data, path), h)
    return h


def _parse_frames_numpy(r, rep):
    d = r.d
    off = r.off
    F = rep.frameCount
    P = len(rep.players)
    t = np.empty(F, np.float32)
    flags = np.zeros((F, P), np.uint8)
    bufs = [bytearray() for _ in range(P)]
    fidx = [[] for _ in range(P)]
    unpack_f = struct.unpack_from
    size = _SHIP_SIZE
    end = len(d)
    for fi in range(F):
        if off + 4 > end:
            raise OlrError("%s: truncated at frame %d" % (r.path, fi))
        t[fi] = unpack_f("<f", d, off)[0]
        off += 4
        for s in range(P):
            fl = d[off]
            off += 1
            if fl & 1:
                flags[fi, s] = fl
                bufs[s] += d[off:off + size]
                fidx[s].append(fi)
                off += size
        # drifting-item block: ushort count + count * (ushort id + 3*f32 pos)
        ic = d[off] | (d[off + 1] << 8)
        off += 2 + ic * 14
        if off > end:
            raise OlrError("%s: truncated in item block of frame %d" % (r.path, fi))
    r.off = off

    rep.t = t
    rep.slots = []
    present_all = (flags & 1) != 0
    alive_all = (flags & 2) != 0
    for s in range(P):
        rec = np.frombuffer(bytes(bufs[s]), dtype=SHIP_DTYPE)
        idx = np.asarray(fidx[s], dtype=np.int64)
        pos = np.full((F, 3), np.nan, np.float32)
        vel = np.full((F, 3), np.nan, np.float32)
        rot = np.full((F, 4), np.nan, np.float32)
        vrot = np.full((F, 3), np.nan, np.float32)
        health = np.zeros(F, np.uint8)
        if idx.size:
            pos[idx] = rec["pos"]
            vel[idx] = rec["vel"].astype(np.float32)
            rot[idx] = rec["rot"]
            vrot[idx] = rec["vrot"].astype(np.float32)
            health[idx] = rec["health"]
        rep.slots.append(SlotData(pos, vel, rot, vrot, health,
                                  present_all[:, s].copy(), alive_all[:, s].copy()))


def _parse_frames_stdlib(r, rep):  # pragma: no cover - numpy-less fallback
    P = len(rep.players)
    F = rep.frameCount
    t = []
    slots = [SlotData([], [], [], [], [], [], []) for _ in range(P)]
    unpack = struct.unpack_from
    for fi in range(F):
        t.append(r.f32())
        for s in range(P):
            sd = slots[s]
            fl = r.u8()
            present = bool(fl & 1)
            sd.present.append(present)
            sd.alive.append(bool(fl & 2))
            if present:
                r._need(_SHIP_SIZE)
                v = unpack(_SHIP_FMT, r.d, r.off)
                r.off += _SHIP_SIZE
                sd.pos.append(v[0:3])
                sd.rot.append(v[3:7])
                sd.health.append(v[9])
                sd.vel.append(v[10:13])
                sd.vrot.append(v[13:16])
            else:
                sd.pos.append(None)
                sd.rot.append(None)
                sd.health.append(0)
                sd.vel.append(None)
                sd.vrot.append(None)
        ic = r.u16()
        r._need(ic * 14)
        r.off += ic * 14
    rep.t = t
    rep.slots = slots


def _read_events(r, count):
    events = []
    for _ in range(count):
        t = r.f32()
        typ = r.u8()
        iA = r.i32()
        iB = r.i32()
        iC = r.i32()
        fA = r.f32()
        pos = (r.f32(), r.f32(), r.f32())
        rot = (r.f32(), r.f32(), r.f32(), r.f32())
        nameA = r.string()
        nameB = r.string()
        events.append(Event(t, typ, iA, iB, iC, fA, pos, rot, nameA, nameB))
    return events


def _read_chat(r, count):
    chat = []
    for _ in range(count):
        t = r.f32()
        sender = r.i32()
        team = r.i32()
        kind = r.u8()
        senderName = r.string()
        message = r.string()
        chat.append(ChatLine(t, sender, team, kind, senderName, message))
    return chat


def load(path, read_events=False, read_chat=False):
    """Parse a v8 .olr file.

    Raises UnsupportedVersion for valid-magic files of another version and
    OlrError for corrupt files.
    """
    with open(path, "rb") as f:
        data = f.read()
    r = _Reader(data, path)
    rep = Replay()
    rep.path = path
    _read_header(r, rep)

    rep.frameCount = r.i32()
    if rep.frameCount < 0:
        raise OlrError("%s: negative frame count" % path)
    if np is not None:
        _parse_frames_numpy(r, rep)
    else:
        _parse_frames_stdlib(r, rep)

    rep.events = None
    rep.chat = None
    if read_events or read_chat:
        ec = r.i32()
        events = _read_events(r, ec)
        if read_events:
            rep.events = events
        if read_chat:
            cc = r.i32()
            rep.chat = _read_chat(r, cc)
    return rep
