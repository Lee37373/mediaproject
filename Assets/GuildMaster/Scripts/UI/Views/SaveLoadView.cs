using System;
using GuildMaster.Core;
using GuildMaster.Save;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Save and load (P5-4, user decision 2026-10-04: manual saves in several slots). One row per
    /// slot: what it holds (day, hour, gold, members, when it was saved) with save / load / delete.
    /// Opened from the pause menu (save and load) and the title (load only). Overwriting and deleting
    /// ask in the shared question window, and so does loading another game during play while there
    /// is unsaved progress (P6-1, user decision 2026-11-09). The exit warning opens it in "save, then
    /// leave" mode for a game never saved (<see cref="ShowToSaveThen"/>).
    /// </summary>
    public class SaveLoadView : MonoBehaviour
    {
        RectTransform _root;
        Text _title, _message;
        readonly Text[] _info = new Text[SaveService.SlotCount];
        readonly Button[] _save = new Button[SaveService.SlotCount];
        readonly Button[] _load = new Button[SaveService.SlotCount];
        readonly Button[] _delete = new Button[SaveService.SlotCount];
        bool _canSave;
        /// <summary>Runs once a save succeeds (the exit warning's "save and leave"), then forgotten.</summary>
        Action _afterSave;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Build(RectTransform canvas)
        {
            var dim = UIBuilder.Panel(canvas, "SaveLoad", new Color(0f, 0f, 0f, 0.55f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 470));
            UIBuilder.VLayout(box, 8, 22);
            _title = UIBuilder.Label(box.transform, "저장 / 불러오기", 26, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 40);

            for (int i = 0; i < SaveService.SlotCount; i++)
            {
                int slot = i;
                var row = UIBuilder.Row(box.transform, 50);
                var bg = row.gameObject.AddComponent<Image>();
                bg.color = i % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                UIBuilder.Label(row, $"슬롯 {slot + 1}", 16, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(70);
                _info[i] = UIBuilder.Label(row, "", 15, TextAnchor.MiddleLeft);
                _info[i].GetComponent<RectTransform>().Size(-1, -1, 1);
                _save[i] = UIBuilder.Button(row, "저장", () => OnSave(slot), 96, 38, new Color(0.25f, 0.45f, 0.3f), 15);
                _load[i] = UIBuilder.Button(row, "불러오기", () => OnLoad(slot), 96, 38, null, 15);
                _delete[i] = UIBuilder.Button(row, "삭제", () => OnDelete(slot), 70, 38, UITheme.ButtonDanger, 15);
            }

            _message = UIBuilder.Label(box.transform, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 44);
            _message.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Button(box.transform, "닫기", Hide, -1, 40, null, 17);
            _root.gameObject.SetActive(false);
        }

        /// <param name="canSave">False on the title screen (and after the game is over): loading only.</param>
        public void Show(bool canSave)
        {
            _afterSave = null;
            _canSave = canSave && !(Services.TryGet<GameOverService>(out var over) && over.IsOver);
            _title.text = _canSave ? "저장 / 불러오기" : "불러오기";
            _message.text = _canSave ? "" : "불러올 저장을 고르세요.";
            Open();
        }

        /// <summary>
        /// "Save and leave" for a game never saved (P6-1): saving only, and a successful save runs
        /// <paramref name="then"/>. Closing the window without saving leaves nothing behind.
        /// </summary>
        public void ShowToSaveThen(Action then)
        {
            _canSave = true;
            _afterSave = then;
            _title.text = "저장하고 나가기";
            _message.text = "저장할 칸을 고르세요. 저장하면 바로 나갑니다.";
            Open();
        }

        void Open()
        {
            Refresh();
            if (IsOpen) return;
            MenuState.Push();
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
            _afterSave = null;
            _root.gameObject.SetActive(false);
            MenuState.Pop();
        }

        /// <summary>Any slot holds a game that can be loaded (the title's button asks this).</summary>
        public static bool AnyLoadable()
        {
            for (int i = 0; i < SaveService.SlotCount; i++)
            {
                var info = SaveService.Peek(i);
                if (info.Exists && info.Problem == null) return true;
            }
            return false;
        }

        /// <summary>What a slot holds, in one or two lines (the slot rows and the questions).</summary>
        public static string Describe(SlotInfo info) =>
            !info.Exists ? "<color=#8a8494>비어 있음</color>"
            : info.Problem != null ? $"<color=#f0705a>{info.Problem}</color>"
            : $"{info.Summary.day}일차 {info.Summary.hour:00}시 · {info.Summary.gold:N0}G · 길드원 {info.Summary.members}명\n"
              + $"<size=13><color=#a8a2b4>{info.SavedAt:yyyy-MM-dd HH:mm} 저장</color></size>";

        void Refresh()
        {
            bool savingOnly = _afterSave != null;
            for (int i = 0; i < SaveService.SlotCount; i++)
            {
                var info = SaveService.Peek(i);
                bool usable = info.Exists && info.Problem == null;
                _info[i].text = Describe(info);
                _save[i].gameObject.SetActive(_canSave);
                _load[i].gameObject.SetActive(!savingOnly);
                _delete[i].gameObject.SetActive(!savingOnly);
                _load[i].interactable = usable;
                _delete[i].interactable = info.Exists;
            }
        }

        void OnSave(int slot)
        {
            if (!_canSave || !Services.TryGet<SaveService>(out _)) return;
            var info = SaveService.Peek(slot);
            if (!info.Exists)
            {
                SaveNow(slot);
                return;
            }
            bool ours = Services.Get<SaveService>().CurrentSlot == slot;
            ConfirmDialog.Ask($"슬롯 {slot + 1}에 덮어쓸까요?",
                $"지금 들어 있는 저장:\n{Describe(info)}\n"
                + (ours ? UIBuilder.Colored("이 게임을 마지막으로 저장한(불러온) 칸입니다.", UITheme.TextMuted) : UIBuilder.Colored("다른 게임일 수 있습니다. 덮어쓰면 되살릴 수 없습니다.", UITheme.Warn)),
                "덮어쓰기", () => SaveNow(slot));
        }

        void SaveNow(int slot)
        {
            if (!IsOpen || !Services.TryGet<SaveService>(out var saves)) return;
            if (!saves.Save(slot, out var error))
            {
                _message.text = UIBuilder.Colored(error, UITheme.Bad);
                Refresh();
                return;
            }
            var then = _afterSave;
            if (then != null)
            {
                Hide();
                then();
                return;
            }
            _message.text = $"슬롯 {slot + 1}에 저장했습니다.";
            Refresh();
        }

        void OnLoad(int slot)
        {
            // During play, loading throws away what was not saved: ask first (P6-1). The title has nothing to lose.
            string lost = _canSave ? ExitGuard.UnsavedText() : null;
            if (lost == null)
            {
                LoadNow(slot);
                return;
            }
            ConfirmDialog.Ask($"슬롯 {slot + 1}을 불러올까요?",
                $"{lost}\n불러오면 그동안의 진행이 사라집니다. 먼저 저장하려면 취소하고 빈 칸이나 이 게임의 칸에 저장하세요.\n\n불러올 저장: {Describe(SaveService.Peek(slot))}",
                "불러오기", () => LoadNow(slot));
        }

        void LoadNow(int slot)
        {
            _message.text = "불러오는 중…";
            // The scene restarts with the save; on success nothing here runs again.
            if (!SaveService.Load(slot, out var error)) _message.text = UIBuilder.Colored(error, UITheme.Bad);
        }

        void OnDelete(int slot)
        {
            var info = SaveService.Peek(slot);
            if (!info.Exists) return;
            ConfirmDialog.Ask($"슬롯 {slot + 1}을 지울까요?",
                $"{Describe(info)}\n지운 저장은 되살릴 수 없습니다.",
                "삭제", () =>
                {
                    _message.text = SaveService.Delete(slot) ? $"슬롯 {slot + 1}을 지웠습니다." : UIBuilder.Colored("지우지 못했습니다.", UITheme.Bad);
                    Refresh();
                });
        }
    }
}
