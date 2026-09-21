using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Legacy
{
    /// <summary>Wording for the relics, memorials, last stands and wounds of F-6, kept in one place so it can be tested and reworded.</summary>
    public static class LegacyTexts
    {
        static string Josa(string word, string withBatchim, string withoutBatchim) => MemoryRecorder.Josa(word, withBatchim, withoutBatchim);

        /// <summary>"체력 물약" or "체력 물약 x3".</summary>
        public static string ItemLabel(string name, int count) => count > 1 ? $"{name} x{count}" : name;

        // ---------------- Notifications ----------------

        public const string RelicTitle = "유품 회수 가능";

        public static string RelicNotice(string dead, int items, float hoursLeft)
            => $"{dead}의 유품 {items}개가 쓰러진 자리에 떨어져 있습니다. 약 {Mathf.CeilToInt(hoursLeft)}시간 안에 되찾지 못하면 사라집니다. 추모의 벽에서 길드원을 보낼 수 있습니다.";

        public const string RecoveredTitle = "유품 회수";

        public static string Recovered(string by, string dead, string item)
            => $"{by}{Josa(by, "이", "가")} {dead}의 유품({item})을 되찾아 왔습니다.";

        public const string LostTitle = "유품 소실";

        public static string LostTo(string dead, string item, string taker)
            => $"{dead}의 유품({item})이 {taker}에게 넘어갔습니다.";

        public static string LostRotted(string dead, string item) => $"{dead}의 유품({item})이 시간이 지나 사라졌습니다.";

        public const string LastStandTitle = "구사일생";

        public static string LastStandNotice(string name, string killer, float woundedHours)
        {
            string by = string.IsNullOrEmpty(killer) ? "" : $"{killer}에게 ";
            return $"{name}{Josa(name, "이", "가")} {by}치명상을 입었지만 간신히 살아남았습니다. 중상이라 약 {Mathf.CeilToInt(woundedHours)}시간은 의뢰를 맡지 못합니다.";
        }

        // ---------------- What members say ----------------

        static readonly string[] LastStandLines =
        {
            "아직... 끝난 게 아니야!",
            "이렇게는... 못 죽어!",
            "크윽... 일단 물러서야 해!",
        };

        public static string LastStandLine(GameRandom rng) => rng.Pick(LastStandLines);

        public static string RetrieverLine(string dead) => $"{dead}의 유품을 찾아오겠어.";

        public static string WoundedRefusal => "중상을 입어 회복 중이다";

        // ---------------- Memories ----------------

        public static string MemoryLastStand(string killer)
            => string.IsNullOrEmpty(killer) ? "치명상을 입고도 간신히 살아남았다" : $"{killer}에게 치명상을 입고도 간신히 살아남았다";

        public static string MemoryRelic(string dead) => $"동료 {dead}의 유품을 되찾아 왔다";

        public static string MemoryRelicHome(string dead) => $"{dead}의 유품이 돌아와 마음이 조금 놓였다";

        public static string MemoryMemorial(string dead) => $"{dead}의 추모식에 참석했다";

        public static string MemoryMemorialFriend(string dead) => $"{dead}{Josa(dead, "을", "를")} 기리며 작별 인사를 했다";

        public static string MemoryTreated(int cost) => $"길드 마스터가 {cost}G를 들여 상처를 치료해 주었다";

        // ---------------- Windows ----------------

        public static string Summary(int fallen)
            => fallen == 0 ? "아직 쓰러진 길드원이 없습니다." : $"쓰러진 길드원 {fallen}명 · 죽은 길드원은 되살릴 수 없지만, 유품을 되찾고 추모식을 열어 남은 이들의 마음을 달랠 수 있습니다.";

        public static string Killer(string killer) => string.IsNullOrEmpty(killer) ? "쓰러졌다" : $"{killer}에게 쓰러졌다";

        /// <summary>Where the relics stand: what still lies there (and for how long), what came home, what was lost.</summary>
        public static string RelicLine(FallenRecord r, float now)
        {
            if (r == null || !r.HasRelics) return "남긴 물건이 없습니다.";
            var parts = new List<string>();
            int lying = r.Count(RelicState.Lying), back = r.Count(RelicState.Recovered), lost = r.Count(RelicState.Lost);
            if (lying > 0) parts.Add($"현장에 {lying}개 (약 {Mathf.CeilToInt(r.HoursLeft(now))}시간 남음)");
            if (back > 0) parts.Add($"되찾음 {back}개");
            if (lost > 0) parts.Add($"잃음 {lost}개");
            return "유품: " + string.Join(" · ", parts);
        }

        /// <summary>The items themselves, e.g. "1등급 검, 뿔 x2" (for the tooltip).</summary>
        public static string RelicList(FallenRecord r)
        {
            if (r == null || !r.HasRelics) return "";
            var sb = new StringBuilder();
            foreach (var item in r.Relics)
            {
                if (sb.Length > 0) sb.Append('\n');
                string state = item.State switch
                {
                    RelicState.Recovered => "되찾음" + (string.IsNullOrEmpty(item.Note) ? "" : $" ({item.Note})"),
                    RelicState.Lost => "잃음" + (string.IsNullOrEmpty(item.Note) ? "" : $" ({item.Note})"),
                    _ => "현장에 있음",
                };
                sb.Append($"{ItemLabel(item.Name, item.Count)} — {state}");
            }
            return sb.ToString();
        }

        public static string MemorialDone(string dead, int cost, int members, int friends)
            => $"{dead}의 추모식을 열었습니다. (-{cost}G) 길드원 {members}명의 마음이 조금 풀렸고" + (friends > 0 ? $", 가까웠던 {friends}명은 더 크게 위로받았습니다." : ".");

        public static string SentRetriever(string retriever, string dead)
            => $"{retriever}{Josa(retriever, "이", "가")} {dead}의 유품을 찾으러 떠납니다.";

        public static string Treated(string name, int cost, int hpPercent)
            => $"{name}{Josa(name, "을", "를")} 치료했습니다. (-{cost}G) 체력 {hpPercent}%, 이제 의뢰를 맡을 수 있습니다.";
    }
}
