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
    /// ask again: the button changes to a question for a few seconds and a second click does it.
    /// </summary>
    public class SaveLoadView : MonoBehaviour
    {
        const float ConfirmSeconds = 3f;

        RectTransform _root;
        Text _title, _message;
        readonly Text[] _info = new Text[SaveService.SlotCount];
        readonly Button[] _save = new Button[SaveService.SlotCount];
        readonly Button[] _load = new Button[SaveService.SlotCount];
        readonly Button[] _delete = new Button[SaveService.SlotCount];
        bool _canSave;
        /// <summary>A button waiting for its second click: which slot, what for, until when (real time).</summary>
        int _confirmSlot = -1;
        string _confirmAction;
        float _confirmUntil;

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
            _canSave = canSave && !(Services.TryGet<GameOverService>(out var over) && over.IsOver);
            _title.text = _canSave ? "저장 / 불러오기" : "불러오기";
            _message.text = _canSave ? "" : "불러올 저장을 고르세요.";
            _confirmSlot = -1;
            Refresh();
            if (IsOpen) return;
            MenuState.Push();
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (!IsOpen) return;
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

        void Refresh()
        {
            for (int i = 0; i < SaveService.SlotCount; i++)
            {
                var info = SaveService.Peek(i);
                bool usable = info.Exists && info.Problem == null;
                _info[i].text = !info.Exists ? "<color=#8a8494>비어 있음</color>"
                    : info.Problem != null ? $"<color=#f0705a>{info.Problem}</color>"
                    : $"{info.Summary.day}일차 {info.Summary.hour:00}시 · {info.Summary.gold:N0}G · 길드원 {info.Summary.members}명\n"
                      + $"<size=13><color=#a8a2b4>{info.SavedAt:yyyy-MM-dd HH:mm} 저장</color></size>";
                _save[i].gameObject.SetActive(_canSave);
                _load[i].interactable = usable;
                _delete[i].interactable = info.Exists;
                SetLabel(_save[i], ConfirmingAt(i, "save") ? "덮어쓰기?" : "저장");
                SetLabel(_delete[i], ConfirmingAt(i, "delete") ? "삭제?" : "삭제");
            }
        }

        static void SetLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        bool ConfirmingAt(int slot, string action)
            => _confirmSlot == slot && _confirmAction == action && Time.unscaledTime < _confirmUntil;

        /// <summary>First click on something that destroys a save: ask; second click within a few seconds: do it.</summary>
        bool Confirmed(int slot, string action)
        {
            if (ConfirmingAt(slot, action))
            {
                _confirmSlot = -1;
                return true;
            }
            _confirmSlot = slot;
            _confirmAction = action;
            _confirmUntil = Time.unscaledTime + ConfirmSeconds;
            return false;
        }

        void OnSave(int slot)
        {
            if (!_canSave || !Services.TryGet<SaveService>(out var saves)) return;
            if (SaveService.Peek(slot).Exists && !Confirmed(slot, "save"))
            {
                _message.text = $"슬롯 {slot + 1}에 이미 저장이 있습니다. 한 번 더 누르면 덮어씁니다.";
                Refresh();
                return;
            }
            _message.text = saves.Save(slot, out var error) ? $"슬롯 {slot + 1}에 저장했습니다." : error;
            Refresh();
        }

        void OnLoad(int slot)
        {
            _message.text = "불러오는 중…";
            // The scene restarts with the save; on success nothing here runs again.
            if (!SaveService.Load(slot, out var error)) _message.text = error;
        }

        void OnDelete(int slot)
        {
            if (!Confirmed(slot, "delete"))
            {
                _message.text = $"한 번 더 누르면 슬롯 {slot + 1}을 지웁니다.";
                Refresh();
                return;
            }
            _message.text = SaveService.Delete(slot) ? $"슬롯 {slot + 1}을 지웠습니다." : "지우지 못했습니다.";
            Refresh();
        }

        void Update()
        {
            // A question nobody answered goes back to the plain button.
            if (IsOpen && _confirmSlot >= 0 && Time.unscaledTime >= _confirmUntil)
            {
                _confirmSlot = -1;
                Refresh();
            }
        }
    }
}
