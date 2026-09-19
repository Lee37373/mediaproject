using System.Collections.Generic;
using GuildMaster.Traits;
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

            var potion = Item(Potion, "체력 물약", ItemCategory.Potion, 20, new Color(0.9f, 0.2f, 0.25f));
            potion.healAmount = 40f;
            for (int tier = 1; tier <= 3; tier++)
            {
                var g = Item($"gear_t{tier}", $"{tier}등급 장비", ItemCategory.Gear, 60 * tier * tier, new Color(0.7f, 0.75f, 0.85f));
                g.gearTier = tier;
                g.attackBonus = 3f * tier;
                g.defenseBonus = 2f * tier;
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
            var slime = Mon("slime", "슬라임", MonsterShape.Slime, new Color(0.4f, 0.85f, 0.45f), new Color(0.8f, 1f, 0.8f), 0.75f,
                18, 3, 0, 1.0f, 1.2f, 1.6f, 4, DangerLevel.Low, false, 8, 0.10f, 5, 3, Biome.Plains, Biome.Shore, Biome.Forest);
            slime.artKey = "slime";
            Loot(slime, jelly, 0.85f, 1, 2);

            var stoneSlime = Mon("stone_slime", "돌 슬라임", MonsterShape.Slime, new Color(0.55f, 0.58f, 0.55f), new Color(0.8f, 0.82f, 0.8f), 0.9f,
                35, 6, 3, 1.1f, 1.3f, 2.2f, 5, DangerLevel.Low, false, 6, 0.06f, 4, 6, Biome.Plains, Biome.Forest);
            stoneSlime.artKey = "stone_slime";
            stoneSlime.fleesFromPredators = true;
            Loot(stoneSlime, stoneShard, 0.7f);
            Loot(stoneSlime, denseJelly, 0.6f, 1, 2);

            var skull = Mon("skull", "해골", MonsterShape.Humanoid, new Color(0.9f, 0.88f, 0.8f), new Color(0.3f, 0.3f, 0.35f), 0.8f,
                30, 7, 1, 1.1f, 1.0f, 3.4f, 7, DangerLevel.Low, true, 6, 0.045f, 3, 8, Biome.Forest, Biome.Plains);
            skull.artKey = "skull";
            skull.rangedArtKey = "skull_archer";
            Loot(skull, boneShard, 0.7f);
            Loot(skull, oldArrowhead, 0.4f);

            var goblin = Mon("goblin", "고블린", MonsterShape.Humanoid, new Color(0.35f, 0.65f, 0.25f), new Color(0.55f, 0.35f, 0.2f), 0.85f,
                28, 6, 2, 1.1f, 1.1f, 3.0f, 7, DangerLevel.Low, true, 20, 0.05f, 4, 7, Biome.Forest, Biome.Plains);
            goblin.artKey = "goblin";
            goblin.rangedArtKey = "goblin_archer";
            goblin.canRaid = true;
            goblin.raidThreshold = 10; goblin.raidStep = 4; // raid danger at 10 / 14 / 18
            Loot(goblin, goblinEar, 0.8f);
            Loot(goblin, rustyDagger, 0.2f);

            var goblinWarrior = Mon("goblin_warrior", "고블린 전사", MonsterShape.Humanoid, new Color(0.3f, 0.45f, 0.3f), new Color(0.4f, 0.3f, 0.25f), 0.95f,
                70, 11, 5, 1.2f, 1.2f, 2.8f, 7, DangerLevel.Medium, true, 12, 0.03f, 3, 20, Biome.Mountain, Biome.Forest);
            goblinWarrior.artKey = "goblin_warrior";
            goblinWarrior.canRaid = true;
            goblinWarrior.raidThreshold = 6; goblinWarrior.raidStep = 3; // 6 / 9 / 12
            Loot(goblinWarrior, goblinHelm, 0.7f);
            Loot(goblinWarrior, ironOre, 0.35f);

            var slimeKing = Mon("slime_king", "슬라임 킹", MonsterShape.Brute, new Color(0.35f, 0.8f, 0.4f), new Color(0.95f, 0.8f, 0.3f), 1.5f,
                220, 22, 8, 1.5f, 1.8f, 2.2f, 6, DangerLevel.High, true, 4, 0.008f, 1, 80, Biome.Mountain);
            slimeKing.artKey = "slime_king";
            slimeKing.canRaid = true;
            slimeKing.raidThreshold = 2; slimeKing.raidStep = 1; // 2 / 3 / 4
            slimeKing.wanderRadius = 4f;
            Loot(slimeKing, crownShard, 1f);
            Loot(slimeKing, royalJelly, 0.6f, 1, 2);

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

            // Food web / rivalries (monster-vs-monster combat).
            skull.prey.AddRange(new[] { slime, stoneSlime });
            goblinWarrior.prey.Add(stoneSlime);
            slimeKing.prey.AddRange(new[] { stoneSlime, skull });
            goblin.rivals.Add(goblinWarrior);
            goblinWarrior.rivals.Add(goblin);

            AddTraits(db);
            return db;
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
