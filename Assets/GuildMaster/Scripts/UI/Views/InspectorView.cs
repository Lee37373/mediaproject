using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Left-click selection on the map + bottom-right info card for the selected
    /// adventurer, monster, nest or building. A ring marks the selection in the world.
    /// </summary>
    public class InspectorView : MonoBehaviour
    {
        RectTransform _panel;
        Text _title;
        Text _body;
        SpriteRenderer _ring;
        object _selected;
        float _timer;

        public object Selected => _selected;

        public void Build(RectTransform canvas)
        {
            var img = UIBuilder.Panel(canvas, "Inspector", UITheme.PanelBg);
            _panel = img.rectTransform;
            _panel.Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(420, 250));
            _title = UIBuilder.Label(_panel, "", 18, TextAnchor.UpperLeft, UITheme.Accent, FontStyle.Bold);
            _title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(-24, 26));
            _body = UIBuilder.Label(_panel, "", 14, TextAnchor.UpperLeft);
            _body.rectTransform.Stretch(12, 12, 38, 10);
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _panel.gameObject.SetActive(false);

            var ringGo = new GameObject("SelectionRing");
            _ring = ringGo.AddComponent<SpriteRenderer>();
            _ring.sprite = SpriteFactory.Circle(20, new Color(1, 1, 1, 0), new Color(1f, 0.9f, 0.3f, 1f));
            _ring.sortingOrder = 1990;
            ringGo.transform.localScale = new Vector3(1.2f, 0.6f, 1f);
            ringGo.SetActive(false);
        }

        public void Show(object target)
        {
            _selected = target;
            _panel.gameObject.SetActive(target != null);
            _timer = 0f;
        }

        public void Clear() => Show(null);

        void Update()
        {
            HandleClick();

            if (_selected is Unit u && (u == null || !u.IsAlive)) Clear();
            if (_selected == null) { _ring.gameObject.SetActive(false); return; }

            UpdateRing();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            RefreshText();
        }

        void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (Services.TryGet<MapPicker>(out var picker) && picker.IsPicking) return;
            var world = Services.Get<CameraController>().MouseWorldPosition();
            Show(PickAt(world));
        }

        static object PickAt(Vector2 p)
        {
            Services.TryGet<FogOfWar>(out var fog);
            var unit = UnitRegistry.FindNearest(p + new Vector2(0, -0.1f), 0.8f,
                u => u.Faction == Faction.Guild || fog == null || fog.IsVisible(u.Position));
            if (unit != null) return unit;
            var eco = Services.Get<EcosystemManager>();
            var nest = eco.Nests.Where(n => n.Discovered && Vector2.Distance(n.WorldPosition, p) < 1.2f)
                .OrderBy(n => Vector2.Distance(n.WorldPosition, p)).FirstOrDefault();
            if (nest != null) return nest;
            var cell = World.GridMap.WorldToCell(p);
            return Services.Get<TownService>().Buildings.FirstOrDefault(b => b.Footprint.Contains(cell));
        }

        void UpdateRing()
        {
            Vector2 pos;
            float scale = 1f;
            switch (_selected)
            {
                case Unit u: pos = u.Position + new Vector2(0, -0.28f); if (u is Monster m) scale = m.Def.scale; break;
                case Nest n: pos = n.WorldPosition; scale = 1.4f; break;
                default: _ring.gameObject.SetActive(false); return;
            }
            _ring.gameObject.SetActive(true);
            _ring.transform.position = pos;
            _ring.transform.localScale = new Vector3(1.2f * scale, 0.6f * scale, 1f);
        }

        void RefreshText()
        {
            switch (_selected)
            {
                case Adventurer a: ShowAdventurer(a); break;
                case Monster m: ShowMonster(m); break;
                case Nest n: ShowNest(n); break;
                case Building b: ShowBuilding(b); break;
                case PlayerAvatar: break;
            }
        }

        static string Muted(string s) => UIBuilder.Colored(s, UITheme.TextMuted);

        void ShowAdventurer(Adventurer a)
        {
            var d = a.Data;
            _title.text = $"{d.name}  {Muted($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}")}";
            var s = a.Stats;
            string quest = a.CurrentQuest != null ? $"{a.CurrentQuest.Title} ({a.CurrentQuest.ProgressText})" : "없음";
            var db = Services.Get<GameDatabase>();
            string loot = d.loot.Count == 0 ? "없음" : string.Join(", ", d.loot.Select(l => $"{db.Item(l.itemId)?.displayName ?? l.itemId} x{l.count}"));
            _body.text =
                $"상태: {UIBuilder.Colored(a.StateLabel, UITheme.Accent)} · 의뢰: {quest}\n" +
                $"HP {a.Hp:0}/{s.MaxHp:0} · 공격 {s.Attack:0.#} · 방어 {s.Defense:0.#} · 사거리 {s.AttackRange:0.#}\n" +
                $"EXP {d.exp}/{d.ExpToNextLevel} · 골드 {d.gold} · 물약 {d.potions} · 장비 {d.gearTier}등급\n" +
                $"피로 {d.fatigue:0} · 스트레스 {d.stress:0} · 처치 {d.kills} · 완료 의뢰 {d.questsCompleted}\n" +
                $"성격 {d.mbti} · 가치관 {d.values}\n" +
                $"{Muted("특성: " + (d.traitIds.Count == 0 ? "-" : string.Join(", ", d.traitIds)))}\n" +
                $"{Muted("전리품: " + loot)}";
        }

        void ShowMonster(Monster m)
        {
            var d = m.Def;
            _title.text = $"{d.displayName}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(d.danger), UITheme.Danger(d.danger))}";
            string prey = d.prey.Count == 0 ? "-" : string.Join(", ", d.prey.Select(p => p.displayName));
            string rivals = d.rivals.Count == 0 ? "-" : string.Join(", ", d.rivals.Select(p => p.displayName));
            _body.text =
                $"상태: {UIBuilder.Colored(m.StateLabel, UITheme.Accent)} · 소속: {m.Home?.DisplayName ?? "-"}\n" +
                $"HP {m.Hp:0}/{m.Stats.MaxHp:0} · 공격 {m.Stats.Attack:0.#} · 방어 {m.Stats.Defense:0.#}\n" +
                $"성향: {(d.aggressiveToHumans ? UIBuilder.Colored("사람을 공격함", UITheme.Bad) : "먼저 공격하지 않음")}\n" +
                $"먹이: {prey} · 적대: {rivals}\n" +
                $"{Muted("전리품: " + string.Join(", ", d.loot.Where(l => l.item).Select(l => $"{l.item.displayName}({l.chance * 100:0}%)")))}";
        }

        void ShowNest(Nest n)
        {
            var eco = Services.Get<EcosystemManager>();
            _title.text = n.DisplayName;
            _body.text =
                $"개체 수 {n.Population}/{n.Capacity} · 지형 {n.Biome}\n" +
                $"종 전체 개체 수 {eco.GetPopulation(n.Species)}{(eco.IsExtinct(n.Species) ? UIBuilder.Colored(" (멸종)", UITheme.Bad) : "")}\n" +
                (n.Species.prey.Count > 0 ? $"먹이 충족도 {eco.FoodFactor(n.Species) * 100f:0}%\n" : "") +
                Muted("둥지가 비면 다른 둥지의 개체가 다시 정착할 수 있습니다. 종 전체가 사라지면 멸종합니다.");
        }

        void ShowBuilding(Building b)
        {
            _title.text = $"{b.DisplayName}  {Muted($"Lv.{b.Level}")}";
            _body.text = $"내구도 {b.Hp:0}/{b.MaxHp:0}\n" + Muted(b.Type switch
            {
                BuildingType.TownHall => "마을의 중심. 파괴되면 게임이 끝납니다.",
                BuildingType.GuildHall => "모험가가 전리품을 팔고 보상을 받으며 쉬는 곳.",
                BuildingType.Blacksmith => "장비 제작 (업그레이드로 해금).",
                BuildingType.Apothecary => "물약 제작 (업그레이드로 해금).",
                _ => "대기 중인 모험가가 경험치를 얻습니다 (업그레이드로 해금).",
            });
        }
    }
}
