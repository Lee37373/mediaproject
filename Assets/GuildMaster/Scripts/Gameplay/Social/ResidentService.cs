using System.Collections.Generic;
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
    /// </summary>
    public class ResidentService : MonoBehaviour
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
    }
}
