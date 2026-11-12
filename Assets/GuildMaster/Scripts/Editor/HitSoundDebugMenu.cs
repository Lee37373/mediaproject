using GuildMaster.Audio;
using GuildMaster.Core;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Trying the candidate hit sounds (2026-11-12): pick a set from GuildMaster → 타격음 후보 (it stays picked, also in
    /// Play), and 들어보기 plays the four blows during Play. See <see cref="HitSoundSets"/>.
    /// </summary>
    public static class HitSoundDebugMenu
    {
        const string Root = "GuildMaster/타격음 후보/";
        const string Now = Root + "지금 쓰는 소리", A = Root + "A 묵직한 액션", B = Root + "B 경쾌한 캐주얼",
            C = Root + "C 실감 무기", D = Root + "D 과장된 만화풍", Listen = Root + "들어보기 (플레이 중)";

        [MenuItem(Now, false, 0)] static void PickNow() => Pick("");
        [MenuItem(A, false, 1)] static void PickA() => Pick("A");
        [MenuItem(B, false, 2)] static void PickB() => Pick("B");
        [MenuItem(C, false, 3)] static void PickC() => Pick("C");
        [MenuItem(D, false, 4)] static void PickD() => Pick("D");

        [MenuItem(Now, true)] static bool CheckNow() => Check(Now, "");
        [MenuItem(A, true)] static bool CheckA() => Check(A, "A");
        [MenuItem(B, true)] static bool CheckB() => Check(B, "B");
        [MenuItem(C, true)] static bool CheckC() => Check(C, "C");
        [MenuItem(D, true)] static bool CheckD() => Check(D, "D");

        [MenuItem(Listen, false, 20)]
        static void Preview()
        {
            if (Application.isPlaying && Services.TryGet<SoundService>(out var sound)) sound.PreviewHits();
            else Debug.Log("[Sound] 들어보기는 플레이 중에만 됩니다. 세트는 골라 둔 대로 플레이에서 쓰입니다.");
        }

        static bool Check(string item, string set)
        {
            Menu.SetChecked(item, HitSoundSets.Current == set);
            return true;
        }

        static void Pick(string set)
        {
            HitSoundSets.Current = set;
            Debug.Log($"[Sound] 타격음: {HitSoundSets.Label(set)}");
            if (Application.isPlaying && Services.TryGet<SoundService>(out var sound)) sound.PreviewHits();
        }
    }
}
