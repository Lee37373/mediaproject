using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Session;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>Editor-only shortcut for trying the watchtower lookout (2026-11-02): a tower next to the guild master, paid for.</summary>
    public static class LookoutDebugMenu
    {
        const string Item = "GuildMaster/감시탑 시험: 길드 마스터 옆에 감시탑 세우기 (플레이 중)";

        [MenuItem(Item)]
        static void BuildNextToMaster()
        {
            if (!Application.isPlaying || !Services.TryGet<GuildTreasury>(out var treasury) || !Services.TryGet<Player.PlayerAvatar>(out var master))
            {
                Debug.Log("[Lookout] 플레이 중에 새 게임을 시작한 뒤 사용하세요.");
                return;
            }
            treasury.AddGold(150, "시험");
            treasury.AddStock("stone_shard", 8);
            treasury.AddStock("iron_ore", 4);
            // A few spots round the master: the first free one takes it.
            var offsets = new[] { new Vector2(3f, 0f), new Vector2(-3f, 0f), new Vector2(0f, 3f), new Vector2(0f, -3f), new Vector2(4f, 4f), new Vector2(-4f, -4f) };
            string last = "";
            foreach (var o in offsets)
            {
                var p = master.Position + o;
                last = DebugCommands.BuildTower(p.x, p.y);
                if (last.StartsWith("built")) break;
            }
            Debug.Log("[Lookout] 감시탑 " + last);
        }

        [MenuItem(Item, true)]
        static bool CanBuild() => Application.isPlaying;
    }
}
