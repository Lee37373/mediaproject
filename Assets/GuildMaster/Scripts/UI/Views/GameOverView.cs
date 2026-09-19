using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Full-screen game-over summary with a restart button (reloads the scene).</summary>
    public class GameOverView : MonoBehaviour
    {
        RectTransform _root;
        Text _reason;
        Text _stats;

        public void Build(RectTransform canvas)
        {
            var dim = UIBuilder.Panel(canvas, "GameOver", new Color(0f, 0f, 0f, 0.75f));
            _root = dim.rectTransform;
            _root.Stretch();
            var box = UIBuilder.Panel(_root, "Box", UITheme.PanelBg);
            box.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 370));
            UIBuilder.VLayout(box, 12, 24).childAlignment = TextAnchor.UpperCenter;
            UIBuilder.Label(box.transform, "게임 오버", 34, TextAnchor.MiddleCenter, UITheme.Bad, FontStyle.Bold).Size(-1, 50);
            _reason = UIBuilder.Label(box.transform, "", 18, TextAnchor.MiddleCenter).Size(-1, 30);
            _stats = UIBuilder.Label(box.transform, "", 16, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 110);
            UIBuilder.Button(box.transform, "다시 시작", Restart, -1, 44, new Color(0.25f, 0.45f, 0.3f), 18);
            UIBuilder.Button(box.transform, "타이틀로", () => GameSettings.ReloadScene(showTitle: true), -1, 40, null, 17);
            _root.gameObject.SetActive(false);
            EventBus.Subscribe<GameOverEvent>(Show);
        }

        void OnDestroy() => EventBus.Unsubscribe<GameOverEvent>(Show);

        void Show(GameOverEvent e)
        {
            _reason.text = e.Reason;
            _stats.text = $"생존 {e.Day}일 · 침공 {e.Waves}회\n남은 길드원 {e.Members}명 · 영입 {e.Recruits}명\n완료한 의뢰 {e.QuestsCompleted}건 · 멸종한 종 {e.ExtinctSpecies}";
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        /// <summary>A fresh world straight away (no title screen, P4-10).</summary>
        static void Restart() => GameSettings.ReloadScene(showTitle: false);
    }
}
