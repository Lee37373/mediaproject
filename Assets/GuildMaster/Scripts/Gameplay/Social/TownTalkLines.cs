using GuildMaster.Core;

namespace GuildMaster.Social
{
    /// <summary>Town talks when there is no model to write them: a few fixed four-line exchanges per kind, with how each ends.</summary>
    public static class TownTalkLines
    {
        public readonly struct Script
        {
            public readonly string[] Lines;
            public readonly TalkOutcome Outcome;
            public Script(TalkOutcome outcome, params string[] lines) { Outcome = outcome; Lines = lines; }
        }

        static readonly Script[] Chat =
        {
            new(TalkOutcome.Warm, "오늘은 좀 한가하네.", "그러게. 이런 날도 있어야지.", "저녁에 여관에서 한잔할래?", "좋지. 네가 사는 거지?"),
            new(TalkOutcome.Neutral, "요즘 의뢰 보수 어때?", "그럭저럭. 물약값이 또 올랐더라.", "다들 그 얘기뿐이네.", "어쩌겠어, 버텨야지."),
            new(TalkOutcome.Warm, "어제 숲에서 이상한 소리 들었어.", "고블린이겠지. 조심해.", "넌 무섭지도 않냐?", "무섭지. 그래도 같이 가면 괜찮아."),
        };

        static readonly Script[] Comfort =
        {
            new(TalkOutcome.Warm, "요즘 얼굴이 많이 안 좋아 보여. 괜찮아?", "...티 났어? 좀 힘들긴 해.", "혼자 끙끙대지 마. 나라도 들어 줄게.", "고마워. 말하고 나니까 좀 낫다."),
            new(TalkOutcome.Neutral, "무슨 일 있어? 계속 한숨만 쉬네.", "아무것도 아니야. 신경 쓰지 마.", "그래도 힘들면 꼭 말해.", "...응, 알았어."),
        };

        static readonly Script[] Quarrel =
        {
            new(TalkOutcome.Fight, "지난번에 네가 멋대로 뛰어드는 바람에 다 망칠 뻔했잖아.", "뭐? 네가 늦게 따라온 거겠지.", "말 한번 편하게 하네.", "됐어, 너랑은 말이 안 통해."),
            new(TalkOutcome.Tense, "몫 나눌 때 좀 이상하지 않았어?", "공평하게 나눴는데 뭐가.", "공평? 글쎄다.", "불만 있으면 길드 마스터한테 말하든가."),
        };

        static readonly Script[] MakeUp =
        {
            new(TalkOutcome.Reconciled, "저기... 그때는 내가 좀 심했어.", "...나도 말이 지나쳤지.", "다음엔 서로 등 맡기자.", "그래. 없던 일로 하자."),
            new(TalkOutcome.Tense, "그때 일, 아직 화났어?", "화가 안 났을 리가 있어?", "미안하다니까.", "...시간이 좀 필요해."),
        };

        static readonly Script[] Welcome =
        {
            new(TalkOutcome.Warm, "이 길드 어때요? 들어올까 고민 중이라.", "괜찮아요. 마스터가 사람을 잘 챙겨요.", "일은 힘들지 않아요?", "힘들죠. 그래도 혼자보단 훨씬 나아요."),
            new(TalkOutcome.Neutral, "여기 길드원이세요?", "네. 둘러보는 중이세요?", "보수는 좀 되나요?", "하기 나름이죠. 쉽진 않아요."),
        };

        static readonly Script[] Trade =
        {
            new(TalkOutcome.Warm, "좋은 물건 들어왔어요? 구경 좀 할게요.", "그럼요! 오늘은 특별히 싸게 드릴게요.", "지난번에도 그 말 했잖아요.", "하하, 이번엔 진짜라니까요."),
            new(TalkOutcome.Neutral, "오는 길은 괜찮았어요?", "숲길에 늑대가 늘어서 돌아왔어요.", "조심하셔야겠네요.", "그러게요. 호위라도 붙여야 하나."),
        };

        static readonly Script[] Strangers =
        {
            new(TalkOutcome.Neutral, "여기 길드 들어가 보려고요?", "고민 중이에요. 그쪽은요?", "저도 둘러보는 중이에요.", "괜찮은 데면 좋겠네요."),
            new(TalkOutcome.Warm, "이 마을은 처음이세요?", "네, 생각보다 활기차네요.", "길드가 있어서 그런가 봐요.", "그러게요. 한번 알아봐야겠어요."),
        };

