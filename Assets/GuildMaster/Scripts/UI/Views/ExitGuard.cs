using System;
using GuildMaster.Core;
using GuildMaster.Save;
using GuildMaster.Session;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>
    /// The exit warning (P6-1 S2, user decision 2026-11-09). Going back to the title, quitting, or closing the
    /// game window (a build) asks first when game time has passed since the last save or load: save and leave
    /// (straight into the game's own slot; a game never saved picks a slot in the save window, and saving there
    /// leaves), leave without saving, or cancel. With nothing to lose — just saved or loaded, the game over, the
    /// title screen — it leaves at once. Loading another game during play asks too (<see cref="SaveLoadView"/>,
    /// with <see cref="UnsavedText"/>).
    /// </summary>
    public class ExitGuard : MonoBehaviour
    {
        SaveLoadView _saves;
        TitleView _title;
        /// <summary>The player already answered: the next quit request goes through.</summary>
        bool _quitApproved;

        public void Build(SaveLoadView saves, TitleView title)
        {
            _saves = saves;
            _title = title;
            Application.wantsToQuit += OnWantsToQuit;
            Services.Register(this);
        }

        void OnDestroy() => Application.wantsToQuit -= OnWantsToQuit;

        /// <summary>
        /// Leaves through <paramref name="leave"/>, asking first if that would lose progress. Without the guard
        /// (a boot without UI) it leaves at once.
        /// </summary>
        public static void Request(string question, Action leave)
        {
            if (Services.TryGet<ExitGuard>(out var guard)) guard.Leave(question, leave);
            else leave?.Invoke();
        }

        public void Leave(string question, Action leave)
        {
            string lost = UnsavedText();
            if (lost == null || !Services.TryGet<SaveService>(out var saves))
            {
                Go(leave);
                return;
            }
            string save = saves.CurrentSlot >= 0 ? $"저장하고 나가기 (슬롯 {saves.CurrentSlot + 1})" : "저장하고 나가기…";
            ConfirmDialog.Ask(question, $"{lost}\n저장하지 않고 나가면 그동안의 진행이 사라집니다.",
                save, () => SaveThenLeave(leave), danger: false,
                alternative: "저장하지 않고 나가기", onAlternative: () => Go(leave));
        }

        void SaveThenLeave(Action leave)
        {
            if (!Services.TryGet<SaveService>(out var saves)) return;
            if (saves.CurrentSlot < 0)
            {
                // Never saved: pick a slot (overwriting asks as usual); the save itself leaves.
                if (_saves != null) _saves.ShowToSaveThen(() => Go(leave));
                return;
            }
            int slot = saves.CurrentSlot;
            if (saves.Save(slot, out var error)) Go(leave);
            else
                ConfirmDialog.Ask("저장하지 못했습니다", $"슬롯 {slot + 1}: {error}\n그래도 나가면 저장하지 않은 진행이 사라집니다.",
                    "저장하지 않고 나가기", () => Go(leave));
        }

        void Go(Action leave)
        {
            // Stays set: a build's quit request arrives after this frame, and a reload destroys the guard anyway.
            _quitApproved = true;
            leave?.Invoke();
        }

        /// <summary>The window's close button in a build (the editor never asks here — stopping play is not quitting).</summary>
        bool OnWantsToQuit()
        {
            if (_quitApproved || Application.isEditor) return true;
            if ((_title != null && _title.IsOpen) || UnsavedText() == null) return true;
            Leave("게임을 끝낼까요?", GameSettings.Quit);
            return false;
        }

        // ---------------- What leaving would lose ----------------

        /// <summary>Less game time than this (one game minute) since the last save counts as nothing: the frames between
        /// saving and the next click should not raise "1분 흘렀습니다".</summary>
        public const float MinUnsavedHours = 1f / 60f;

        /// <summary>
        /// What leaving now would lose, as a sentence ("슬롯 2에 저장한 뒤 게임 시간 5시간(실제 12분)이 흘렀습니다.");
        /// null when nothing would be: no game time since the last save or load, or the game is over.
        /// </summary>
        public static string UnsavedText()
        {
            if (!Services.TryGet<SaveService>(out var saves) || !Services.TryGet<GameClock>(out var clock)) return null;
            if (Services.TryGet<GameOverService>(out var over) && over.IsOver) return null;
            float hours = saves.UnsavedHours(clock.TotalHours);
            if (hours < MinUnsavedHours) return null;
            string played = UIBuilder.Colored(GameTime(hours), UITheme.Warn);
            if (saves.CleanAtReal is not DateTime at)
                return $"이 게임은 아직 저장한 적이 없습니다. 시작한 뒤 게임 시간 {played}이 흘렀습니다.";
            string real = RealAgo(DateTime.Now - at);
            return saves.CleanByLoad
                ? $"슬롯 {saves.CurrentSlot + 1}을 불러온 뒤 게임 시간 {played}({real})이 흘렀습니다."
                : $"슬롯 {saves.CurrentSlot + 1}에 저장한 뒤 게임 시간 {played}({real})이 흘렀습니다.";
        }

        /// <summary>Game hours as "2일 5시간", "5시간", "40분".</summary>
        public static string GameTime(float hours)
        {
            if (hours < 1f) return $"{Mathf.Max(1, Mathf.FloorToInt(hours * 60f))}분";
            int whole = Mathf.FloorToInt(hours);
            int days = whole / 24, rest = whole % 24;
            if (days == 0) return $"{rest}시간";
            return rest == 0 ? $"{days}일" : $"{days}일 {rest}시간";
        }

        /// <summary>Real time since, as "실제 1분 미만", "실제 12분", "실제 1시간 5분".</summary>
        public static string RealAgo(TimeSpan span)
        {
            if (span.TotalMinutes < 1) return "실제 1분 미만";
            if (span.TotalHours < 1) return $"실제 {(int)span.TotalMinutes}분";
            return span.Minutes == 0 ? $"실제 {(int)span.TotalHours}시간" : $"실제 {(int)span.TotalHours}시간 {span.Minutes}분";
        }
    }
}
