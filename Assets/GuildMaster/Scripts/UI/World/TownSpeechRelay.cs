using GuildMaster.Core;
using GuildMaster.Social;
using UnityEngine;

namespace GuildMaster.UI
{
    /// <summary>
    /// Shows town-talk lines of people who are not adventurers (the merchant) as speech bubbles: gameplay publishes
    /// <see cref="TownSpeechEvent"/>, which it can see, and this passes it on as the UI's own <see cref="SpeechRequestEvent"/>.
    /// </summary>
    public class TownSpeechRelay : MonoBehaviour
    {
        void Awake() => EventBus.Subscribe<TownSpeechEvent>(OnSpeech);
        void OnDestroy() => EventBus.Unsubscribe<TownSpeechEvent>(OnSpeech);

        static void OnSpeech(TownSpeechEvent e)
            => EventBus.Publish(new SpeechRequestEvent { Speaker = e.Speaker, Text = e.Text });
    }
}
