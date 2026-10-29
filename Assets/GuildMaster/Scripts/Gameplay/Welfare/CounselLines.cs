using GuildMaster.Core;

namespace GuildMaster.Welfare
{
    /// <summary>
    /// The member's lines in a counselling talk when there is no model to write them (and the opening line, which
    /// is always from here so the window never waits). Kept apart from the logic so it can be reworded in one place.
    /// </summary>
    public static class CounselLines
    {
        /// <summary>First words when called in: open members say what is wrong, guarded ones hold back.</summary>
        public static string Opening(CounselConcern c, bool open, GameRandom rng)
        {
            if (!open)
                return rng.Pick(new[]
                {
                    "부르셨어요? ...무슨 일이세요.",
                    "저 찾으셨어요? 별일 없는데요.",
                    "면담이요? 괜찮은데... 말씀하세요.",
                });
            return c.Kind switch
            {
                CounselConcernKind.Grief => $"마스터... 사실 요즘 {c.Subject} 생각이 자꾸 나요.",
                CounselConcernKind.Neglect => "불러 주셔서 다행이네요. 솔직히 좀 서운했거든요.",
                CounselConcernKind.Fear => "마스터, 솔직히 말하면... 요즘 밖에 나가는 게 무서워요.",
                CounselConcernKind.Failure => "지난번 의뢰 일 때문에 부르신 거죠? 면목이 없네요.",
                CounselConcernKind.Conflict => $"마스터, {c.Subject} 일로 좀 드릴 말씀이 있어요.",
                CounselConcernKind.Overwork => "하아... 부르셨어요? 요즘 좀 많이 지쳐서요.",
                CounselConcernKind.Money => "마스터, 돈 얘기라 좀 그렇지만... 요즘 사정이 빠듯해요.",
                _ => "부르셨어요? 요즘 괜히 마음이 무겁네요.",
            };
        }

        /// <summary>The worry said outright (used once the member opens up).</summary>
        public static string Confession(CounselConcern c) => c.Kind switch
        {
            CounselConcernKind.Grief => $"{c.Subject}이(가) 그렇게 가고 나니까, 다음은 제 차례일까 봐 잠이 안 와요.",
            CounselConcernKind.Neglect => "힘들다고 말씀드렸는데 아무 답이 없으셔서... 제가 필요 없는 사람인가 싶었어요.",
            CounselConcernKind.Fear => "그때 정말 죽는 줄 알았어요. 아직도 손이 떨려요.",
            CounselConcernKind.Failure => "끝까지 못 하고 돌아온 게 계속 마음에 걸려요. 제 실력이 이것밖에 안 되나 싶고요.",
            CounselConcernKind.Conflict => $"{c.Subject}이랑 자꾸 부딪쳐요. 같은 길드에 있는 게 불편할 정도예요.",
            CounselConcernKind.Overwork => "쉬지도 못하고 계속 나가다 보니 몸이 버티질 못하겠어요.",
            CounselConcernKind.Money => "일은 하는데 밥값 내고 나면 남는 게 없어요. 이러다 빚만 늘까 봐요.",
            _ => "뭐 때문인지 저도 잘 모르겠어요. 그냥 다 버거워요.",
        };

        public static string Reply(CounselReaction r, CounselConcern c, bool open, bool touched, GameRandom rng)
        {
            // The first kind word gets the worry out.
            if (open && touched && CounselScoring.IsPositive(r)) return rng.Pick(Touched(r));
            return r switch
            {
                CounselReaction.Empathy => open ? Confession(c) : rng.Pick(new[] { "...그렇게 말해 주시니 조금 낫네요.", "고마워요. 사실 좀 힘들었어요." }),
                CounselReaction.Listen => open ? Confession(c) : rng.Pick(new[] { "음... 어디서부터 말해야 할지 모르겠어요.", "그게... 말하자면 좀 길어요." }),
                CounselReaction.Solution => rng.Pick(new[] { "정말요? 그렇게 해 주시면 한결 나을 것 같아요.", "그런 방법이 있었네요. 고맙습니다." }),
                CounselReaction.Praise => rng.Pick(new[] { "그렇게 봐 주셨다니... 힘이 나네요.", "제가요? 그런 말 들으니 좀 쑥스럽네요." }),
                CounselReaction.LowEffort => rng.Pick(new[] { "...듣고는 계신 거죠?", "네... 뭐, 그렇죠." }),
                CounselReaction.Dismiss => rng.Pick(new[] { "...제가 괜히 말했나 보네요.", "다들 힘든 거 알아요. 그래도 그렇게 말씀하시면 서운하죠." }),
                CounselReaction.Rude => rng.Pick(new[] { "그런 말씀은 너무하시네요.", "...지금 그걸 말이라고 하세요?" }),
                _ => rng.Pick(new[] { "네, 그렇죠.", "음, 그런가요." }),
            };
        }

        static string[] Touched(CounselReaction r) => r switch
        {
            CounselReaction.Solution => new[] { "그렇게만 해 주시면... 정말 한결 나을 것 같아요.", "딱 그게 필요했어요. 고맙습니다, 마스터." },
            CounselReaction.Praise => new[] { "마스터가 그렇게 말해 주시니까 다시 해 볼 힘이 나요.", "그 말 한마디가 제일 듣고 싶었나 봐요." },
            _ => new[] { "...맞아요. 그게 제일 힘들었어요. 알아주셔서 고마워요.", "그걸 알아주시네요. 속이 좀 풀려요." },
        };

        public static string Closing(CounselOutcome outcome, GameRandom rng) => outcome switch
        {
            CounselOutcome.Relieved => rng.Pick(new[] { "이야기하고 나니 한결 가벼워졌어요. 고마워요, 마스터.", "덕분에 좀 살 것 같아요. 다시 힘내 볼게요." }),
            CounselOutcome.Walkout => rng.Pick(new[] { "...됐어요. 그만 가 볼게요.", "더 얘기해 봐야 소용없겠네요. 실례할게요." }),
            _ => rng.Pick(new[] { "시간 내 주셔서 고마워요. 이만 가 볼게요.", "네, 알겠어요. 그럼 이만." }),
        };
    }
}
