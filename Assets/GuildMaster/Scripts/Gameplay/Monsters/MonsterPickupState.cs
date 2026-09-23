using GuildMaster.AI;
using GuildMaster.Core;
using GuildMaster.Items;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// Walks to a reserved ground item and wears or swallows it (P4-21). Gives up when the item is
    /// gone, no longer wanted, unreachable or after a timeout; fights and fleeing interrupt it as usual
    /// (Sense and OnDamaged switch state), which releases the reservation.
    /// </summary>
    public class MonsterPickupState : State<Monster>
    {
        const float Timeout = 10f;
        const float ReachDistance = 0.55f;

        GroundItem _item;

        public override string Label => "줍는 중";

        public void Begin(GroundItem item) => _item = item;

        public override void Enter()
        {
            var m = Owner;
            m.Engage.Clear();
            if (_item == null || !m.Mover.SetDestination(_item.Position, 0.2f)) m.GoToDefaultState();
        }

        public override void Tick(float dt)
        {
            var m = Owner;
            if (!Services.TryGet<GroundItemService>(out var ground) || !ground.Contains(_item) || !m.WantsItem(_item.Item) || TimeInState > Timeout)
            {
                m.GoToDefaultState();
                return;
            }
            if (Vector2.Distance(m.Position, _item.Position) <= ReachDistance)
            {
                var item = _item.Item;
                bool absorb = !item.IsUnique && m.Def.pickupMode == Data.MonsterPickupMode.Absorb;
                ground.Take(_item, m, absorb);
                _item = null;
                m.UseItem(item);
                m.LookForMoreSpoilsNow();
                m.GoToDefaultState();
                return;
            }
            if (!m.Mover.IsMoving) m.Mover.SetDestination(_item.Position, 0.2f);
        }

        public override void Exit()
        {
            if (_item != null) _item.Release(Owner);
            _item = null;
        }
    }
}
