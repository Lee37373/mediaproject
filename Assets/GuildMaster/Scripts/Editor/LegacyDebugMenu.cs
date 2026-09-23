using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Legacy;
using GuildMaster.Session;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>Play-mode helpers to try the F-6 death and last-stand flow without waiting for a fight.</summary>
    public static class LegacyDebugMenu
    {
        const string Root = "GuildMaster/Legacy/";
        const string Check = "GuildMaster/Legacy/검증/";

        static bool Ready => Application.isPlaying && Services.TryGet<GuildLegacy>(out _) && Services.TryGet<GuildRoster>(out var r) && r.Members.Count > 0;

        [MenuItem(Root + "길드원 0번 사망 (유품 지급 후)")]
        static void KillFirst() => Kill(false);

        [MenuItem(Root + "길드원 0번을 마을 밖으로 옮겨 사망 (유품 회수 시험)")]
        static void KillOutside() => Kill(true);

        static void Kill(bool outsideTown)
        {
            var legacy = Services.Get<GuildLegacy>();
            var a = Services.Get<GuildRoster>().Members[0];
            if (outsideTown && !MoveOutsideTown(a)) return;
            // Something to leave behind: gear and a couple of potions.
            if (a.Data.gearTier < 1) a.Data.gearTier = 1;
            a.Data.AddLoot(DefaultContent.Potion, 2);
            var before = Snapshot();
            legacy.ForcedRoll = 1f;
            Debug.Log("[Legacy] " + DebugCommands.Hurt(0, 99999f));
            legacy.ForcedRoll = null;
            Report(legacy);
            Debug.Log("[Legacy] 사망 직후 스트레스 변화\n" + Diff(before));
        }

        /// <summary>Puts the member on a walkable spot a few cells beyond the town's east wall (same idea as the skirmish debug command).</summary>
        static bool MoveOutsideTown(Adventurer a)
        {
            var world = Services.Get<World.WorldService>();
            var town = world.Map.TownRect;
            var cell = World.GridMap.WorldToCell(world.TownCenterWorld + Vector2.right * (town.width * 0.5f + 10f));
            if (!world.Grid.TryFindNearestWalkable(cell, 8, out cell))
            {
                Debug.LogWarning("[Legacy] 마을 밖에 걸을 수 있는 자리를 찾지 못했습니다.");
                return false;
            }
            var p = World.GridMap.CellToWorld(cell);
            a.Mover.Stop();
            a.transform.position = new Vector3(p.x, p.y, a.transform.position.z);
            return true;
        }

        [MenuItem(Root + "길드원 0번 치명타 (구사일생 확정)")]
        static void LastStand()
        {
            var legacy = Services.Get<GuildLegacy>();
            var a = Services.Get<GuildRoster>().Members[0];
            a.Data.woundedUntilHour = 0f;
            a.Data.lastStandHour = -999f;
            legacy.ForcedRoll = 0f;
            Debug.Log("[Legacy] " + DebugCommands.Hurt(0, 99999f));
            legacy.ForcedRoll = null;
            Debug.Log($"[Legacy] {a.Data.name} wounded={legacy.IsWounded(a)} hoursLeft={legacy.WoundedHoursLeft(a):0.0} hp={a.Hp:0}");
        }

        [MenuItem(Root + "쓰러진 길드원 기록 보기")]
        static void ShowRecords() => Report(Services.Get<GuildLegacy>());

        [MenuItem(Root + "길드원 0번 사망 (유품 지급 후)", true)]
        static bool ValidateKill() => Ready;

        [MenuItem(Root + "길드원 0번을 마을 밖으로 옮겨 사망 (유품 회수 시험)", true)]
        static bool ValidateKillOutside() => Ready;

        [MenuItem(Root + "길드원 0번 치명타 (구사일생 확정)", true)]
        static bool ValidateLastStand() => Ready;

        [MenuItem(Root + "쓰러진 길드원 기록 보기", true)]
        static bool ValidateRecords() => Ready;

        static void Report(GuildLegacy legacy)
        {
            var now = Services.Get<GameClock>().TotalHours;
            if (legacy.Fallen.Count == 0) { Debug.Log("[Legacy] 기록 없음"); return; }
            foreach (var r in legacy.Fallen)
                Debug.Log($"[Legacy] #{r.Id} {r.Name} Lv{r.Level} {r.Day}일차 killer={r.KillerName ?? "-"} friends={r.FriendIds.Count} memorial={r.MemorialHeld} | {LegacyTexts.RelicLine(r, now)}");
        }

        // ---------------- Verification helpers (play-verification of the F-6 numbers) ----------------

        [MenuItem(Check + "1. 호감도 설정 (1→0 -30, 2→0 +60, 2→1 0 · 스트레스 10)")]
        static void SetupRelations()
        {
            var m = Services.Get<GuildRoster>().Members;
            if (m.Count < 3) { Debug.LogWarning("[Legacy] 길드원이 3명 필요합니다."); return; }
            SetRelation(m[1], m[0], -30);
            SetRelation(m[2], m[0], 60);
            SetRelation(m[2], m[1], 0);
            foreach (var a in m) a.Data.stress = 10f;
            LogState("호감도 설정 후");
        }

        [MenuItem(Check + "2. 길드원 스트레스·호감도·상태 로그")]
        static void LogNow() => LogState("현황");

        [MenuItem(Check + "3. 마지막 기록에 추모식 열기 (전후 스트레스)")]
        static void Memorial()
        {
            var legacy = Services.Get<GuildLegacy>();
            if (legacy.Fallen.Count == 0) { Debug.Log("[Legacy] 기록 없음"); return; }
            var rec = legacy.Fallen[legacy.Fallen.Count - 1];
            var before = Snapshot();
            var result = legacy.HoldMemorial(rec);
            Debug.Log($"[Legacy] 추모식 → {rec.Name} ok={result.Ok} : {result.Message}\n" + Diff(before));
        }

        [MenuItem(Check + "4. 시계 +49시간 (유품 부패 시험)")]
        static void Plus49() => AdvanceHours(49f);

        [MenuItem(Check + "5. 마을 동쪽 밖에 슬라임 3마리 (길드 미소환)")]
        static void SlimesOutside() => Debug.Log("[Legacy] " + DebugCommands.SpawnSkirmish("slime", 3, false));

        [MenuItem(Check + "6. 실전 구사일생: 전원 HP 5, 확정 켜고 고블린 3마리와 전투")]
        static void RealLastStand()
        {
            var legacy = Services.Get<GuildLegacy>();
            var members = Services.Get<GuildRoster>().Members.ToList();
            legacy.ForcedRoll = 0f;
            for (int i = 0; i < members.Count; i++)
            {
                var a = members[i];
                a.Data.woundedUntilHour = 0f;
                a.Data.lastStandHour = -999f;
                float cut = a.Hp - 5f;
                if (cut > 0f) DebugCommands.Hurt(i, cut);
            }
            Debug.Log("[Legacy] " + DebugCommands.SpawnSkirmish("goblin", 3, true));
            LogState("전투 시작 (구사일생 확정 켜짐)");
        }

        [MenuItem(Check + "7. 구사일생 확정 끄기")]
        static void ForceOff()
        {
            Services.Get<GuildLegacy>().ForcedRoll = null;
            Debug.Log("[Legacy] ForcedRoll 해제");
        }

        [MenuItem(Check + "8. 슬라임 사냥 의뢰 게시 (30G, 파티 1)")]
        static void PostHunt() => Debug.Log("[Legacy] " + DebugCommands.PostHunt("slime", 2, 30, 10, 1));

        [MenuItem(Check + "9. 가짜 기록 29건 추가 (30건 한도 시험)")]
        static void FillRecords()
        {
            var list = (List<FallenRecord>)Services.Get<GuildLegacy>().Fallen;
            for (int i = 0; i < 29; i++)
                list.Add(new FallenRecord { Id = 1000 + i, Name = $"더미{i}", ClassLabel = "전사", Values = "-", Level = 1, Day = 1, DiedAtHour = 0f });
            Debug.Log($"[Legacy] 기록 {list.Count}건");
        }

        [MenuItem(Check + "10. 기록 개수·맨 앞 이름 로그")]
        static void CountRecords()
        {
            var f = Services.Get<GuildLegacy>().Fallen;
            Debug.Log($"[Legacy] 기록 {f.Count}건, 가장 오래된 기록={(f.Count > 0 ? f[0].Name : "-")}, 최신={(f.Count > 0 ? f[f.Count - 1].Name : "-")}");
        }

        [MenuItem(Check + "11. 각 길드원 의뢰·상태 로그")]
        static void LogQuests()
        {
            var legacy = Services.Get<GuildLegacy>();
            var sb = new StringBuilder("[Legacy] 의뢰 현황\n");
            foreach (var a in Services.Get<GuildRoster>().Members)
                sb.AppendLine($"  {a.Data.name} quest={(a.CurrentQuest != null ? a.CurrentQuest.Title : "-")} state={a.StateLabel} wounded={legacy.IsWounded(a)} hp={a.Hp:0}");
            Debug.Log(sb.ToString());
        }

        [MenuItem(Check + "12. 전원 스트레스 55로 올리기")]
        static void StressUp()
        {
            foreach (var a in Services.Get<GuildRoster>().Members) a.Data.stress = 55f;
            LogState("스트레스 55 설정");
        }

        [MenuItem(Check + "12. 전원 스트레스 55로 올리기", true)]
        static bool V12() => Ready;

        [MenuItem(Check + "1. 호감도 설정 (1→0 -30, 2→0 +60, 2→1 0 · 스트레스 10)", true)]
        static bool V1() => Ready;

        [MenuItem(Check + "2. 길드원 스트레스·호감도·상태 로그", true)]
        static bool V2() => Ready;

        [MenuItem(Check + "3. 마지막 기록에 추모식 열기 (전후 스트레스)", true)]
        static bool V3() => Ready;

        [MenuItem(Check + "4. 시계 +49시간 (유품 부패 시험)", true)]
        static bool V4() => Ready;

        [MenuItem(Check + "5. 마을 동쪽 밖에 슬라임 3마리 (길드 미소환)", true)]
        static bool V5() => Ready;

        [MenuItem(Check + "6. 실전 구사일생: 전원 HP 5, 확정 켜고 고블린 3마리와 전투", true)]
        static bool V6() => Ready;

        [MenuItem(Check + "7. 구사일생 확정 끄기", true)]
        static bool V7() => Ready;

        [MenuItem(Check + "8. 슬라임 사냥 의뢰 게시 (30G, 파티 1)", true)]
        static bool V8() => Ready;

        [MenuItem(Check + "9. 가짜 기록 29건 추가 (30건 한도 시험)", true)]
        static bool V9() => Ready;

        [MenuItem(Check + "10. 기록 개수·맨 앞 이름 로그", true)]
        static bool V10() => Ready;

        [MenuItem(Check + "11. 각 길드원 의뢰·상태 로그", true)]
        static bool V11() => Ready;

        [MenuItem(Check + "X1. 실전 전투: 고블린 전사 4마리 · 체력 유지 · 구사일생 확정 켬")]
        static void RealFight()
        {
            var legacy = Services.Get<GuildLegacy>();
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                a.Data.woundedUntilHour = 0f;
                a.Data.lastStandHour = -999f;
            }
            legacy.ForcedRoll = 0f;
            Debug.Log("[Legacy] " + DebugCommands.SpawnSkirmish("goblin_warrior", 4, true));
            LogState("실전 전투 시작 (구사일생 확정 켜짐)");
        }

        [MenuItem(Check + "X2. 전원 HP 30%로")]
        static void HpLow()
        {
            var members = Services.Get<GuildRoster>().Members;
            for (int i = 0; i < members.Count; i++)
            {
                float cut = members[i].Hp - members[i].Stats.MaxHp * 0.3f;
                if (cut > 0f) DebugCommands.Hurt(i, cut);
            }
            LogState("전원 HP 30%");
        }

        [MenuItem(Check + "X3. 전원 HP 최대로")]
        static void HpFull()
        {
            foreach (var a in Services.Get<GuildRoster>().Members) a.Heal(a.Stats.MaxHp);
            LogState("전원 HP 최대");
        }

        [MenuItem(Check + "X1. 실전 전투: 고블린 전사 4마리 · 체력 유지 · 구사일생 확정 켬", true)]
        static bool VX1() => Ready;

        [MenuItem(Check + "X2. 전원 HP 30%로", true)]
        static bool VX2() => Ready;

        [MenuItem(Check + "X3. 전원 HP 최대로", true)]
        static bool VX3() => Ready;

        static void SetRelation(Adventurer from, Adventurer to, int target)
            => Relationships.Add(from, to, target - Relationships.Get(from, to), "검증");

        static Dictionary<string, float> Snapshot()
            => Services.Get<GuildRoster>().Members.ToDictionary(a => a.Data.name, a => a.Data.stress);

        static string Diff(Dictionary<string, float> before)
        {
            var sb = new StringBuilder();
            foreach (var a in Services.Get<GuildRoster>().Members)
            {
                before.TryGetValue(a.Data.name, out float was);
                sb.AppendLine($"  {a.Data.name}: 스트레스 {was:0.#} → {a.Data.stress:0.#} ({a.Data.stress - was:+0.#;-0.#;0})");
            }
            return sb.ToString();
        }

        static void LogState(string title)
        {
            var legacy = Services.Get<GuildLegacy>();
            var m = Services.Get<GuildRoster>().Members;
            var sb = new StringBuilder($"[Legacy] {title} · {Services.Get<GameClock>().FormatTime()} · 골드 {Services.Get<GuildTreasury>().Gold}\n");
            for (int i = 0; i < m.Count; i++)
            {
                var a = m[i];
                sb.Append($"  #{i} {a.Data.name} 스트레스={a.Data.stress:0.#} hp={a.Hp:0}/{a.Stats.MaxHp:0} state={a.StateLabel} wounded={legacy.IsWounded(a)}");
                for (int j = 0; j < m.Count; j++)
                    if (j != i) sb.Append($" | →{m[j].Data.name} {Relationships.Get(a, m[j])}");
                sb.AppendLine();
                foreach (var mem in a.Data.memories.Where(e => e.key != null && (e.key.StartsWith("relic") || e.key.StartsWith("memorial") || e.key == "last-stand" || e.key == "treated")))
                    sb.AppendLine($"      기억 {mem.key}: {mem.Format()}");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>Jumps the clock forward; the clock publishes the skipped hours on its next update.</summary>
        static void AdvanceHours(float hours)
        {
            var clock = Services.Get<GameClock>();
            var field = typeof(GameClock).GetField("_totalHours", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) { Debug.LogWarning("[Legacy] GameClock._totalHours를 찾지 못했습니다."); return; }
            field.SetValue(clock, clock.TotalHours + hours);
            Debug.Log($"[Legacy] 시계 +{hours}시간 → {clock.FormatTime()}");
        }
    }

    /// <summary>Copies every "[Legacy]" log line to legacy_verify.log in the project folder, so play-verification results can be read without the Console window.</summary>
    [InitializeOnLoad]
    static class LegacyLogFile
    {
        static readonly string LogPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "legacy_verify.log"));

        static LegacyLogFile()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (!message.Contains("[Legacy]")) return;
            try { System.IO.File.AppendAllText(LogPath, $"{System.DateTime.Now:HH:mm:ss} {message}\n"); }
            catch { /* the log file is a convenience */ }
        }
    }
}
