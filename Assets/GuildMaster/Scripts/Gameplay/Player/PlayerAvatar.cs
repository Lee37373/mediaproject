using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Player
{
    public struct PlayerMoveOrderedEvent { public Vector2 Target; }

    /// <summary>
    /// The guild master's non-combat avatar: click-to-move, provides vision,
    /// and walks up to NPCs to interact (e.g. recruitment talks).
    /// </summary>
    [RequireComponent(typeof(PathFollower))]
    public class PlayerAvatar : MonoBehaviour
    {
        PathFollower _mover;
        VisionSource _vision;
        System.Action _onArrive;

        public PathFollower Mover => _mover;

        public static PlayerAvatar Create(Vector2 position, float speed, float sight)
        {
            var go = new GameObject("GuildMaster(Player)");
            go.transform.position = position;
            var mover = go.AddComponent<PathFollower>();
            mover.Speed = speed;
            var visual = go.AddComponent<CharacterVisual>();
            visual.Init(PixelArt.GuildMaster());
            var vision = go.AddComponent<VisionSource>();
            vision.SightRadius = sight;
            var avatar = go.AddComponent<PlayerAvatar>();
            return avatar;
        }

        void Awake()
        {
            _mover = GetComponent<PathFollower>();
            _vision = GetComponent<VisionSource>();
            _mover.Arrived += HandleArrived;
            _mover.PathFailed += () => _onArrive = null;
        }

        public bool MoveTo(Vector2 target, System.Action onArrive = null, float stopDistance = 0.05f)
        {
            _onArrive = onArrive;
            bool ok = _mover.SetDestination(target, stopDistance);
            if (ok) EventBus.Publish(new PlayerMoveOrderedEvent { Target = target });
            return ok;
        }

        void HandleArrived()
        {
            var cb = _onArrive;
            _onArrive = null;
            cb?.Invoke();
        }
    }
}
