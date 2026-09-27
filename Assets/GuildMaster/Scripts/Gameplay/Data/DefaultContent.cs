using System.Collections.Generic;
using GuildMaster.Traits;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Data
{
    /// <summary>
    /// Built-in default content. The editor menu "GuildMaster/Generate Data Assets" saves
    /// these as ScriptableObject assets so designers can tune them.
    /// </summary>
    public static class DefaultContent
    {
        public const string Potion = "potion_hp";

        public static GameDatabase Build()
        {
            var db = ScriptableObject.CreateInstance<GameDatabase>();
            db.name = "GameDatabase(Default)";

            // ---------- Items ----------
            var items = new Dictionary<string, ItemDefinition>();
            ItemDefinition Item(string id, string name, ItemCategory cat, int value, Color color)
            {
                var i = ScriptableObject.CreateInstance<ItemDefinition>();
                i.name = i.id = id;
                i.displayName = name;
                i.category = cat;
                i.baseValue = value;
                i.color = color;
                items[id] = i;
                db.items.Add(i);
                return i;
            }

            // Loot of the Casual Monsters roster (2026-09-19: species follow the imported art).
            var jelly = Item("slime_jelly", "슬라임 젤", ItemCategory.Material, 4, new Color(0.5f, 0.9f, 0.5f));
            var stoneShard = Item("stone_shard", "돌 슬라임 조각", ItemCategory.Material, 8, new Color(0.6f, 0.62f, 0.6f));
            var denseJelly = Item("dense_jelly", "단단한 젤", ItemCategory.Material, 5, new Color(0.45f, 0.75f, 0.45f));
            var boneShard = Item("bone_shard", "해골 뼛조각", ItemCategory.Material, 10, new Color(0.92f, 0.9f, 0.82f));
            var oldArrowhead = Item("old_arrowhead", "낡은 화살촉", ItemCategory.Material, 7, new Color(0.55f, 0.5f, 0.45f));
            var goblinEar = Item("goblin_ear", "고블린 귀", ItemCategory.Material, 6, new Color(0.4f, 0.7f, 0.3f));
            var rustyDagger = Item("rusty_dagger", "녹슨 단검", ItemCategory.Material, 12, new Color(0.6f, 0.45f, 0.3f));
            var goblinHelm = Item("goblin_helm", "고블린 전사 투구", ItemCategory.Material, 18, new Color(0.65f, 0.65f, 0.7f));
            var ironOre = Item("iron_ore", "철광석", ItemCategory.Material, 15, new Color(0.5f, 0.5f, 0.55f));
            var crownShard = Item("crown_shard", "왕관 조각", ItemCategory.Material, 60, new Color(0.95f, 0.8f, 0.3f));
            var royalJelly = Item("royal_jelly", "왕의 젤", ItemCategory.Material, 35, new Color(0.35f, 0.85f, 0.4f));

            // Monster use (P4-21): what monsters wear when they find these, and the one unique item.
            rustyDagger.equipSlot = EquipSlot.Weapon; rustyDagger.monsterAttackBonus = 2f;
            oldArrowhead.equipSlot = EquipSlot.Weapon; oldArrowhead.monsterAttackBonus = 1.5f;
            goblinHelm.equipSlot = EquipSlot.Armor; goblinHelm.monsterDefenseBonus = 2f; goblinHelm.monsterHpBonus = 8f;
            crownShard.equipSlot = EquipSlot.Trinket; crownShard.monsterHpBonus = 10f; crownShard.uniqueEffect = "pack_leader";

            var potion = Item(Potion, "체력 물약", ItemCategory.Potion, 20, new Color(0.9f, 0.2f, 0.25f));
            potion.healAmount = 40f;
            for (int tier = 1; tier <= 3; tier++)
            {
                var g = Item($"gear_t{tier}", $"{tier}등급 장비", ItemCategory.Gear, 60 * tier * tier, new Color(0.7f, 0.75f, 0.85f));
                g.gearTier = tier;
                g.attackBonus = 3f * tier;
                g.defenseBonus = 2f * tier;
                g.equipSlot = EquipSlot.Weapon;
                g.monsterAttackBonus = 3f * tier;
                g.monsterDefenseBonus = 2f * tier;
            }

            // ---------- Monsters ----------
            MonsterDefinition Mon(string id, string name, MonsterShape shape, Color body, Color accent, float scale,
                float hp, float atk, float def, float range, float cd, float speed, float sight, DangerLevel danger,
                bool aggressive, int cap, float growth, int initial, int exp, params Biome[] biomes)
            {
                var m = ScriptableObject.CreateInstance<MonsterDefinition>();
                m.name = m.id = id;
                m.displayName = name;
                m.shape = shape;
                m.bodyColor = body;
                m.accentColor = accent;
                m.scale = scale;
                m.maxHp = hp; m.attack = atk; m.defense = def; m.attackRange = range; m.attackCooldown = cd;
                m.moveSpeed = speed; m.sightRadius = sight; m.danger = danger;
                m.aggressiveToHumans = aggressive;
                m.nestCapacity = cap; m.growthPerHour = growth; m.initialPopulation = initial;
                m.expValue = exp;
                m.biomes.AddRange(biomes);
                db.monsters.Add(m);
                return m;
            }
            void Loot(MonsterDefinition m, ItemDefinition item, float chance, int min = 1, int max = 1)
                => m.loot.Add(new LootEntry { item = item, chance = chance, min = min, max = max });

            // Species follow the imported Casual Monsters art; roles in the ecosystem are unchanged
            // from the previous roster (prey / skittish prey / pack predator / raiders / boss).
            //
            // P4-58 (user decision 2026-09-22: "strength is the level"): base stats and EXP sit within
            // about ±30% of the goblin's; the level (born by distance from town) carries the strength.
            // The slime king was 220 HP / 22 attack / 160 EXP, the goblin warrior 70 / 11 / 40.
            //
            // growthPerHour was raised about 1.5x over the P5-1 values on 2026-09-20 (P4-37): with
            // free hunting the guild clears the near rings even on an empty board, and at the old
            // rates the species nearest town were farmed down to the recolonisation floor. The far
            // ones (goblin warrior, slime king) go up least, so the walk out stays graded.
            //
            // P4-38/40 (user report 2026-09-21: "levelling is slow, they hardly fight early"): the
            // weak rings (slime, stone slime, skull) got bigger nests, more to start with and faster
            // breeding. A measured day and a half found 20 kills in 35 game hours — the countryside
            // near town was simply empty most of the time.
            //
            // P4-41 (user decision 2026-09-22: "raise spawns by shortening the time, not the count"):
            // nest sizes and starting numbers are back to the pre-P4-40 values and only breeding got
            // faster. From three left, a slime den refills in ~11 h (P4-40: 34 h), stone slime ~14 h
            // from two, skull ~17 h from two.
            var slime = Mon("slime", "슬라임", MonsterShape.Slime, new Color(0.4f, 0.85f, 0.45f), new Color(0.8f, 1f, 0.8f), 0.75f,
                24, 5, 0, 1.0f, 1.2f, 1.6f, 4, DangerLevel.Low, false, 8, 0.30f, 5, 10, Biome.Plains, Biome.Shore, Biome.Forest);
            slime.artKey = "slime";
            Loot(slime, jelly, 0.85f, 1, 2);

            var stoneSlime = Mon("stone_slime", "돌 슬라임", MonsterShape.Slime, new Color(0.55f, 0.58f, 0.55f), new Color(0.8f, 0.82f, 0.8f), 0.9f,
                34, 5, 3, 1.1f, 1.3f, 2.2f, 5, DangerLevel.Low, false, 6, 0.25f, 4, 12, Biome.Plains, Biome.Forest);
            stoneSlime.artKey = "stone_slime";
            stoneSlime.fleesFromPredators = true;
            Loot(stoneSlime, stoneShard, 0.7f);
            Loot(stoneSlime, denseJelly, 0.6f, 1, 2);

            var skull = Mon("skull", "해골", MonsterShape.Humanoid, new Color(0.9f, 0.88f, 0.8f), new Color(0.3f, 0.3f, 0.35f), 0.8f,
                26, 7, 1, 1.1f, 1.0f, 3.4f, 7, DangerLevel.Low, true, 6, 0.20f, 3, 14, Biome.Forest, Biome.Plains);
            skull.artKey = "skull";
            skull.rangedArtKey = "skull_archer";
            Loot(skull, boneShard, 0.7f);
            Loot(skull, oldArrowhead, 0.4f);

            var goblin = Mon("goblin", "고블린", MonsterShape.Humanoid, new Color(0.35f, 0.65f, 0.25f), new Color(0.55f, 0.35f, 0.2f), 0.85f,
                28, 6, 2, 1.1f, 1.1f, 3.0f, 7, DangerLevel.Low, true, 20, 0.048f, 4, 14, Biome.Forest, Biome.Plains);
            goblin.artKey = "goblin";
            goblin.rangedArtKey = "goblin_archer";
            goblin.canRaid = true;
            goblin.raidThreshold = 10; goblin.raidStep = 4; // raid danger at 10 / 14 / 18
            Loot(goblin, goblinEar, 0.8f);
            goblin.startingGear.Add(new LootEntry { item = rustyDagger, chance = 0.2f, min = 1, max = 1 });
            goblin.pickupMode = MonsterPickupMode.Equip;

            var goblinWarrior = Mon("goblin_warrior", "고블린 전사", MonsterShape.Humanoid, new Color(0.3f, 0.45f, 0.3f), new Color(0.4f, 0.3f, 0.25f), 0.95f,
                34, 7.5f, 3, 1.2f, 1.2f, 2.8f, 7, DangerLevel.Medium, true, 12, 0.025f, 2, 16, Biome.Mountain, Biome.Forest);
            goblinWarrior.artKey = "goblin_warrior";
            goblinWarrior.canRaid = true;
            // P5-1: warriors live far out (band 0.75), so their raids are a late-game threat.
            goblinWarrior.raidThreshold = 9; goblinWarrior.raidStep = 3; // 9 / 12 / 15
            goblinWarrior.startingGear.Add(new LootEntry { item = goblinHelm, chance = 0.7f, min = 1, max = 1 });
            goblinWarrior.pickupMode = MonsterPickupMode.Equip;
            Loot(goblinWarrior, ironOre, 0.35f);

            var slimeKing = Mon("slime_king", "슬라임 킹", MonsterShape.Brute, new Color(0.35f, 0.8f, 0.4f), new Color(0.95f, 0.8f, 0.3f), 1.5f,
                38, 8, 3, 1.5f, 1.8f, 2.2f, 6, DangerLevel.High, true, 4, 0.011f, 1, 18, Biome.Mountain);
            slimeKing.artKey = "slime_king";
            slimeKing.canRaid = true;
            slimeKing.raidThreshold = 2; slimeKing.raidStep = 1; // 2 / 3 / 4
            slimeKing.wanderRadius = 4f;
            Loot(slimeKing, crownShard, 1f);
            Loot(slimeKing, royalJelly, 0.6f, 1, 2);
            // The rest pick items up too (P4-21): skeletons wear them, slimes swallow them.
            skull.pickupMode = MonsterPickupMode.Equip;
            slime.pickupMode = MonsterPickupMode.Absorb;
            stoneSlime.pickupMode = MonsterPickupMode.Absorb;
            slimeKing.pickupMode = MonsterPickupMode.Absorb;

            // Habitat (P4-58, user decision 2026-09-22: species live by terrain only; how strong they
            // are is the level, i.e. the distance from town). Every region has at least one kind.
            slime.regions.AddRange(new[] { Region.Grassland, Region.Swamp });
            stoneSlime.regions.AddRange(new[] { Region.Wasteland, Region.Grassland });
            skull.regions.AddRange(new[] { Region.Wasteland, Region.Snow });
            goblin.regions.Add(Region.Grassland);
            goblin.habitatBiomes.Add(Biome.Forest); // "grassland and woods"
            goblinWarrior.regions.AddRange(new[] { Region.Desert, Region.Wasteland });
            slimeKing.regions.Add(Region.Swamp);
            // The near ring must be thick enough to live off: potions are slime jelly, and a new
            // guild has nothing else it can safely hunt (P5-1).
            slime.minNests = 3;
            stoneSlime.minNests = 2;

            // Territories (base radius; the nest's fill scales it 0.6x..1.4x).
            slime.territoryRadius = 3.5f;
            stoneSlime.territoryRadius = 4f;
            skull.territoryRadius = 5f;
            goblin.territoryRadius = 5.5f;
            goblinWarrior.territoryRadius = 6f;
            slimeKing.territoryRadius = 6.5f;

            // Fighting smarts (P4-26): slimes act on instinct, the rest fight like novices. P4-58: the
            // level does the rest (a step up every MonsterLevel.LevelsPerTier levels, a boss one more),
            // so only the edge of the map and its bosses reach the top.
            slime.combatTier = CombatTier.Instinct;
            stoneSlime.combatTier = CombatTier.Instinct;
            skull.combatTier = CombatTier.Novice;
            goblin.combatTier = CombatTier.Novice;
            goblinWarrior.combatTier = CombatTier.Novice;
            slimeKing.combatTier = CombatTier.Novice;

            // Tactics & skills.
            stoneSlime.skillIds.Add("charge");
            skull.packHunter = true;
            skull.skillIds.Add("howl");
            skull.rangedVariantChance = 0.3f;
            skull.rangedVariantRange = 4.5f;
            skull.rangedVariantName = "해골 궁수";
            goblin.packHunter = true;
            goblin.rangedVariantChance = 0.35f;
            goblin.rangedVariantRange = 4.5f;
            goblin.rangedVariantName = "고블린 궁수";
            goblinWarrior.skillIds.Add("charge");
            slimeKing.skillIds.Add("slam");

            // Pack bosses (P4-57): a founded pack may have one (Monsters/MonsterBoss). Art from the
            // Casual Monsters images the species do not use yet.
            slime.bossName = "슬라임 대장"; slime.bossArtKey = "boss_slime";
            stoneSlime.bossName = "바위 슬라임 대장"; stoneSlime.bossArtKey = "boss_stone_slime";
            skull.bossName = "독 해골 기사"; skull.bossArtKey = "boss_skull";
            goblin.bossName = "고블린 족장"; goblin.bossArtKey = "boss_goblin";
            goblinWarrior.bossName = "고블린 대장군"; // no spare warrior image: its own, drawn bigger
            slimeKing.bossName = "슬라임 황제"; slimeKing.bossArtKey = "boss_slime_king";

            // Food web / rivalries (monster-vs-monster combat).
            skull.prey.AddRange(new[] { slime, stoneSlime });
            goblinWarrior.prey.Add(stoneSlime);
            slimeKing.prey.AddRange(new[] { stoneSlime, skull });
            goblin.rivals.Add(goblinWarrior);
            goblinWarrior.rivals.Add(goblin);

            AddTraits(db);
            AddBuildings(db);
            AddRecipes(db);
            return db;
        }

        /// <summary>Building upgrade paths (P4-1). Costs agreed with the user 2026-09-19; tune in P5-1.</summary>
        static void AddBuildings(GameDatabase db)
        {
            static ItemCost I(string id, int n) => new() { itemId = id, count = n };
            BuildingDefinition B(Town.BuildingType type, params BuildingLevel[] levels)
            {
                var b = ScriptableObject.CreateInstance<BuildingDefinition>();
                b.name = "building_" + type;
                b.type = type;
                b.levels.AddRange(levels);
                db.buildings.Add(b);
                return b;
            }

            // P4-69: level 1 is what the game actually starts with (P5-1 set 5 EXP an hour, 90G tax); the old
            // 8 / 60 here never applied, so the town hall's level 2 (90G) was an upgrade that paid nothing.
            // Levels 2 and 3 keep their old steps (user decision 2026-09-27).
            B(Town.BuildingType.GuildHall,
                new BuildingLevel { expPerHour = 5, rosterCapacity = 6, visitorLevelBonus = 0, effectText = "EXP 시간당 5 · 정원 6명" },
                new BuildingLevel { goldCost = 300, itemCosts = { I("bone_shard", 5), I("goblin_ear", 5) }, expPerHour = 11, rosterCapacity = 9, visitorLevelBonus = 1, effectText = "EXP 시간당 11 · 정원 9명 · 방문자 레벨 +1" },
                new BuildingLevel { goldCost = 800, itemCosts = { I("goblin_helm", 4), I("iron_ore", 6) }, expPerHour = 19, rosterCapacity = 12, visitorLevelBonus = 2, effectText = "EXP 시간당 19 · 정원 12명 · 방문자 레벨 +2" });

            B(Town.BuildingType.Blacksmith,
                new BuildingLevel { craftTier = 1, effectText = "1등급 장비 제작" },
                new BuildingLevel { goldCost = 200, itemCosts = { I("iron_ore", 5), I("rusty_dagger", 3) }, craftTier = 2, effectText = "2등급 장비 제작" },
                new BuildingLevel { goldCost = 600, itemCosts = { I("iron_ore", 12), I("goblin_helm", 3) }, craftTier = 3, effectText = "3등급 장비 제작" });

            B(Town.BuildingType.Apothecary,
                new BuildingLevel { craftTier = 1, craftSpeed = 1f, effectText = "물약 제작" },
                new BuildingLevel { goldCost = 150, itemCosts = { I("slime_jelly", 10), I("dense_jelly", 5) }, craftTier = 1, craftSpeed = 1.5f, effectText = "물약 제작 속도 ×1.5" },
                new BuildingLevel { goldCost = 450, itemCosts = { I("royal_jelly", 2), I("slime_jelly", 20) }, craftTier = 1, craftSpeed = 2f, effectText = "물약 제작 속도 ×2" });

            B(Town.BuildingType.TrainingGround,
                new BuildingLevel { idleExpPerHour = 0, effectText = "훈련 없음" },
                new BuildingLevel { goldCost = 200, itemCosts = { I("stone_shard", 6), I("old_arrowhead", 4) }, idleExpPerHour = 3, effectText = "대기 중인 모험가 EXP 시간당 3" },
                new BuildingLevel { goldCost = 550, itemCosts = { I("bone_shard", 12), I("stone_shard", 10) }, idleExpPerHour = 6, effectText = "대기 중인 모험가 EXP 시간당 6" });

            // P5-1: the hall fell in seconds to the first raid, so it is much sturdier now and
            // raiders only land a fraction of their attack on it (GameBalance.monsterBuildingDamageMul).
            B(Town.BuildingType.TownHall,
                new BuildingLevel { maxHp = 1000, dailyTax = 90, effectText = "내구도 1000 · 세금 하루 90G" },
                new BuildingLevel { goldCost = 400, itemCosts = { I("stone_shard", 10), I("iron_ore", 6) }, maxHp = 1500, dailyTax = 120, effectText = "내구도 1500 · 세금 하루 120G" },
                new BuildingLevel { goldCost = 1000, itemCosts = { I("crown_shard", 1), I("iron_ore", 15) }, maxHp = 2100, dailyTax = 160, effectText = "내구도 2100 · 세금 하루 160G" });

            // The watchtower is placed by the player (P4-13), so its one level is the build cost.
            // Fixed price however many are already standing (user decision, 2026-09-20).
            B(Town.BuildingType.Watchtower,
                new BuildingLevel
                {
                    goldCost = 150,
                    itemCosts = { I("stone_shard", 8), I("iron_ore", 4) },
                    maxHp = 220,
                    sightRadius = 14f,
                    footprint = 2,
                    effectText = "주변 14칸을 성벽 너머까지 감시",
                });
        }

        /// <summary>Crafting recipes (P4-2). Agreed with the user 2026-09-19; tune in P5-1.</summary>
        static void AddRecipes(GameDatabase db)
        {
            static ItemCost I(string id, int n) => new() { itemId = id, count = n };
            void R(string id, string output, Town.BuildingType station, int tier, float hours, int gold, params ItemCost[] inputs)
            {
                var r = ScriptableObject.CreateInstance<RecipeDefinition>();
                r.name = r.id = id;
                r.outputItemId = output;
                r.station = station;
                r.requiredTier = tier;
                r.hours = hours;
                r.goldCost = gold;
                r.inputs.AddRange(inputs);
                db.recipes.Add(r);
            }

            R("potion", Potion, Town.BuildingType.Apothecary, 1, 2f, 5, I("slime_jelly", 2));
            // P5-1: tier 1 used to need a rusty dagger, which only 20% of goblins carry — a new guild
            // could not make its first gear for days. It now comes from the near ring's own drops.
            R("gear_t1", "gear_t1", Town.BuildingType.Blacksmith, 1, 4f, 0, I("stone_shard", 3), I("dense_jelly", 2));
            R("gear_t2", "gear_t2", Town.BuildingType.Blacksmith, 2, 6f, 0, I("bone_shard", 4), I("iron_ore", 2));
            R("gear_t3", "gear_t3", Town.BuildingType.Blacksmith, 3, 8f, 0, I("goblin_helm", 3), I("crown_shard", 1));
        }

        static void AddTraits(GameDatabase db)
        {
            TraitDefinition T(string id, string name, bool positive, string desc, string hint)
            {
                var t = ScriptableObject.CreateInstance<TraitDefinition>();
                t.name = t.id = id;
                t.displayName = name;
                t.isPositive = positive;
                t.description = desc;
                t.promptHint = hint;
                db.traits.Add(t);
                return t;
            }

            void Conflict(TraitDefinition a, TraitDefinition b) { a.conflicts.Add(b); b.conflicts.Add(a); }

            // ---------- Positive ----------
            var brave = T("brave", "용감함", true, "늦게 후퇴하고 위험한 의뢰를 선호합니다.", "위험을 두려워하지 않고 강한 적과 싸우길 즐긴다.");
            brave.retreatShift = -0.1f; brave.dangerAversion = -0.5f;
            var sturdy = T("sturdy", "강골", true, "최대 HP +20%.", "몸이 튼튼해 웬만한 상처는 버틴다.");
            sturdy.hpMul = 1.2f;
            var marksman = T("marksman", "명사수", true, "공격력 +15%.", "공격이 정확하고 매섭다.");
            marksman.atkMul = 1.15f;
            var diligent = T("diligent", "근면함", true, "피로 누적 ×0.7.", "지치지 않고 성실하게 일한다.");
            diligent.fatigueMul = 0.7f;
            var learner = T("quick_learner", "빠른 학습자", true, "획득 EXP ×1.3.", "배우는 속도가 빠르고 성장에 욕심이 있다.");
            learner.expMul = 1.3f;
            var nimble = T("nimble", "날렵함", true, "이동 속도 +15%.", "몸놀림이 가볍고 빠르다.");
            nimble.speedMul = 1.15f;
            var hawkeye = T("hawkeye", "매의 눈", true, "시야 +2.", "멀리 있는 것도 놓치지 않는다.");
            hawkeye.sightAdd = 2f;
            var sociable = T("sociable", "사교적", true, "다른 모험가와 함께하는 의뢰를 선호합니다(×1.4).", "동료와 함께하는 것을 좋아한다.");
            sociable.partyAffinity = 1.4f;
            var loyal = T("loyal", "충성스러움", true, "영입하기 쉽습니다.", "한번 믿은 길드에는 끝까지 충실하다.");
            loyal.recruitDifficulty = -0.3f;

            // ---------- Negative ----------
            var coward = T("coward", "겁많음", false, "일찍 후퇴하고 위험한 의뢰를 피합니다.", "겁이 많아 위험한 일은 되도록 피하고 싶어 한다.");
            coward.retreatShift = 0.15f; coward.dangerAversion = 1f;
            var frail = T("frail", "허약함", false, "최대 HP -20%.", "몸이 약해 쉽게 다친다.");
            frail.hpMul = 0.8f;
            var clumsy = T("clumsy", "서투름", false, "공격력 -15%.", "손재주가 없어 공격이 자주 빗나간다.");
            clumsy.atkMul = 0.85f;
            var lazy = T("lazy", "게으름", false, "피로 누적 ×1.4.", "게을러서 금방 쉬고 싶어 한다.");
            lazy.fatigueMul = 1.4f;
            var greedy = T("greedy", "탐욕스러움", false, "골드 보상을 크게 따지고(×1.5) 영입하기 어렵습니다.", "돈이라면 무엇이든 한다. 보상이 적으면 불만이다.");
            greedy.goldGreed = 1.5f; greedy.recruitDifficulty = 0.2f;
            var sluggish = T("sluggish", "느림보", false, "이동 속도 -15%.", "행동이 굼뜨고 느리다.");
            sluggish.speedMul = 0.85f;
            var loner = T("loner", "외톨이", false, "다른 모험가와 함께하는 의뢰를 꺼립니다(×0.5).", "혼자 일하는 것을 선호하고 남과 어울리기 싫어한다.");
            loner.partyAffinity = 0.5f;
            var arrogant = T("arrogant", "오만함", false, "영입하기 어렵습니다.", "자신이 최고라고 믿으며 남을 깔본다.");
            arrogant.recruitDifficulty = 0.3f;

            Conflict(brave, coward);
            Conflict(sturdy, frail);
            Conflict(marksman, clumsy);
            Conflict(diligent, lazy);
            Conflict(nimble, sluggish);
            Conflict(sociable, loner);
        }
    }
}
