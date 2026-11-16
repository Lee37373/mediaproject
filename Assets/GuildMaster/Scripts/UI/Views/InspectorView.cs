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
using GuildMaster.Traits;
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
        Button _followButton;
        Button _recruitButton;
        Button _dialogueButton;
        Button _detailButton;
        Button _repairButton;
        object _selected;
        MemberCard _member;
        float _timer;
        const float DoubleClickSeconds = 0.35f;
        object _lastClicked;
        float _lastClickTime;

        public object Selected => _selected;

        public void Build(RectTransform canvas)
        {
            var img = UIBuilder.Panel(canvas, "Inspector", UITheme.PanelBg);
            HudStyle.Card(img); // 2026-11-13: the outlined HUD card
            _panel = img.rectTransform;
            _panel.Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(420, MinHeight));
            _title = UIBuilder.Label(_panel, "", 18, TextAnchor.UpperLeft, UITheme.Accent, FontStyle.Bold);
            _title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(12, -8), new Vector2(-124, 26));
            _followButton = UIBuilder.Button(_panel, "추적 (F)", ToggleFocus, 96, 26, fontSize: 14);
            _followButton.GetComponent<RectTransform>().Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(96, 26));
            _recruitButton = UIBuilder.Button(_panel, "영입 대화", OnRecruitClicked, 120, 30, UITheme.Header, 15);
            _recruitButton.GetComponent<RectTransform>().Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(120, 30));
            _recruitButton.gameObject.SetActive(false);
            // Gate repair (P4-39), in the recruit button's corner: the two never show together.
            _repairButton = UIBuilder.Button(_panel, "수리", OnRepairClicked, 150, 30, UITheme.Header, 15);
            _repairButton.GetComponent<RectTransform>().Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 8), new Vector2(150, 30));
            _repairButton.gameObject.SetActive(false);
            _dialogueButton = UIBuilder.Button(_panel, "대화 기록", OnDialogueClicked, 100, 30, null, 15);
            _dialogueButton.gameObject.SetActive(false);
            _detailButton = UIBuilder.Button(_panel, "상세", OnDetailClicked, 80, 30, UITheme.Header, 15);
            _detailButton.gameObject.SetActive(false);
            _body = UIBuilder.Label(_panel, "", 14, TextAnchor.UpperLeft);
            _body.rectTransform.Stretch(12, 12, 38, 10);
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _member = MemberCard.Build(_panel, BodyTop); // a guild member's card (2026-11-13)
            _panel.gameObject.SetActive(false);

            var ringGo = new GameObject("SelectionRing");
            _ring = ringGo.AddComponent<SpriteRenderer>();
            _ring.sprite = SpriteFactory.Circle(20, new Color(1, 1, 1, 0), new Color(1f, 0.9f, 0.3f, 1f));
            _ring.sortingOrder = FogOfWar.SortingOrder - 1010;
            ringGo.transform.localScale = new Vector3(1.2f, 0.6f, 1f);
            ringGo.SetActive(false);
        }

        public void Show(object target)
        {
            bool wasShown = _panel.gameObject.activeSelf;
            _selected = target;
            UnitGizmos.Focus = target as Unit;
            _panel.gameObject.SetActive(target != null);
            if (target != null && !wasShown) UIMotion.PopIn(_panel, 0.94f, 0.18f, fade: false); // 2026-11-13 (DOTween)
            _timer = 0f;
            RefreshFollowButton();
            // Clicking a building also opens its upgrade window (P4-1).
            if (target is Building b && Services.TryGet<BuildingView>(out var view)) view.ShowBuilding(b);
            // Clicking a ruined watchtower opens its lookout window (2026-11-02): climb, flare, alarm bell.
            if (target is World.PlaceSite { Kind: World.PlaceKind.WatchRuin } ruin && Services.TryGet<WatchRuinView>(out var ruinView)) ruinView.ShowRuin(ruin);
            // Clicking the travelling merchant opens its trade window (P4-3).
            if (target is Merchant && Services.TryGet<MerchantView>(out var shop) && !shop.Visible) shop.Open();
            // Clicking a resident with a request opens it (P4-134, user decision: asked of the guild master directly).
            if (target is Social.Resident resident && Services.TryGet<Social.ResidentRequests>(out var requests)
                && requests.ActiveOf(resident.Profile.Id) != null && Services.TryGet<ResidentRequestView>(out var requestView))
                requestView.Show(resident);
        }

        public void Clear() => Show(null);

        /// <summary>The unit picked, or null (its name tag always shows, P4-123).</summary>
        public Unit SelectedUnit => _selected as Unit;

        void Update()
        {
            HandleClick();
            HandleFocusKey();

            if (_selected is Unit u && (u == null || !u.IsAlive || !IsSeen(u))) Clear();
            if (_selected is FogGhost g && (g == null || !g.IsActive)) Clear();
            if (_selected == null) { _ring.gameObject.SetActive(false); return; }

            UpdateRing();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            RefreshText();
            RefreshFollowButton();
        }

        void HandleClick()
        {
            var cam = Services.Get<CameraController>();
            if (!cam.MapClickReleased) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (Services.TryGet<MapPicker>(out var picker) && picker.IsPicking) return;
            // An incident's "?"/"!" mark takes the click and goes away (2026-11-13).
            if (Mouse.current != null && Services.TryGet<RumourMarkLayer>(out var marks) && marks.TryDismissAt(Mouse.current.position.ReadValue())) return;
            var picked = PickAt(cam.MouseWorldPosition());

            // Double-click on the same unit focuses (follows) it.
            bool doubleClick = picked is Unit && picked == _lastClicked && Time.unscaledTime - _lastClickTime < DoubleClickSeconds;
            _lastClicked = picked;
            _lastClickTime = Time.unscaledTime;
            Show(picked);
            if (doubleClick) FocusSelected();
        }

        void HandleFocusKey()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.fKey.wasPressedThisFrame || MenuState.IsOpen) return;
            if (Session.TextFocus.Typing) return; // P6-2: a field with the caret, not just a selected one
            if (_selected is Unit) { ToggleFocus(); return; }
            // Nothing selected: F toggles following the guild master avatar.
            if (!Services.TryGet<CameraController>(out var cam) || !Services.TryGet<PlayerAvatar>(out var avatar)) return;
            if (cam.FollowTarget == avatar.transform) cam.Follow(null);
            else cam.Focus(avatar.transform);
        }

        bool IsFollowingSelected
            => _selected is Unit u && Services.TryGet<CameraController>(out var cam) && cam.FollowTarget == u.transform;

        /// <summary>Camera follows the selected unit (NPC focus).</summary>
        public void FocusSelected()
        {
            if (_selected is Unit u && Services.TryGet<CameraController>(out var cam)) cam.Focus(u.transform);
            RefreshFollowButton();
        }

        void ToggleFocus()
        {
            if (IsFollowingSelected) Services.Get<CameraController>().Follow(null);
            else FocusSelected();
            RefreshFollowButton();
        }

        void OnRecruitClicked()
        {
            if (_selected is Adventurer { IsVisitor: true } v && Services.TryGet<RecruitDialogView>(out var dialog))
                dialog.BeginApproach(v);
        }

        void OnDetailClicked()
        {
            if (_selected is Adventurer a && Services.TryGet<AdventurerDetailView>(out var view)) view.Show(a);
        }

        void OnDialogueClicked()
        {
            if (_selected is Adventurer a && Services.TryGet<DialogueLogView>(out var view)) view.Show(a);
        }

        void RefreshFollowButton()
        {
            bool visitor = _selected is Adventurer { IsVisitor: true };
            _recruitButton.gameObject.SetActive(visitor);
            _dialogueButton.gameObject.SetActive(_selected is Adventurer);
            // Bottom-right corner; shifts left when the recruit button sits there.
            _dialogueButton.GetComponent<RectTransform>().Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(visitor ? -136 : -8, 8), new Vector2(100, 30));
            _detailButton.gameObject.SetActive(_selected is Adventurer);
            _detailButton.GetComponent<RectTransform>().Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(visitor ? -244 : -116, 8), new Vector2(80, 30));
            RefreshRepairButton();
            _followButton.gameObject.SetActive(_selected is Unit);
            _followButton.SetLabel(IsFollowingSelected ? "추적 해제" : "추적 (F)");
        }

        /// <summary>
        /// Guild units are always known; anything else only while the guild sees it. A selected
        /// monster walking into the fog is dropped, or the card and ring would keep tracking it.
        /// </summary>
        static bool IsSeen(Unit u)
            => u.Faction == Faction.Guild || !Services.TryGet<FogOfWar>(out var fog) || fog.IsVisible(u.Position);

        static object PickAt(Vector2 p)
        {
            var unit = UnitRegistry.FindNearest(p + new Vector2(0, -0.1f), 0.8f, IsSeen);
            if (unit != null) return unit;
            // A last sighting left under the fog (P4-23); the real thing always wins over it.
            if (Services.TryGet<FogGhostService>(out var ghosts))
            {
                var ghost = ghosts.PickAt(p, 0.8f);
                if (ghost != null) return ghost;
            }
            var eco = Services.Get<EcosystemManager>();
            var nest = eco.Nests.Where(n => n.Discovered && Vector2.Distance(n.WorldPosition, p) < 1.2f)
                .OrderBy(n => Vector2.Distance(n.WorldPosition, p)).FirstOrDefault();
            if (nest != null) return nest;
            // A place worth going to the guild knows of (P4-105).
            if (Services.TryGet<Places.PlaceService>(out var places) && places.PickAt(p, 1.2f) is { } place) return place;
            if (Services.TryGet<GateService>(out var gates) && gates.GateAt(p) is TownGate gate) return gate;
            var cell = World.GridMap.WorldToCell(p);
            // Another village the guild has seen (P4-84): its icon and a little round it.
            if (Services.TryGet<Villages.VillageService>(out var villages))
            {
                var village = villages.Known.FirstOrDefault(v => Vector2.Distance(v.WorldPosition, p) <= Villages.VillageRules.Size * 0.5f + 0.5f);
                if (village != null) return village;
            }
            // A ruin (P4-85): its way down once the guild has seen the ground there, or its steps up from inside.
            if (Services.TryGet<World.WorldService>(out var world) && Services.TryGet<FogOfWar>(out var fog))
            {
                var ruin = world.Map.Ruins.FirstOrDefault(r =>
                    fog.IsExplored(r.Entrance) && Vector2.Distance(World.GridMap.CellToWorld(r.Entrance), p) <= 1.2f
                    || fog.IsExplored(r.Inside) && Vector2.Distance(World.GridMap.CellToWorld(r.Inside), p) <= 1.2f);
                if (ruin != null) return ruin;
            }
            return Services.Get<TownService>().Buildings.FirstOrDefault(b => b.Footprint.Contains(cell));
        }

        void UpdateRing()
        {
            Vector2 pos;
            float scale = 1f;
            switch (_selected)
            {
                case Unit u: pos = u.Position + new Vector2(0, -0.28f); if (u is Monster m) scale = m.Scale; break;
                case Nest n: pos = n.WorldPosition; scale = 1.4f; break;
                case FogGhost g: pos = g.Position + new Vector2(0, -0.28f); break;
                case TownGate gate: pos = gate.Center; scale = 2.4f; break;
                case Villages.Village v: pos = v.WorldPosition + new Vector2(0f, -1.2f); scale = 3f; break;
                case World.RuinSite r: pos = RuinSpotInView(r); scale = 2f; break;
                case World.PlaceSite pl: pos = pl.WorldPosition + new Vector2(0f, -0.4f); scale = 1.8f; break;
                default: _ring.gameObject.SetActive(false); return;
            }
            _ring.gameObject.SetActive(true);
            _ring.transform.position = pos;
            _ring.transform.localScale = new Vector3(1.2f * scale, 0.6f * scale, 1f);
        }

        void RefreshText()
        {
            if (_selected is not Adventurer) _member.Hide();
            switch (_selected)
            {
                case Adventurer a: ShowAdventurer(a); break;
                case Monster m: ShowMonster(m); break;
                case Nest n: ShowNest(n); break;
                case Building b: ShowBuilding(b); break;
                case TownGate g: ShowGate(g); break;
                case Villages.Village v: ShowVillage(v); break;
                case Merchant m: ShowMerchant(m); break;
                case Social.Resident r: ShowResident(r); break;
                case Incidents.Traveller t: ShowTraveller(t); break;
                case World.RuinSite r: ShowRuin(r); break;
                case World.PlaceSite pl: ShowPlace(pl); break;
                case FogGhost g: ShowGhost(g); break;
                case PlayerAvatar: break;
            }
            // Tall enough for what it says, the buttons in the corner kept clear (P4-104c: a member's card grew by its mood).
            float height = Mathf.Max(MinHeight, BodyTop + (_member.Visible ? _member.Height : _body.preferredHeight) + ButtonRow);
            if (Mathf.Abs(_panel.sizeDelta.y - height) > 0.5f) _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, height);
        }

        const float MinHeight = 200f, BodyTop = 38f, ButtonRow = 46f; // 292 → 200 with the short member card (2026-11-12)

        /// <summary>A last sighting under the fog (P4-23): only what was seen, never the present state.</summary>
        void ShowGhost(FogGhost g)
        {
            var ghosts = Services.Get<FogGhostService>();
            _title.text = UIBuilder.Colored("마지막 목격", UITheme.TextMuted) + "  " + g.Label;
            string hp = g.HpRatio >= 0f ? $" · 체력 약 {Mathf.RoundToInt(g.HpRatio * 100f / 5f) * 5}%" : "";
            _body.text =
                $"{ghosts.AgoText(g.SeenAtHour)}에 이 자리에서 봤습니다{hp}\n" +
                Muted("지금 무엇이 있는지는 알 수 없습니다. 다시 가서 보면 이 표시는 사라집니다.");
        }

        void ShowMerchant(Merchant m)
        {
            _title.text = $"행상인  {Muted("(외부 상인)")}";
            _body.text =
                $"상태: {UIBuilder.Colored(m.StateLabel, UITheme.Accent)}\n" +
                Muted("창고의 재료·물약·장비를 사 갑니다. [행상인 (T)] 창에서 팔 수 있습니다.");
        }

        /// <summary>A townsperson (user request 2026-10-07): who they are and what they think of the guild.</summary>
        void ShowResident(Social.Resident r)
        {
            var p = r.Profile;
            var opinionColor = p.GuildOpinion >= Social.ResidentProfile.GoodOpinion ? UITheme.Good
                : p.GuildOpinion <= -Social.ResidentProfile.GoodOpinion ? UITheme.Bad : UITheme.TextMuted;
            _title.text = $"{p.Name}  {Muted($"{p.JobLabel} (주민)")}";
            // P4-134: the standing favour of its trade, and its request.
            int tier = Social.ResidentRequestRules.Tier(p.GuildOpinion);
            string favour = tier > 0 ? UIBuilder.Colored(Social.ResidentRequestRules.FavourText(p.Job, tier), UITheme.Good) : Muted("없음");
            string request = "";
            if (Services.TryGet<Social.ResidentRequests>(out var requests) && requests.ActiveOf(p.Id) is { } q)
                request = "\n" + (q.IsOpen
                    ? UIBuilder.Colored($"부탁: {requests.WhatText(q)} ({requests.HoursLeft(q):0}시간 남음) — 눌러서 들어주기", UITheme.Warn)
                    : UIBuilder.Colored($"부탁: {requests.WhatText(q)} — {q.MemberName}이(가) 하는 중", UITheme.Good));
            _body.text =
                $"상태: {UIBuilder.Colored(r.StateLabel, UITheme.Accent)}\n" +
                $"성격 {p.Mbti} · 가치관 {p.Values}\n" +
                $"길드에 대한 생각: {UIBuilder.Colored(p.OpinionText, opinionColor)} ({(p.GuildOpinion > 0 ? "+" : "")}{p.GuildOpinion})\n" +
                $"단골 혜택: {favour}" + request + "\n" +
                Muted("방문자에게 길드 이야기를 전합니다. 길드를 좋게 보면 영입을 권하기도 합니다.");
        }

        /// <summary>A traveller on the road (P4-85 rescue): only what can be seen of it.</summary>
        void ShowTraveller(Incidents.Traveller t)
        {
            _title.text = $"{t.DisplayName}  {Muted("(길손)")}";
            string state = t.Current == Incidents.Traveller.Phase.Crouching
                ? UIBuilder.Colored(t.StateLabel, UITheme.Bad) : UIBuilder.Colored(t.StateLabel, UITheme.Accent);
            _body.text =
                $"상태: {state} · 체력 {Mathf.RoundToInt(t.HpRatio * 100f)}%\n" +
                Muted(t.Current == Incidents.Traveller.Phase.Crouching
                    ? "수레 옆에 웅크려 버티고 있습니다. 둘러싼 몬스터를 모두 쓰러뜨리면 다시 길을 떠납니다."
                    : $"{Quests.QuestBoard.DescribeArea(t.Destination)}(으)로 가는 길입니다.");
        }

        static string Muted(string s) => UIBuilder.Colored(s, UITheme.TextMuted);

        /// <summary>Birth grade tag: muted for common, coloured for rare/heroic.</summary>
        public static string GradeTag(AdventurerGrade g) => g switch
        {
            AdventurerGrade.Rare => UIBuilder.Colored("[희귀]", UITheme.Exp),
            AdventurerGrade.Heroic => UIBuilder.Colored("[영웅]", UITheme.Accent),
            _ => UIBuilder.Colored("[일반]", UITheme.TextMuted),
        };

        /// <summary>
        /// A member or visitor: the title here, the rest on <see cref="MemberCard"/> (2026-11-13: portrait, gauges, tags;
        /// the 11-12 short text card before it). Stats, purse, gear, record, memories, relations and loot are in [상세].
        /// </summary>
        void ShowAdventurer(Adventurer a)
        {
            var d = a.Data;
            _title.text = $"{d.name}  {Muted($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}")} {GradeTag(d.grade)}" + (a.IsVisitor ? "  " + UIBuilder.Colored("(방문자)", UITheme.Warn) : "");
            _body.text = "";
            _member.Bind(a);
        }

        void ShowMonster(Monster m)
        {
            var d = m.Def;
            _title.text = $"{(m.IsNemesis ? UIBuilder.Colored("[원수] ", UITheme.Bad) : m.IsRegionLord ? UIBuilder.Colored("[지대 군주] ", UITheme.Accent) : m.IsBoss ? UIBuilder.Colored("[보스] ", UITheme.Bad) : "")}{m.DisplayName}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(m.Danger), UITheme.Danger(m.Danger))}";
            string prey = d.prey.Count == 0 ? "-" : string.Join(", ", d.prey.Select(p => p.displayName));
            string rivals = d.rivals.Count == 0 ? "-" : string.Join(", ", d.rivals.Select(p => p.displayName));
            _body.text =
                NemesisText(m) +
                $"상태: {UIBuilder.Colored(m.StateLabel, UITheme.Accent)} · 소속: {HomeName(m)}\n" +
                $"HP {m.Hp:0}/{m.Stats.MaxHp:0} · 공격 {m.Stats.Attack:0.#} · 방어 {m.Stats.Defense:0.#}\n" +
                $"성향: {(d.aggressiveToHumans ? UIBuilder.Colored("사람을 공격함", UITheme.Bad) : "먼저 공격하지 않음")} · 전투 {CombatTiers.Label(m.Tier)}\n" +
                $"먹이: {prey} · 적대: {rivals}\n" +
                ResearchText(d) +
                GearText(m) +
                $"{Muted("전리품: " + string.Join(", ", d.loot.Where(l => l.item).Select(l => $"{l.item.displayName}({l.chance * 100:0}%)")))}";
        }

        /// <summary>A nemesis's story (P4-109): its kind, what it did to the guild and when, its bounty. Empty for any other monster.</summary>
        static string NemesisText(Monster m)
        {
            if (!m.IsNemesis || !Services.TryGet<Nemeses.NemesisService>(out var nemeses) || nemeses.Of(m) is not { } n) return "";
            return $"{UIBuilder.Colored("원수", UITheme.Bad)} {Muted($"({m.Def.displayName})")}: {nemeses.Story(n)}\n";
        }

        /// <summary>The bestiary's line (P4-107): the research level, from 2 its weakness and strength, at 3 the mastery.</summary>
        static string ResearchText(Data.MonsterDefinition d)
        {
            int level = Bestiary.BestiaryService.LevelOf(d);
            if (level <= 0) return "";
            string more = level >= 2 ? " · " + Bestiary.BestiaryText.Matchup(d) : Muted(" · 2단계부터 약점·강함");
            if (level >= Bestiary.BestiaryRules.MaxLevel) more += UIBuilder.Colored(" · 숙련", UITheme.Good);
            return $"도감: 연구 {level}단계{more}\n";
        }

        /// <summary>
        /// The monster's nest as the guild knows it (GuildIntel): undiscovered nests stay unnamed, and
        /// a nest taken over out of sight keeps the owners last seen there.
        /// </summary>
        static string HomeName(Monster m)
        {
            if (m.Home == null) return "-";
            if (!m.Home.Discovered) return "알 수 없는 둥지";
            return Services.TryGet<GuildIntel>(out var intel) ? intel.NameOf(m.Home) : m.Home.DisplayName;
        }

        /// <summary>Worn items and swallowed HP (P4-21); empty when it has none.</summary>
        static string GearText(Monster m)
        {
            var worn = m.Equipment.Worn.Select(i => i.IsUnique ? UIBuilder.Colored(i.displayName, UITheme.Accent) : i.displayName).ToList();
            var parts = new System.Collections.Generic.List<string>();
            if (worn.Count > 0) parts.Add("착용: " + string.Join(", ", worn));
            if (m.Equipment.AbsorbedHp > 0f) parts.Add($"삼킨 아이템으로 HP +{m.Equipment.AbsorbedHp:0}");
            if (m.Equipment.Has(MonsterEquipment.PackLeaderEffect)) parts.Add(UIBuilder.Colored("무리 우두머리(주변 무리 공격 +10%)", UITheme.Accent));
            return parts.Count == 0 ? "" : string.Join(" · ", parts) + "\n";
        }

        void ShowNest(Nest n)
        {
            // Observations only (GuildIntel): live count while in view, otherwise the last sighting.
            var intel = Services.Get<GuildIntel>();
            // Owners can change out of sight: name, capacity and totals follow the last seen species.
            var species = intel.SpeciesOf(n);
            _title.text = intel.NameOf(n);
            if (species == null)
            {
                // The den was found but nobody living in it has been seen yet.
                _body.text =
                    $"개체 수: {intel.DescribeNest(n)} · 지형 {n.Biome}\n" +
                    Muted("아직 주인을 보지 못했습니다. 둥지에 사는 개체가 시야에 들어와야 어떤 종인지 알 수 있습니다.");
                return;
            }
            _body.text =
                $"개체 수: {intel.DescribeNest(n)} · 최대 {species.nestCapacity} · 지형 {n.Biome}\n" +
                $"이 종의 발견한 둥지 {intel.KnownNestCount(species)}곳 · 관측 합계 {intel.ObservedPopulation(species)}마리\n" +
                NestGearText(intel, n) +
                Muted("시야에 들어와야 개체 수와 주인이 갱신됩니다. 둥지가 비거나 다른 종에게 밀려나면 다른 무리가 자리잡을 수 있습니다.");
        }

        /// <summary>Gear the guild observed on this nest's monsters (P4-22); empty when none.</summary>
        static string NestGearText(GuildIntel intel, Nest n)
        {
            var gear = intel.GearAt(n);
            if (!gear.Any) return "";
            string line = intel.DescribeGear(gear);
            if (GuildIntel.RaisesDanger(gear)) line += " · 위험 +1";
            return UIBuilder.Colored(line, gear.HasCrown ? UITheme.Accent : UITheme.Warn) + "\n";
        }

        /// <summary>A town gate (P4-39): its state and what a repair would cost.</summary>
        void ShowGate(TownGate g)
        {
            var gates = Services.Get<GateService>();
            string state = g.IsBroken
                ? UIBuilder.Colored("부서짐 — 몬스터가 드나들 수 있습니다", UITheme.Bad)
                : g.Hp < g.MaxHp ? UIBuilder.Colored("손상됨", UITheme.Warn) : UIBuilder.Colored("온전함", UITheme.Good);
            _title.text = $"{g.DisplayName}  {Muted("성문")}";
            _body.text = $"내구도 {g.Hp:0}/{g.MaxHp:0} · {state}\n" +
                         Muted("길드원과 손님은 자유롭게 드나들고, 몬스터는 성문을 부숴야 들어옵니다. 성벽은 부서지지 않습니다.") + "\n" +
                         Muted($"수리비: 손상 1당 {gates.RepairGoldPerHp:0.#}G");
            RefreshRepairButton();
        }

        /// <summary>
        /// Another village (P4-84): what the guild saw of it, its quests on the board, and its candidates — only when the
        /// guild master stands there (they are met in person; escorts may come along).
        /// </summary>
        void ShowVillage(Villages.Village v)
        {
            var villages = Services.Get<Villages.VillageService>();
            bool visible = villages.IsVisible(v);
            string where = $"{Monsters.RegionLords.RegionName(v.Region)} · {(v.Inner ? "안쪽 땅" : "바깥 땅")} · {QuestBoard.DescribeArea(v.WorldPosition)}";
            _title.text = $"{v.DisplayName}  {Muted("다른 마을")}";
            string state;
            if (visible ? v.IsDestroyed : v.SeenRuined) state = UIBuilder.Colored("무너졌습니다 — 다시 세워지지 않습니다", UITheme.Bad);
            else if (visible)
            {
                bool attacked = Services.TryGet<Villages.VillageRaids>(out var raids) && raids.IsUnderAttack(v);
                state = $"내구도 {v.Hp:0}/{v.MaxHp:0}" + (attacked ? "  " + UIBuilder.Colored("습격받는 중!", UITheme.Bad) : "");
            }
            else
            {
                float ago = Services.Get<GameClock>().TotalHours - v.SeenAtHour;
                state = Muted($"{(ago < 1f ? "방금" : $"{ago:0}시간 전")} 봤을 때 내구도 약 {Mathf.RoundToInt(v.SeenHpRatio * 20f) * 5}%");
            }
            int quests = Services.TryGet<QuestBoard>(out var board) ? board.Quests.Count(q => q.IsActive && q.Village == v) : 0;
            var out_ = villages.CandidatesOut(v).ToList();
            string candidates = (visible ? v.IsDestroyed : v.SeenRuined) ? ""
                : out_.Count > 0
                    ? "영입 후보: " + string.Join(", ", out_.Select(a => $"{a.Data.name}({AdventurerData.ClassLabel(a.Data.cls)} Lv.{a.Data.level})"))
                      + "\n" + Muted("후보를 눌러 영입 대화를 하세요. 영입하면 혼자 우리 마을로 걸어옵니다.")
                    : Muted("영입 후보는 길드 마스터가 직접 가야 만날 수 있습니다. (호위를 데려갈 수 있습니다)");
            _body.text = $"{where}\n{state}" + (quests > 0 ? $" · 이 마을의 의뢰 {quests}건" : "") + "\n" + candidates;
            RefreshRepairButton();
        }

        /// <summary>The camera looks into the ruin's dungeon now (P4-85).</summary>
        static bool CameraIsBelow(World.RuinSite r)
            => Services.TryGet<CameraController>(out var cam) && Services.TryGet<World.WorldService>(out var world)
               && world.Map.RuinAt(World.GridMap.WorldToCell(cam.transform.position)) == r;

        /// <summary>Where the ruin is in the camera's view: its steps up from inside, else its way down.</summary>
        static Vector2 RuinSpotInView(World.RuinSite r)
            => World.GridMap.CellToWorld(CameraIsBelow(r) ? r.Inside : r.Entrance);

        /// <summary>A ruin (P4-85): what the guild knows of it — word of it, its quest, whether its treasure is taken.</summary>
        void ShowRuin(World.RuinSite r)
        {
            var incidents = Services.Get<Incidents.IncidentService>();
            var talk = incidents.ActiveAt(r);
            bool looted = incidents.IsLooted(r.Index);
            string name = talk != null && (talk.Rumoured || talk.Sighted) || looted ? Incidents.IncidentRules.RuinName(r) : "오래된 유적";
            _title.text = $"{name}  {Muted("(유적)")}";
            string state = looted ? "보물은 이미 가져갔습니다."
                : talk is { Rumoured: true } ? $"탐사 의뢰: {talk.Quest?.Title ?? "-"} · {Incidents.IncidentRules.RuinProgress(talk)}"
                : "아직 알려진 이야기가 없습니다.";
            _body.text = $"{Quests.QuestBoard.DescribeArea(World.GridMap.CellToWorld(r.Entrance))}\n{state}\n" +
                         Muted("땅 아래로 이어지는 입구입니다. 안은 길드원이 들어가야 보입니다.");
            RefreshRepairButton();
        }

        /// <summary>A place worth going to (P4-105): what it is, what it does, and how it stands now.</summary>
        void ShowPlace(World.PlaceSite p)
        {
            _title.text = $"{p.Name()}  {Muted("(장소)")}";
            var places = Services.Get<Places.PlaceService>();
            string state = p.Kind switch
            {
                World.PlaceKind.OreVein or World.PlaceKind.HerbPatch when !places.IsReady(p)
                    => $"다시 {(p.Kind == World.PlaceKind.OreVein ? "캘" : "딸")} 수 있기까지 약 {Mathf.CeilToInt(places.HoursUntilReady(p))}시간",
                World.PlaceKind.OreVein => "캘 수 있습니다.",
                World.PlaceKind.HerbPatch => "약초가 자라 있습니다.",
                World.PlaceKind.WatchRuin => places.IsClimbed(p) ? "이미 올라가 둘레를 살폈습니다." : "아직 아무도 올라가 보지 않았습니다.",
                _ => "",
            };
            _body.text = $"{Quests.QuestBoard.DescribeArea(p.WorldPosition)}" + (state.Length > 0 ? $"\n{state}" : "") + $"\n{Muted(Places.PlaceRules.Describe(p.Kind))}";
            RefreshRepairButton();
        }

        void RefreshRepairButton()
        {
            if (_selected is World.RuinSite ruin)
            {
                // The ruin's corner: look down into its halls, or back up from them.
                _repairButton.gameObject.SetActive(true);
                _repairButton.interactable = true;
                _repairButton.SetLabel(CameraIsBelow(ruin) ? "지상으로" : "내부 보기");
                return;
            }
            if (_selected is Villages.Village village)
            {
                // The village corner holds "go there": the guild master walks to its doorstep.
                _repairButton.gameObject.SetActive(true);
                bool here = Services.TryGet<PlayerAvatar>(out var master) && Vector2.Distance(master.Position, village.WorldPosition) <= Villages.VillageRules.MeetRadius;
                _repairButton.interactable = !here && master != null && master.IsAlive;
                _repairButton.SetLabel(here ? "마을에 와 있음" : "길드 마스터 보내기");
                return;
            }
            var gate = _selected as TownGate;
            _repairButton.gameObject.SetActive(gate != null);
            if (gate == null || !Services.TryGet<GateService>(out var gates)) return;
            var reason = gates.CannotRepairReason(gate);
            _repairButton.interactable = reason == null;
            _repairButton.SetLabel(reason == null ? $"수리 ({gates.RepairCost(gate)}G)"
                : reason == "손상이 없습니다" ? "수리 필요 없음"
                : reason == "골드가 부족합니다" ? $"골드 부족 ({gates.RepairCost(gate)}G)" : "적이 가까이 있음");
        }

        void OnRepairClicked()
        {
            if (_selected is Villages.Village village && Services.TryGet<PlayerAvatar>(out var master))
                master.MoveTo(village.Doorstep, null, 0.6f);
            if (_selected is TownGate gate && Services.TryGet<GateService>(out var gates)) gates.TryRepair(gate);
            if (_selected is World.RuinSite ruin && Services.TryGet<CameraController>(out var cam))
            {
                bool below = CameraIsBelow(ruin);
                cam.Follow(null);
                cam.JumpTo(World.GridMap.CellToWorld(below ? ruin.Entrance : ruin.Inside));
            }
            RefreshText();
            RefreshRepairButton();
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
