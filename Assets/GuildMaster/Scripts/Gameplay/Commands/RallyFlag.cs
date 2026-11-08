using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Commands
{
    /// <summary>The guild master's rally flag on the map (P4-132): its cloth flaps while it stands.</summary>
    public class RallyFlag : MonoBehaviour
    {
        SpriteRenderer _cloth;
        float _t;

        public static RallyFlag Create(Transform parent)
        {
            var go = new GameObject("RallyFlag");
            go.transform.SetParent(parent, false);
            var flag = go.AddComponent<RallyFlag>();
            flag._cloth = go.AddComponent<SpriteRenderer>();
            flag._cloth.sprite = CartoonArt.RallyFlag(0);
            go.SetActive(false);
            return flag;
        }

        public void Plant(Vector2 at)
        {
            transform.position = at;
            _t = 0f;
            gameObject.SetActive(true);
        }

        public void Take() => gameObject.SetActive(false);

        void Update()
        {
            _t += Time.deltaTime;
            _cloth.sprite = CartoonArt.RallyFlag(Mathf.FloorToInt(_t * 2.5f));
        }
    }
}
