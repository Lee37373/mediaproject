using System;
using GuildMaster.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The one question window (P6-1 U4, user decision 2026-11-09): a title, a few lines and up to three
    /// buttons over a dimmed screen that blocks everything else. The game clock is held while it is open,
    /// Enter picks the first button and ESC the last (cancel) — <see cref="UIRoot"/> routes both keys before
    /// any other shortcut. Callers that destroy something (dismissal, demolition, overwriting or deleting a
    /// save) and the exit warning (<see cref="ExitGuard"/>) ask through <see cref="Ask"/>.
    /// </summary>
    public class ConfirmDialog : MonoBehaviour
    {
        const float Width = 700f;
        static readonly Color SafeGreen = new(0.25f, 0.45f, 0.3f);

        RectTransform _root, _box;
        Text _title, _body;
        // Two first buttons, one green and one red, so the skin is never painted twice on one button.
        Button _acceptSafe, _acceptDanger, _alternative, _cancel;
        Action _onAccept, _onAlternative, _onCancel;
        bool _holding;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        /// <summary>The question on screen (tests read it).</summary>
        public string TitleText => _title != null ? _title.text : "";
        public string BodyText => _body != null ? _body.text : "";
        /// <summary>Labels of the buttons on screen, left to right (tests read them).</summary>
        public string ButtonsText
        {
            get
            {
                if (!IsOpen) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var b in new[] { _acceptSafe, _acceptDanger, _alternative, _cancel })
                    if (b.gameObject.activeSelf) sb.Append(sb.Length > 0 ? " | " : "").Append(b.GetComponentInChildren<Text>().text);
                return sb.ToString();
            }
        }

        public void Build(RectTransform canvas)
        {
            var dim = UIBuilder.Panel(canvas, "ConfirmDialog", new Color(0f, 0f, 0f, 0.7f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            _box = box.rectTransform;
            _box.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, 200));
            UIBuilder.VLayout(box, 12, 24);
            UIBuilder.FitContent(box);
            // The skinned panel is see-through; an opaque backing inside its outline keeps the window behind from showing.
            var backing = UIBuilder.Panel(box.transform, "Backing", new Color(UITheme.PanelBg.r, UITheme.PanelBg.g, UITheme.PanelBg.b, 1f), raycast: false);
            backing.rectTransform.Stretch(5, 5, 5, 5);
            backing.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _title = UIBuilder.Label(box.transform, "", 23, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 36);
            _body = UIBuilder.Label(box.transform, "", 17, TextAnchor.UpperLeft, UITheme.Text);
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.lineSpacing = 1.1f;
            var row = UIBuilder.Row(box.transform, 46, 10);
            _acceptSafe = UIBuilder.Button(row, "", Accept, -1, 46, SafeGreen, 16).Size(-1, -1, 1);
            _acceptDanger = UIBuilder.Button(row, "", Accept, -1, 46, UITheme.ButtonDanger, 16).Size(-1, -1, 1);
            _alternative = UIBuilder.Button(row, "", Alternative, -1, 46, UITheme.ButtonDanger, 16).Size(-1, -1, 1);
            _cancel = UIBuilder.Button(row, "취소", Cancel, -1, 46, null, 16).Size(-1, -1, 1);
            UIBuilder.Label(box.transform, "Enter: 왼쪽 버튼 · ESC: 취소", 13, TextAnchor.MiddleRight, UITheme.TextMuted).Size(-1, 18);
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Asks a question. <paramref name="danger"/> paints the first button red (it destroys something);
        /// the optional middle button is always red (the exit warning's "leave without saving").
        /// Without a dialog (a boot without UI) the first button is taken at once.
        /// </summary>
        public static void Ask(string title, string body, string accept, Action onAccept, bool danger = true,
            string alternative = null, Action onAlternative = null, string cancel = "취소", Action onCancel = null)
        {
            if (!Services.TryGet<ConfirmDialog>(out var dialog))
            {
                onAccept?.Invoke();
                return;
            }
            dialog.Show(title, body, accept, onAccept, danger, alternative, onAlternative, cancel, onCancel);
        }

        public void Show(string title, string body, string accept, Action onAccept, bool danger = true,
            string alternative = null, Action onAlternative = null, string cancel = "취소", Action onCancel = null)
        {
            // A second question replaces the first, which counts as cancelled.
            if (IsOpen) Cancel();
            _title.text = title;
            _body.text = body ?? "";
            _body.gameObject.SetActive(!string.IsNullOrEmpty(body));
            _onAccept = onAccept;
            _onAlternative = onAlternative;
            _onCancel = onCancel;
            _acceptSafe.gameObject.SetActive(!danger);
            _acceptDanger.gameObject.SetActive(danger);
            (danger ? _acceptDanger : _acceptSafe).SetLabel(accept);
            _alternative.gameObject.SetActive(!string.IsNullOrEmpty(alternative));
            if (!string.IsNullOrEmpty(alternative)) _alternative.SetLabel(alternative);
            _cancel.SetLabel(string.IsNullOrEmpty(cancel) ? "취소" : cancel);

            MenuState.Push();
            if (Services.TryGet<GameClock>(out var clock)) { clock.Hold(); _holding = true; }
            // The button that asked stays selected otherwise, and Enter would "submit" it again behind the question.
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            GuildMaster.Audio.Sound.Play(GuildMaster.Audio.SoundKeys.Open);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_box);
        }

        public void Accept() => Answer(_onAccept);
        public void Alternative() => Answer(_onAlternative);
        public void Cancel() => Answer(_onCancel);

        /// <summary>Hides the window and lets go of the clock and menus first, then runs the answer
        /// (so it can open the next window — the save slots, another question).</summary>
        void Answer(Action chosen)
        {
            if (!IsOpen) return;
            _onAccept = _onAlternative = _onCancel = null;
            _root.gameObject.SetActive(false);
            MenuState.Pop();
            if (_holding && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holding = false;
            chosen?.Invoke();
        }
    }
}
