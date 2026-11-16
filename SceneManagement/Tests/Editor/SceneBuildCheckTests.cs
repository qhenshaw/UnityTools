using NUnit.Framework;
using SceneManagement.Editor;
using UnityEditor;
using UnityEngine;

namespace SceneManagement.Tests
{
    public class SceneBuildCheckTests
    {
        private static readonly EditorBuildSettingsScene[] Build =
        {
            new("Assets/Scenes/Level 1.unity", true),
            new("Assets/Scenes/Disabled.unity", false),
            new("Assets/Other/Shared.unity", true),
        };

        [Test]
        public void Name_InBuild()
        {
            Assert.IsTrue(SceneBuildCheck.IsInBuild("Level 1", true, Build));
            Assert.IsTrue(SceneBuildCheck.IsInBuild("Shared", true, Build));
        }

        [Test]
        public void Name_NotInBuild()
        {
            Assert.IsFalse(SceneBuildCheck.IsInBuild("Level 2", true, Build));
        }

        [Test]
        public void DisabledScene_CountsAsNotInBuild()
        {
            Assert.IsFalse(SceneBuildCheck.IsInBuild("Disabled", true, Build));
            Assert.IsFalse(SceneBuildCheck.IsInBuild("Assets/Scenes/Disabled.unity", false, Build));
        }

        [Test]
        public void Path_MustMatchExactly()
        {
            Assert.IsTrue(SceneBuildCheck.IsInBuild("Assets/Scenes/Level 1.unity", false, Build));
            Assert.IsFalse(SceneBuildCheck.IsInBuild("Assets/Elsewhere/Level 1.unity", false, Build));
        }

        [Test]
        public void Empty_IsNotFlagged()
        {
            Assert.IsTrue(SceneBuildCheck.IsInBuild("", true, Build));
            Assert.IsTrue(SceneBuildCheck.IsInBuild(null, false, Build));
        }

        [Test]
        public void TriggerLayer_FallsBackToIgnoreRaycast()
        {
            int trigger = LayerMask.NameToLayer("Trigger");
            int expected = trigger >= 0 ? trigger : LayerMask.NameToLayer("Ignore Raycast");
            Assert.AreEqual(expected, StreamingSceneTransition.TriggerLayer());
            Assert.GreaterOrEqual(StreamingSceneTransition.TriggerLayer(), 0);
        }
    }
}
