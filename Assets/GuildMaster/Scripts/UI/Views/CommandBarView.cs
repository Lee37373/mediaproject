using GuildMaster.Commands;
using GuildMaster.Core;
using GuildMaster.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The guild master's orders at the bottom of the screen (P4-132): the command gauge and four buttons (keys 5–8) with their
    /// cost or cooldown; hovering one shows how far the master's voice carries. The focus order asks for a monster on the map.
    /// A line above the bar says what came of an order, or why it could not be given; the master's shout floats over its head.
    /// </summary>
    public class CommandBarView : MonoBehaviour
    {
        const float Width = 620f, MessageSeconds = 5f;

        MasterCommands _orders;
        Image _fill;
        Text _power;
        Text _message;
        CanvasGroup _messageGroup;
        readonly Button[] _buttons = new Button[CommandRules.All.Length];
        readonly Text[] _labels = new Text[CommandRules.All.Length];
        float _messageLeft;
        float _timer;
        bool _picking;

        public void Build(RectTransform canvas)
        {
            if (!Services.TryGet(out _orders)) return;

            var root = UIBuilder.Panel(canvas, "CommandBar", UITheme.PanelBg).rectTransform;
            root.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(Width, 84));
            UIBuilder.VLayout(root, 6, 8);

            var gauge = UIBuilder.Row(root, 18, 8);
            UIBuilder.Label(gauge, "지휘력", 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(56, 18, 0);
            var track = UIBuilder.Panel(gauge, "Track", UIChips.Track).rectTransform;
            track.Size(-1, 18, 1);
            _fill = UIChips.Fill(track, new Color(0.62f, 0.45f, 0.95f, 1f));
            _power = UIBuilder.Label(gauge, "", 15, TextAnchor.MiddleRight, UITheme.Text).Size(70, 18, 0);
            track.Tooltip(() => $"지휘력: 명령마다 씁니다. 시간이 지나면 시간당 {CommandRules.PowerPerHour:0}씩 차고, 의뢰 완료 +{CommandRules.QuestDonePower:0}, 침공을 막으면 +{CommandRules.RaidHeldPower:0}.");

            var row = UIBuilder.Row(root, 40, 8);
            foreach (var c in CommandRules.All)
            {
                var cmd = c;
                var b = UIBuilder.Button(row, "", () => Give(cmd), -1, 40, null, 15);
                b.Size(-1, 40, 1);
                _buttons[(int)c] = b;
                _labels[(int)c] = b.GetComponentInChildren<Text>();
                _labels[(int)c].supportRichText = true;
                b.Tooltip(() => $"{CommandRules.Label(cmd)} ({CommandRules.Key(cmd)}) — 지휘력 {CommandRules.Cost(cmd):0}, 다시 쓰기까지 {CommandRules.Hours(CommandRules.CooldownHours(cmd))}\n{CommandRules.Describe(cmd)}");
                b.gameObject.AddComponent<ReachOnHover>().Orders = _orders;
            }

            var msg = UIBuilder.Panel(canvas, "CommandMessage", new Color(0.08f, 0.07f, 0.11f, 0.9f), raycast: false);
            msg.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 98), new Vector2(Width + 120f, 30));
            _messageGroup = msg.gameObject.AddComponent<CanvasGroup>();
            _messageGroup.alpha = 0f;
            _messageGroup.blocksRaycasts = false;
            _message = UIBuilder.Label(msg.transform, "", 15, TextAnchor.MiddleCenter, UITheme.Text);
            _message.rectTransform.Stretch(10, 10, 0, 0);

            EventBus.Subscribe<MasterCommandEvent>(OnCommand);
            Refresh();
        }

        void OnDestroy() => EventBus.Unsubscribe<MasterCommandEvent>(OnCommand);

        void OnCommand(MasterCommandEvent e)
        {
            if (Services.TryGet<FloatingTextLayer>(out var floating))
                floating.Spawn(_orders.MasterPosition + new Vector2(0f, 1.3f), CommandRules.Shout(e.Command), UITheme.Accent, 22);
        }

        void Update()
        {
            if (_orders == null) return;
            if (_messageLeft > 0f)
            {
                _messageLeft -= Time.unscaledDeltaTime;
                _messageGroup.alpha = Mathf.Clamp01(_messageLeft / 0.6f);
            }
            Hotkeys();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.2f;
            Refresh();
        }

        void Hotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null || MenuState.IsOpen || IsTyping()) return;
            if (Services.TryGet<MapPicker>(out var picker) && picker.IsPicking) return;
            if (kb.digit5Key.wasPressedThisFrame) Give(MasterCommand.Cheer);
            else if (kb.digit6Key.wasPressedThisFrame) Give(MasterCommand.Focus);
            else if (kb.digit7Key.wasPressedThisFrame) Give(MasterCommand.Rally);
            else if (kb.digit8Key.wasPressedThisFrame) Give(MasterCommand.Retreat);
        }

        void Refresh()
        {
            float p = _orders.Power;
            UIChips.SetFill(_fill, p / CommandRules.MaxPower);
            _power.text = $"{Mathf.FloorToInt(p)} / {CommandRules.MaxPower:0}";
            foreach (var c in CommandRules.All)
            {
                float wait = _orders.HoursToReady(c);
                bool short_ = p + 1e-3f < CommandRules.Cost(c);
                string tail = wait > 0f ? UIBuilder.Colored(CommandRules.Hours(wait), UITheme.TextMuted)
                    : UIBuilder.Colored($"{CommandRules.Cost(c):0}", short_ ? UITheme.Bad : UITheme.Accent);
                string name = wait > 0f || short_ ? UIBuilder.Colored(CommandRules.Label(c), UITheme.TextMuted) : CommandRules.Label(c);
                _labels[(int)c].text = $"{name} <size=12>({CommandRules.Key(c)})</size> · {tail}";
            }
            if (_orders.HasFlag)
                _labels[(int)MasterCommand.Rally].text += UIBuilder.Colored(" ⚑", new Color(0.75f, 0.55f, 1f));
        }

        void Give(MasterCommand c)
        {
            if (_picking) return;
            var refusal = _orders.Check(c);
            if (refusal != CommandRefusal.None)
            {
                Say(CommandRules.Why(refusal, c, _orders.Power, _orders.HoursToReady(c)), false);
                return;
            }
            if (c != MasterCommand.Focus)
            {
                bool given = _orders.TryIssue(c, null, out var message);
                Say(message, given);
                return;
            }
            if (!Services.TryGet<MapPicker>(out var picker)) return;
            _picking = true;
            _orders.ShowReach(true);
            picker.Begin($"집중 공격: 마스터 {CommandRules.Reach:0}칸 안의 몬스터를 고르세요 (오른쪽 클릭·ESC 취소)", pos =>
            {
                EndPicking();
                bool ok = _orders.TryIssue(MasterCommand.Focus, pos, out var message);
                Say(message, ok);
            }, EndPicking);
        }

        void EndPicking()
        {
            if (!_picking) return;
            _picking = false;
            _orders.ShowReach(false);
        }

        void Say(string text, bool done)
        {
            if (_message == null || string.IsNullOrEmpty(text)) return;
            _message.text = text;
            _message.color = done ? UITheme.Text : UITheme.Warn;
            _messageLeft = MessageSeconds;
            _messageGroup.alpha = 1f;
            Refresh();
        }

        static bool IsTyping()
        {
            var es = EventSystem.current;
            return es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }

        /// <summary>Hovering an order's button shows how far the master's voice carries.</summary>
        class ReachOnHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public MasterCommands Orders;
            bool _on;

            public void OnPointerEnter(PointerEventData e) => Set(true);
            public void OnPointerExit(PointerEventData e) => Set(false);
            void OnDisable() => Set(false);

            void Set(bool on)
            {
                if (on == _on || Orders == null) return;
                _on = on;
                Orders.ShowReach(on);
            }
        }
    }
}
