using System.Linq;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Places;
using GuildMaster.World;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Editor-only shortcut for trying the ruined watchtower lookout (2026-11-02): the nearest ruin is made known and the guild
    /// master is put down a few steps from it, the camera on it.
    /// </summary>
    public static class LookoutDebugMenu
    {
        const string Item = "GuildMaster/폐허 감시탑 시험: 가장 가까운 폐허 옆으로 길드 마스터 옮기기 (플레이 중)";

        [MenuItem(Item)]
        static void ToNearestRuin()
        {
            if (!Application.isPlaying || !Services.TryGet<PlaceService>(out var places) || !Services.TryGet<Player.PlayerAvatar>(out var master)
                || !Services.TryGet<WorldService>(out var world))
            {
                Debug.Log("[Lookout] 플레이 중에 새 게임을 시작한 뒤 사용하세요.");
                return;
            }
            var town = world.TownCenterWorld;
            var ruin = places.All.Where(p => p.Kind == PlaceKind.WatchRuin).OrderBy(p => Vector2.Distance(p.WorldPosition, town)).FirstOrDefault();
            if (ruin == null)
            {
                Debug.Log("[Lookout] 이 지도에는 폐허 감시탑이 없습니다.");
                return;
            }
            if (!places.IsKnown(ruin) && Services.TryGet<FogOfWar>(out var fog)) fog.Reveal(ruin.WorldPosition, 3f);
            var at = ruin.WorldPosition + new Vector2(3f, 0f);
            master.Mover.Stop();
            master.transform.position = new Vector3(at.x, at.y, master.transform.position.z);
            if (Services.TryGet<CameraController>(out var cam)) cam.JumpTo(at);
            Debug.Log($"[Lookout] 폐허 감시탑 #{ruin.Index} ({Vector2.Distance(ruin.WorldPosition, town):0}칸) 옆으로 옮겼습니다. 감시탑을 클릭하세요.");
        }

        [MenuItem(Item, true)]
        static bool CanUse() => Application.isPlaying;
    }
}
