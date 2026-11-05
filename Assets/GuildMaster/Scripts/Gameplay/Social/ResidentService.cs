using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Town;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Social
{
    /// <summary>
    /// The townsfolk (user request 2026-10-07): six residents with jobs and personalities who live in town from the
    /// start (<see cref="ResidentProfile.Defaults"/>). Their talks run through <see cref="TownTalkService"/>.
    /// Saved as "residents" (P4-119): what each thinks of the guild and of the people they have talked to — not where
    /// they stand (they go back to their day from their workplace).
    /// </summary>
    public class ResidentService : MonoBehaviour, ISaveable
    {
        readonly List<Resident> _residents = new();

        public IReadOnlyList<Resident> Residents => _residents;

        public void Init(TownService town, WorldService world)
        {
            foreach (var p in ResidentProfile.Defaults())
            {
                var work = Workplace(p.Job, town, world);
                var r = Resident.Create(p, work + GameRandom.Shared.InsideUnitCircle() * 0.8f, work, transform);
                _residents.Add(r);
            }
        }

        public Resident Find(string id) => _residents.Find(r => r != null && r.Profile.Id == id);

        static Vector2 Workplace(ResidentJob job, TownService town, WorldService world)
        {
            BuildingType? at = job switch
            {
                ResidentJob.Blacksmith => BuildingType.Blacksmith,
                ResidentJob.Herbalist => BuildingType.Apothecary,
                ResidentJob.Chief => BuildingType.TownHall,
                ResidentJob.Guard => BuildingType.TrainingGround,
                _ => null,
            };
            var b = at.HasValue ? town.Get(at.Value) : null;
            return b != null ? b.DoorPosition : town.RandomPlazaSpot(GameRandom.Shared, 4);
        }

        // ---------------- Save (P4-119) ----------------

        [Serializable]
        class FeelingData { public string id; public int value; }

        [Serializable]
        class ResidentData
        {
            public string id;
            public int guild_opinion;
            public List<FeelingData> feelings = new();
        }

        [Serializable]
        class SaveData { public List<ResidentData> residents = new(); }

        public string SaveKey => "residents";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var r in _residents)
            {
                if (r == null || r.Profile == null) continue;
                var d = new ResidentData { id = r.Profile.Id, guild_opinion = r.Profile.GuildOpinion };
                foreach (var kv in r.Profile.Feelings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    d.feelings.Add(new FeelingData { id = kv.Key, value = kv.Value });
                s.residents.Add(d);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            if (s.residents == null) return;
            foreach (var d in s.residents)
            {
                var r = d != null ? Find(d.id) : null;
                if (r == null) { ctx.Warnings.Add("unknown resident " + d?.id); continue; }
                r.Profile.GuildOpinion = Math.Clamp(d.guild_opinion, ResidentProfile.Min, ResidentProfile.Max);
                r.Profile.RestoreFeelings(d.feelings?.Select(f => new KeyValuePair<string, int>(f.id, f.value)));
            }
        }
    }
}
