using System.IO;
using Overload;
using UnityEngine;

namespace GameMod.Replay
{
    // Reads the .olr versions the luponix+ builds wrote (v8 to v10) straight into the current
    // model, so those recordings play without a conversion step. Read-only: nothing writes
    // these layouts any more, and a recording saved again is written as the current version.
    public static class LegacyOlrFile
    {
        public static bool CanRead(int version)
        {
            return version >= 8 && version <= 10;
        }

        internal static Recording ReadHeader(BinaryReader reader, int version, out float storedDuration)
        {
            var replay = new Recording();
            replay.sceneName = OlrFile.ReadString(reader);
            replay.levelIdHash = OlrFile.ReadString(reader);
            replay.matchMode = reader.ReadInt32();
            reader.ReadInt32(); // tickRateHz, dropped in v12: the snapshot stream is always 60 Hz
            storedDuration = reader.ReadSingle();
            replay.maxHitpoints = reader.ReadSingle();
            replay.matchTimeLimit = reader.ReadSingle();
            replay.matchStartOffset = reader.ReadSingle();
            if (version >= 10)
            {
                replay.recorderSlot = reader.ReadInt32();
                replay.recordedUtcBinary = reader.ReadInt64();
            }
            else if (replay.matchTimeLimit > 0f &&
                     replay.matchStartOffset + storedDuration > replay.matchTimeLimit + 60f) // 60 s of overtime
            {
                replay.matchStartOffset = 0f; // before v10 this could hold the server's uptime
            }
            OlrFile.ReadRoster(reader, replay);
            return replay;
        }

        internal static Recording ReadBody(BinaryReader reader, int version)
        {
            float storedDuration;
            var replay = ReadHeader(reader, version, out storedDuration);
            int playerCount = replay.players.Count;

            int frameCount = OlrFile.ReadCount(reader, OlrFile.MAX_FRAMES, "frame");
            for (int i = 0; i < frameCount; i++)
            {
                var frame = new Frame();
                frame.t = reader.ReadSingle();
                frame.ships = new ShipState[playerCount];
                for (int slot = 0; slot < playerCount; slot++)
                    frame.ships[slot] = version == 8 ? ReadV8ShipState(reader) : OlrFile.ReadShipState(reader);
                frame.items = OlrFile.ReadItemSamples(reader);
                if (version >= 9) // v9 added the objective and robot blocks
                {
                    frame.objective = OlrFile.ReadObjectiveState(reader);
                    frame.robots = OlrFile.ReadRobotSamples(reader);
                }
                replay.frames.Add(frame);
            }

            int eventCount = OlrFile.ReadCount(reader, OlrFile.MAX_EVENTS, "event");
            for (int i = 0; i < eventCount; i++)
            {
                var ev = OlrFile.ReadEvent(reader, playerCount);
                ev.type = CurrentEventType(ev.type);
                replay.events.Add(ev);
            }
            if (version == 8)
                RestoreKillerTeams(replay);

            int chatCount = OlrFile.ReadCount(reader, OlrFile.MAX_CHAT_LINES, "chat");
            for (int i = 0; i < chatCount; i++)
            {
                var line = new ChatLine();
                line.t = reader.ReadSingle();
                line.sender = reader.ReadInt32();
                line.team = reader.ReadInt32();
                reader.ReadByte(); // typed or quick chat, dropped in v12: nothing read it back
                line.senderName = OlrFile.ReadString(reader);
                line.message = OlrFile.ReadString(reader);
                replay.chat.Add(line);
            }
            return replay;
        }

        // v8 stored the rotation as four floats and only which weapons were owned, not their
        // level, and had no status flags (cloak, overdrive, invulnerable)
        private static ShipState ReadV8ShipState(BinaryReader reader)
        {
            byte flags = reader.ReadByte();
            var ship = new ShipState();
            ship.present = (flags & OlrFile.PRESENT_FLAG) != 0;
            ship.alive = (flags & OlrFile.ALIVE_FLAG) != 0;
            if (!ship.present)
                return ship;
            ship.pos = OlrFile.ReadVector(reader);
            ship.rot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(),
                                      reader.ReadSingle(), reader.ReadSingle());
            ship.weapon = reader.ReadByte();
            ship.missile = reader.ReadByte();
            OlrFile.CheckSelection(ship);
            ship.health = reader.ReadByte();
            ship.vel = OlrFile.ReadHalfVector(reader);
            ship.vrot = OlrFile.ReadHalfVector(reader);
            ship.energy = reader.ReadByte();
            ship.ammo = reader.ReadUInt16();
            ship.weaponLevels = LevelsForOwnedMask(reader.ReadByte());
            ship.missileLevels = LevelsForOwnedMask(reader.ReadByte());
            ship.missileAmmo = reader.ReadUInt64();
            return ship;
        }

        // LEVEL_1 for every owned slot: what a multiplayer spawn hands out, so the weapon
        // names, ammo caps and reticles match the recorded match
        private static uint LevelsForOwnedMask(byte ownedMask)
        {
            uint levels = 0;
            for (int slot = 0; slot < 8; slot++)
                if ((ownedMask & (1 << slot)) != 0)
                    levels = ShipState.PackWeaponLevel(levels, slot, WeaponUnlock.LEVEL_1);
            return levels;
        }

        // v9 and v10 numbered the robot events 18 to 20, and 18 is MassdriverShot since v13
        private static MatchEventType CurrentEventType(MatchEventType legacyType)
        {
            switch ((int)legacyType)
            {
                case 18: return MatchEventType.RobotSpawn;
                case 19: return MatchEventType.RobotDeath;
                case 20: return MatchEventType.RobotFire;
                default: return legacyType;
            }
        }

        // v8 left Kill.iC, the killer's team at the kill, at zero, which reads as TEAM0 and
        // credits every kill of a team match to one team. The roster holds each player's team
        // at join, so following the TeamChange events forward recovers it.
        private static void RestoreKillerTeams(Recording replay)
        {
            var teams = new int[replay.players.Count];
            for (int i = 0; i < teams.Length; i++)
                teams[i] = replay.players[i].team;
            foreach (var ev in replay.events)
            {
                bool knownPlayer = ev.iA >= 0 && ev.iA < teams.Length;
                if (ev.type == MatchEventType.TeamChange && knownPlayer)
                    teams[ev.iA] = ev.iB;
                else if (ev.type == MatchEventType.Kill)
                    ev.iC = knownPlayer ? teams[ev.iA] : (int)MpTeam.ANARCHY;
            }
        }
    }
}
