using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Data.Tests
{
    public class TestStats : ScriptableObject
    {
        public int Health = 10;
    }

    public class OtherStats : ScriptableObject
    {
        public float Speed = 5f;
    }

    public class TestHolder : ScriptableObject
    {
        public DataReference<TestStats> Stats = new();
        public List<DataReference<TestStats>> StatsList = new();
        public DataReference<OtherStats>[] OtherArray = new DataReference<OtherStats>[0];
    }
}