        /// <summary>What A has against B in a quarrel ({0} = B's name with the subject particle).</summary>
        static readonly string[] Grievances =
        {
            "지난 의뢰에서 {0} 뒤를 제대로 봐주지 않아 크게 다칠 뻔했다",
            "{0} 몫을 나눌 때 자기 몫만 두둑하게 챙겼다",
            "{0} 늘 잘난 척하며 가르치려 든다",
            "{0} 어젯밤 여관에서 늦게까지 시끄럽게 굴어 한숨도 못 잤다",
            "{0} 길드 일은 대충 하고 편한 일만 골라 한다",
            "{0} 내 뒷말을 하고 다닌다는 소문을 들었다",
        };

        public static string Grievance(string nameOfBWithParticle, GameRandom rng)
            => string.Format(Grievances[rng.Range(0, Grievances.Length)], nameOfBWithParticle);

        /// <summary>"…잃었다" → "…잃었대." (reported speech).</summary>
        public static string Hearsay(string rumor)
        {
            string r = rumor.TrimEnd('.', ' ');
            return r.EndsWith("다") ? r.Substring(0, r.Length - 1) + "대." : r + "래.";
        }

        // ---- Residents and recruiting in town (user request 2026-10-07) ----

        static readonly Script[] InviteJoined =
        {
            new(TalkOutcome.Joined, "어때요, 우리 길드에 같이 하지 않을래요?", "안 그래도 고민 중이었어요.", "그럼 결정한 거죠? 환영해요!", "좋아요, 들어갈게요. 잘 부탁해요!"),
            new(TalkOutcome.Joined, "여기서 같이 일해 보는 거 어때요?", "보수랑 동료만 괜찮다면요.", "그건 제가 장담할게요.", "그럼 오늘부터 신세 좀 질게요."),
        };
        static readonly Script[] InviteLater =
        {
            new(TalkOutcome.Neutral, "우리 길드에 들어오는 거 생각해 봤어요?", "끌리긴 하는데 아직은요.", "천천히 생각해 봐요. 기다릴게요.", "네, 며칠만 더 둘러볼게요."),
            new(TalkOutcome.Neutral, "같이 하면 든든할 것 같은데요.", "말은 고맙지만 아직 확신이 없어요.", "궁금한 거 있으면 언제든 물어봐요.", "그럴게요. 조금만 더 생각해 볼게요."),
        };
        static readonly Script[] InviteDeclined =
        {
            new(TalkOutcome.Declined, "우리 길드 들어올 생각 없어요?", "음... 지금은 별로요.", "왜요, 뭐가 마음에 걸려요?", "그냥 저랑은 안 맞는 것 같아요."),
        };
        static readonly Script[] ResidentWelcomeGood =
        {
            new(TalkOutcome.Warm, "길드 들어가려고요? 거기 사람들 괜찮아요.", "그래요? 마을 분이 그러시니 믿음이 가네요.", "몬스터도 잘 막아 주고 인사성도 밝아요.", "한번 제대로 알아봐야겠어요."),
        };
        static readonly Script[] ResidentWelcomeBad =
        {
            new(TalkOutcome.Tense, "길드요? 거기 요즘 시끄럽던데.", "무슨 일 있었어요?", "다치고 싸우고, 좋은 소리는 못 들었어요.", "흠... 좀 더 지켜봐야겠네요."),
        };
        static readonly Script[] ResidentWelcomeMeh =
        {
            new(TalkOutcome.Neutral, "처음 보는 얼굴이네요. 길드 보러 왔어요?", "네, 어떤 곳인지 궁금해서요.", "나쁘진 않아요. 직접 얘기해 봐요.", "그래야겠네요. 고마워요."),
        };
        static readonly Script[] ResidentChat =
        {
            new(TalkOutcome.Warm, "오늘 장사는 좀 어때요?", "그럭저럭이에요. 날이 좋아서 다행이죠.", "저녁에 빵 좀 남으면 챙겨 둘게요.", "어머, 고마워라."),
            new(TalkOutcome.Neutral, "밤새 성 밖이 시끄럽던데요.", "몬스터들이 또 설쳤나 봐요.", "길드 사람들이 있어서 그나마 낫죠.", "그러게요. 조심해야죠."),
        };
        static readonly System.Collections.Generic.Dictionary<ResidentJob, Script[]> ResidentToMember = new()
        {
            [ResidentJob.Blacksmith] = new[] { new Script(TalkOutcome.Warm, "칼날이 다 나갔군. 갖고 와, 손봐 주지.", "역시 보는 눈이 있으시네요.", "쇠는 정직하거든. 막 쓰지 마.", "명심할게요. 다음에 들를게요.") },
            [ResidentJob.Herbalist] = new[] { new Script(TalkOutcome.Warm, "어머, 팔에 상처가 났네요. 괜찮아요?", "별거 아니에요. 금방 나아요.", "그래도 이 연고 발라요. 덧나면 큰일이에요.", "고마워요. 마리 씨 덕분에 살아요.") },
            [ResidentJob.Chief] = new[] { new Script(TalkOutcome.Neutral, "요즘 성 밖은 어떤가? 마을 사람들이 걱정이 많네.", "몬스터가 좀 늘긴 했어요.", "길드가 제 몫을 해 주길 바라네.", "최선을 다하고 있어요, 촌장님.") },
            [ResidentJob.Baker] = new[] { new Script(TalkOutcome.Warm, "어서 와요! 갓 구운 빵 냄새 맡고 왔죠?", "들켰네요. 하나만 주세요.", "모험 얘기 하나 해 주면 덤으로 줄게요!", "하하, 그럼 오늘은 늑대 얘기로 할게요.") },
            [ResidentJob.Guard] = new[] { new Script(TalkOutcome.Neutral, "북문 쪽 이상 없었습니까?", "조용했어요. 발자국도 없었고요.", "알겠습니다. 계속 경계하겠습니다.", "수고 많으세요.") },
            [ResidentJob.Child] = new[] { new Script(TalkOutcome.Warm, "형아, 오늘도 몬스터 잡았어?", "응, 슬라임 두 마리.", "우와! 나도 커서 모험가 될 거야!", "그럼 밥 많이 먹어야 해.") },
        };

