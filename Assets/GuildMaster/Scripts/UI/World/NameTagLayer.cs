using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Name labels above adventurers, monsters and the guild master. Hidden in fog and when the
    /// camera is zoomed far out. N toggles them.
    /// </summary>
    public class NameTagLayer : MonoBehaviour
    {
        /// <summary>Tags hide above this orthographic size to avoid clutter.</summary>
        const float MaxZoom = 14f;
        /// <summary>World-space gap above the unit's head (HP bar sits at 0.75 × scale).</summary>
        const float HeadOffset = 0.95f;

        static readonly Color AdventurerColor = new(0.7f, 1f, 0.7f);
        static readonly Color MonsterColor = new(1f, 0.72f, 0.66f);
        static readonly Color MasterColor = new(1f, 0.88f, 0.45f);
        static readonly Color VisitorColor = new(0.85f, 0.8f, 1f);

        RectTransform _layer;
        readonly List<Text> _tags = new();
        int _used;
        bool _enabled = true;

        public bool Enabled => _enabled;

        public void Build(RectTransform layer) => _layer = layer;

        Text Acquire()
        {
            if (_used < _tags.Count)
            {
                var t = _tags[_used++];
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                return t;
            }
            var text = UIBuilder.Label(_layer, "", 15, TextAnchor.LowerCenter, style: FontStyle.Bold);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(160f, 20f);
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);
            _tags.Add(text);
            _used++;
            return text;
        }

        void Place(Vector3 world, string label, Color color)
        {
            if (!WorldOverlay.TryWorldToLocal(_layer, world, out var local)) return;
            var t = Acquire();
            t.text = label;
            t.color = color;
            t.rectTransform.anchoredPosition = local;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.nKey.wasPressedThisFrame) return;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            _enabled = !_enabled;
        }

        void LateUpdate()
        {
            _used = 0;
            var cam = Camera.main;
            if (_enabled && cam != null && cam.orthographicSize <= MaxZoom)
            {
                Services.TryGet<FogOfWar>(out var fog);
                foreach (var u in UnitRegistry.All)
                {
                    if (!u.IsAlive) continue;
                    switch (u)
                    {
                        case Adventurer a:
                            if (a.IsVisitor) Place(a.transform.position + new Vector3(0f, HeadOffset), $"{a.Data.name} Lv.{a.Data.level} (방문자)", VisitorColor);
                            else Place(a.transform.position + new Vector3(0f, HeadOffset), $"{a.Data.name} Lv.{a.Data.level}", AdventurerColor);
                            break;
                        case Town.Merchant merchant:
                            Place(merchant.transform.position + new Vector3(0f, HeadOffset), "행상인", VisitorColor);
                            break;
                        case Monster m:
                            if (fog != null && !fog.IsVisible(m.Position)) break;
                            Place(m.transform.position + new Vector3(0f, HeadOffset * m.Def.scale), m.DisplayName, MonsterColor);
                            break;
                    }
                }
                if (Services.TryGet<PlayerAvatar>(out var avatar))
                    Place(avatar.transform.position + new Vector3(0f, HeadOffset), "길드 마스터", MasterColor);
            }
            for (int i = _used; i < _tags.Count; i++)
                if (_tags[i].gameObject.activeSelf) _tags[i].gameObject.SetActive(false);
        }
    }
}
