using System.Collections.Generic;
using UnityEngine;

namespace RingingLoot.View
{
    /// Assets/Art/Sprites 의 스프라이트를 이름으로 찾는 목록. 씬 없이(Play만 눌러도) 코드에서 그림을 쓰려고 Resources에 둔다.
    /// 이름은 "Tiles/fl0_00", "Characters/warrior_hood_down_0" 처럼 Sprites 폴더 기준 경로(확장자 없음).
    /// Assets/Resources/SpriteLibrary.asset 은 에디터 스크립트(SpriteLibraryBuilder)가 만든다. 손으로 고치지 않는다.
    public sealed class SpriteLibrary : ScriptableObject
    {
        [SerializeField] string[] keys = new string[0];
        [SerializeField] Sprite[] sprites = new Sprite[0];
        Dictionary<string, Sprite> map;

        public int Count => keys.Length;

        public Sprite Get(string key)
        {
            if (map == null)
            {
                map = new Dictionary<string, Sprite>();
                for (int i = 0; i < keys.Length; i++) map[keys[i]] = sprites[i];
            }
            return map.TryGetValue(key, out var s) ? s : null;
        }

        public void Set(string[] newKeys, Sprite[] newSprites) { keys = newKeys; sprites = newSprites; map = null; }
    }
}