        /// <summary>
        /// A fixed exchange for this pair. Invitations follow the visitor's real opinion (<paramref name="invite"/>),
        /// residents talk about their work or put in a word for (or against) the guild.
        /// </summary>
        public static Script Pick(TownTalkKind kind, TalkerKind a, TalkerKind b, ResidentProfile resident, string rumor, GameRandom rng,
                                  TalkOutcome invite = TalkOutcome.Neutral)
        {
            Script[] set = null;
            if (kind == TownTalkKind.Invite)
                set = invite == TalkOutcome.Joined ? InviteJoined : invite == TalkOutcome.Declined ? InviteDeclined : InviteLater;
            else if (resident != null && kind == TownTalkKind.Welcome)
                set = resident.GuildOpinion >= ResidentProfile.GoodOpinion ? ResidentWelcomeGood
                    : resident.GuildOpinion <= -ResidentProfile.GoodOpinion ? ResidentWelcomeBad : ResidentWelcomeMeh;
            else if (resident != null && kind == TownTalkKind.Chat)
            {
                bool withMember = a == TalkerKind.Member || b == TalkerKind.Member;
                set = withMember && ResidentToMember.TryGetValue(resident.Job, out var mine) ? mine : ResidentChat;
            }
            if (set != null) return set[rng.Range(0, set.Length)];
            return Pick(kind, a != TalkerKind.Member && b != TalkerKind.Member, rumor, rng);
        }

        /// <summary>A fixed exchange for this kind of talk. Gossip is written around the rumor itself.</summary>
        public static Script Pick(TownTalkKind kind, bool strangers, string rumor, GameRandom rng)
        {
            if (kind == TownTalkKind.Gossip && !string.IsNullOrEmpty(rumor))
                return new Script(TalkOutcome.Neutral, "그 얘기 들었어? " + Hearsay(rumor), "정말? 처음 듣는데.", "다들 수군대더라.", "허, 그렇구나.");
            var set = kind switch
            {
                TownTalkKind.Comfort => Comfort,
                TownTalkKind.Quarrel => Quarrel,
                TownTalkKind.MakeUp => MakeUp,
                TownTalkKind.Welcome => Welcome,
                TownTalkKind.Trade => Trade,
                TownTalkKind.Invite => InviteLater,
                _ => strangers ? Strangers : Chat,
            };
            return set[rng.Range(0, set.Length)];
        }
    }
}
