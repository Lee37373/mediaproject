using GuildMaster.Core;
using GuildMaster.Incidents;
using GuildMaster.UI;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>Editor-only shortcut for trying event cards (2026-11-01): draws one now, in play mode.</summary>
    public static class EventCardDebugMenu
    {
        [MenuItem("GuildMaster/사건 카드 지금 뽑기 (플레이 중)")]
        static void DrawNow()
        {
            if (!Application.isPlaying || !Services.TryGet<EventCardService>(out var service))
            {
                Debug.Log("[EventCard] 플레이 중에 새 게임을 시작한 뒤 사용하세요.");
                return;
            }
            if (!service.DrawNow()) Debug.Log("[EventCard] 지금은 뽑을 수 있는 카드가 없습니다 (대기 중인 카드가 있거나 조건이 맞지 않음).");
        }

        [MenuItem("GuildMaster/대기 중인 사건 카드 열기 (플레이 중)")]
        static void OpenPending()
        {
            if (!Application.isPlaying) return;
            Services.TryGet<EventCardService>(out var service);
            Debug.Log($"[EventCard] 대기 중인 카드: {(service?.Pending != null ? service.Pending.Title : "없음")}");
            if (Services.TryGet<EventCardView>(out var view)) view.ShowCard();
        }

        [MenuItem("GuildMaster/대기 중인 사건 카드 열기 (플레이 중)", true)]
        static bool CanOpen() => Application.isPlaying;

        [MenuItem("GuildMaster/사건 카드 지금 뽑기 (플레이 중)", true)]
        static bool CanDraw() => Application.isPlaying;
    }
}
