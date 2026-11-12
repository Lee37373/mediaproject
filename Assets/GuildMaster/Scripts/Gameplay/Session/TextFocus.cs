using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GuildMaster.Session
{
    /// <summary>
    /// Whether the player is typing into a text field, so shortcuts and ESC stay quiet (P6-2). Only a field
    /// with the caret counts: one that ESC or Enter left stays the selected object, and treating that as typing
    /// (the old per-file checks) kept every shortcut and ESC dead until the player clicked somewhere else.
    /// </summary>
    public static class TextFocus
    {
        public static bool Typing
        {
            get
            {
                var es = EventSystem.current;
                var selected = es != null ? es.currentSelectedGameObject : null;
                if (selected == null) return false;
                var field = selected.GetComponent<InputField>();
                return field != null && field.isFocused;
            }
        }
    }
}
