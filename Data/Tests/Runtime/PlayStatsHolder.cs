using UnityEngine;

namespace UnityTools.Data.PlayModeTests
{
    public class PlayStats : ScriptableObject
    {
        public int Health = 10;
    }

    public class PlayStatsHolder : MonoBehaviour
    {
        public DataReference<PlayStats> Stats = new();
    }
}
