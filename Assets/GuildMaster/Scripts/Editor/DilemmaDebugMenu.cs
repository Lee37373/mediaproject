using GuildMaster.Core;
using GuildMaster.Dilemmas;
using GuildMaster.UI;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Editor-only shortcuts for trying decision cards, in play mode (KMS's event card menu, 2026-11-01; P4-118 the event
    /// cards are decision cards): deal one now (its window opens by itself), or open the window on the cards waiting.
    /// </summary>
    public static class DilemmaDebugMenu
    {
        [MenuItem("GuildMaster/결정 카드 지금 뽑기 (플레이 중)")]
        static void DealNow()
        {
            if (!Services.TryGet<DilemmaService>(out var service))
            {
                Debug.Log("[Dilemma] 플레이 중에 새 게임을 시작한 뒤 사용하세요.");
                return;
            }
            if (service.Deal() == null) Debug.Log("[Dilemma] 지금은 나올 수 있는 카드가 없습니다 (열린 카드가 있거나 조건이 맞지 않음).");
        }

        [MenuItem("GuildMaster/결정 카드 창 열기 (플레이 중)")]
        static void OpenWindow()
        {
            if (Services.TryGet<DilemmaView>(out var view)) view.Open();
        }

        [MenuItem("GuildMaster/결정 카드 지금 뽑기 (플레이 중)", true)]
        [MenuItem("GuildMaster/결정 카드 창 열기 (플레이 중)", true)]
        static bool InPlay() => Application.isPlaying;
    }
}
