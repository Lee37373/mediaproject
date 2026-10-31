using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Units
{
    /// <summary>Tiny sprite-based HP bar above a unit, visible while damaged (or always, if set).</summary>
    public class HealthBar : MonoBehaviour
    {
        const float Width = 0.8f;
        const float Height = 0.09f;

        Unit _unit;
        Transform _root;
        Transform _fill;
        SpriteRenderer _fillSr;
        SpriteRenderer _bgSr;

        public bool AlwaysVisible { get; set; }

        public void Init(Unit unit, float heightOffset, Color fillColor)
        {
            _unit = unit;
            if (_root == null)
            {
                _root = new GameObject("HealthBar").transform;
                _root.SetParent(transform, false);
                _bgSr = CreateBar("Bg", new Color(0f, 0f, 0f, 0.7f), Fog.FogOfWar.SortingOrder - 50);
                _bgSr.transform.localScale = new Vector3(Width + 0.06f, Height + 0.06f, 1f);
                _fillSr = CreateBar("Fill", fillColor, Fog.FogOfWar.SortingOrder - 49);
                _fill = _fillSr.transform;
            }
            _fillSr.color = fillColor;
            _root.localPosition = new Vector3(0f, heightOffset, 0f);
        }

        SpriteRenderer CreateBar(string name, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.White;
            sr.color = c;
            sr.sortingOrder = order;
            go.transform.localScale = new Vector3(Width, Height, 1f);
            return sr;
        }

        void LateUpdate()
        {
            if (_unit == null || _root == null) return;
            float ratio = _unit.HpRatio;
            bool show = _unit.IsAlive && (AlwaysVisible || ratio < 0.999f);
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;
            _fill.localScale = new Vector3(Width * ratio, Height, 1f);
            _fill.localPosition = new Vector3(-Width * (1f - ratio) * 0.5f, 0f, 0f);
        }
    }
}
