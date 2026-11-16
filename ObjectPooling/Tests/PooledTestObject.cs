using System.Collections.Generic;
using ObjectPooling;
using UnityEngine;

namespace UnityTools.ObjectPooling.Tests
{
    /// <summary>Records its lifecycle calls, and the position it was at for each one.</summary>
    public class PooledTestObject : PooledObject
    {
        public readonly List<string> Events = new List<string>();
        public readonly List<Vector3> EventPositions = new List<Vector3>();

        private void Record(string message)
        {
            Events.Add(message);
            EventPositions.Add(transform.position);
        }

        private void Awake() => Record("Awake");
        private void OnEnable() => Record("OnEnable");
        private void OnDisable() => Record("OnDisable");
        protected override void OnTakenFromPool() => Record("Taken");
        protected override void OnReturnedToPool() => Record("Returned");
    }
}
