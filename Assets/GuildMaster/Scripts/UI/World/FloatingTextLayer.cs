using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Pooled floating numbers/labels (damage, rewards, level-ups) that rise and fade.</summary>
    public class FloatingTextLayer : MonoBehaviour
    {
        const string PoolKey = "ui.floatingText";
        const float Lifetime = 1.1f;

        RectTransform _layer;
        PoolService _pool;

        public void Build(RectTransform layer)
        {
            _layer = layer;
            _pool = Services.Get<PoolService>();
            _pool.Register(PoolKey, () =>
            {
                var t = UIBuilder.Label(_layer, "", 18, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                t.rectTransform.sizeDelta = new Vector2(200, 30);
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0, 0, 0, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                t.gameObject.AddComponent<FloatingText>();
                return t.gameObject;
            }, prewarm: 24, maxSize: 128);

            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<QuestRewardPaidEvent>(OnReward);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<SkillUsedEvent>(OnSkill);
            EventBus.Subscribe<ProjectileBlockedEvent>(OnBlocked);
            EventBus.Subscribe<UnitHealedEvent>(OnHealed);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<QuestRewardPaidEvent>(OnReward);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<SkillUsedEvent>(OnSkill);
            EventBus.Unsubscribe<ProjectileBlockedEvent>(OnBlocked);
            EventBus.Unsubscribe<UnitHealedEvent>(OnHealed);
        }

        static bool IsVisible(Vector2 p) => !Services.TryGet<FogOfWar>(out var fog) || fog.IsVisible(p);

        void OnSkill(SkillUsedEvent e)
        {
            if (e.User == null || !IsVisible(e.User.Position)) return;
            Spawn(e.User.transform.position + new Vector3(0, 1f), e.Name + "!", e.Color, 15);
        }

        void OnBlocked(ProjectileBlockedEvent e)
        {
            if (!IsVisible(e.Position)) return;
            Spawn(e.Position + new Vector2(0, 0.3f), "막힘", new Color(0.8f, 0.8f, 0.8f), 13);
        }

        void OnHealed(UnitHealedEvent e)
        {
            if (e.Target == null || !IsVisible(e.Target.Position)) return;
            Spawn(e.Target.transform.position + new Vector3(0, 0.75f), $"+{e.Amount:0}", new Color(0.45f, 1f, 0.5f), 17);
        }

        void OnDamaged(UnitDamagedEvent e)
        {
            if (e.Target == null) return;
            if (Services.TryGet<FogOfWar>(out var fog) && !fog.IsVisible(e.Target.Position)) return;
            var color = e.Target.Faction == Faction.Guild ? new Color(1f, 0.4f, 0.35f) : e.Critical ? UITheme.Accent : Color.white;
            Spawn(e.Target.transform.position + new Vector3(0, 0.6f), e.Critical ? $"{e.Amount:0}!" : $"{e.Amount:0}", color, e.Critical ? 22 : 17);
        }

        void OnReward(QuestRewardPaidEvent e)
        {
            if (e.Taker is Adventurer a)
                Spawn(a.transform.position + new Vector3(0, 0.9f), $"+{e.Gold}G  +{e.Exp}EXP", UITheme.Accent, 18);
        }

        void OnLevelUp(AdventurerLevelUpEvent e)
            => Spawn(e.Adventurer.transform.position + new Vector3(0, 1.1f), $"LEVEL UP! Lv.{e.Level}", UITheme.Exp, 20);

        public void Spawn(Vector3 world, string text, Color color, int size)
        {
            var go = _pool.Spawn(PoolKey, Vector3.zero, _layer);
            var ft = go.GetComponent<FloatingText>();
            ft.Begin(_layer, world, text, color, size, Lifetime);
        }

        class FloatingText : MonoBehaviour, IPoolable
        {
            Text _text;
            RectTransform _layer;
            Vector3 _world;
            float _t, _life;
            Color _color;
            Vector2 _jitter;

            void Awake() => _text = GetComponent<Text>();

            public void Begin(RectTransform layer, Vector3 world, string text, Color color, int size, float life)
            {
                _layer = layer;
                _world = world;
                _text.text = text;
                _text.fontSize = size;
                _color = color;
                _t = 0f;
                _life = life;
                _jitter = new Vector2(Random.Range(-12f, 12f), 0f);
                Place();
            }

            public void OnSpawned() { }
            public void OnDespawned() { }

            void Update()
            {
                _t += Time.unscaledDeltaTime;
                Place();
                if (_t >= _life && Services.TryGet<PoolService>(out var pool)) pool.Despawn(gameObject);
            }

            void Place()
            {
                float k = _t / _life;
                var visible = WorldOverlay.TryWorldToLocal(_layer, _world, out var local);
                _text.enabled = visible;
                if (!visible) return;
                ((RectTransform)transform).anchoredPosition = local + _jitter + new Vector2(0, 40f * k);
                _text.color = new Color(_color.r, _color.g, _color.b, 1f - k * k);
            }
        }
    }
}
