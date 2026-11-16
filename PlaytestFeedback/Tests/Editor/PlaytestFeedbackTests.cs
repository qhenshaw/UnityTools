using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityTools.PlaytestFeedback.Editor;
using UnityTools.PlaytestFeedback.Internal;
using Object = UnityEngine.Object;

namespace UnityTools.PlaytestFeedback.Tests
{
    public class PlaytestFeedbackTests
    {
        private string _outbox;

        [SetUp]
        public void SetUp()
        {
            _outbox = Path.Combine(Path.GetTempPath(), "UnityToolsOutboxTest_" + Guid.NewGuid().ToString("N"));
            FeedbackOutbox.FolderOverride = _outbox;
            FeedbackSettings.WebhookUrlOverride = null;
        }

        [TearDown]
        public void TearDown()
        {
            FeedbackOutbox.FolderOverride = null;
            FeedbackSettings.WebhookUrlOverride = null;
            if (Directory.Exists(_outbox)) Directory.Delete(_outbox, true);
        }

        private static FeedbackReport Report(string text = "The door doesn't open", FeedbackCategory category = null,
            Dictionary<string, object> context = null, string log = null)
        {
            return new FeedbackReport
            {
                Id = "abc123",
                TimestampUtc = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc),
                Category = category ?? new FeedbackCategory("Bug", Color.red, "X"),
                Text = text,
                PlayerName = "Tester",
                SystemInfo = new Dictionary<string, object> { ["gameVersion"] = "1.0", ["sessionId"] = "s1", ["activeScene"] = "Level1" },
                GameContext = context ?? new Dictionary<string, object>(),
                Log = log,
            };
        }

        // The payload_json part of a multipart request, and the file names it carries.
        private static string PayloadOf(DiscordWebhook.Request request)
        {
            string body = Encoding.UTF8.GetString(request.Body);
            int start = body.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
            int end = body.IndexOf("\r\n--", start, StringComparison.Ordinal);
            return body.Substring(start, end - start);
        }

        // ---------- Settings ----------

        [Test]
        public void SettingsJson_RoundTripsEveryValue()
        {
            var settings = new FeedbackSettings
            {
                webhookUrl = "https://discord.com/api/webhooks/1/abc",
                postToForumChannel = false,
                botUsername = "Bot",
                developmentBuildsOnly = true,
                pauseAudio = false,
                actionMapsToKeepEnabled = new[] { "Voice" },
                maxTextLength = 500,
                categories = new List<FeedbackCategory> { new FeedbackCategory("Crash", Color.black, "!") { forumTagId = "42" } },
                logLines = 50,
            };
            settings.toggleAction = new InputAction("Toggle", InputActionType.Button, "<Keyboard>/f9");

            FeedbackSettings copy = FeedbackSettings.FromJson(settings.ToJson());

            Assert.AreEqual(settings.webhookUrl, copy.webhookUrl);
            Assert.IsFalse(copy.postToForumChannel);
            Assert.AreEqual("Bot", copy.botUsername);
            Assert.IsTrue(copy.developmentBuildsOnly);
            Assert.IsFalse(copy.pauseAudio);
            CollectionAssert.AreEqual(new[] { "Voice" }, copy.actionMapsToKeepEnabled);
            Assert.AreEqual(500, copy.maxTextLength);
            Assert.AreEqual(1, copy.categories.Count);
            Assert.AreEqual("Crash", copy.categories[0].name);
            Assert.AreEqual("42", copy.categories[0].forumTagId);
            Assert.AreEqual(50, copy.logLines);
            Assert.AreEqual("<Keyboard>/f9", copy.toggleAction.bindings[0].path);
        }

        [Test]
        public void SettingsFromEmptyJson_HasDefaults()
        {
            FeedbackSettings settings = FeedbackSettings.FromJson("");

            Assert.IsTrue(settings.autoCreate);
            Assert.IsTrue(settings.pauseTime);
            Assert.AreEqual(3, settings.categories.Count);
            Assert.AreEqual(0, settings.toggleAction.bindings.Count);
            InputAction hotkey = settings.CreateToggleActionInstance();
            Assert.AreEqual("<Keyboard>/f8", hotkey.bindings[0].path);
            hotkey.Dispose();
        }

        [Test]
        public void CustomHotkey_SurvivesSerializingIntoFreshSettings()
        {
            // Unity deserializes ProjectSettings over a freshly constructed FeedbackSettings, like FromJson does.
            var settings = new FeedbackSettings();
            settings.toggleAction.AddBinding("<Keyboard>/f12");

            FeedbackSettings copy = FeedbackSettings.FromJson(settings.ToJson());
            InputAction hotkey = copy.CreateToggleActionInstance();

            Assert.AreEqual("<Keyboard>/f12", hotkey.bindings[0].path);
            LogAssert.NoUnexpectedReceived();
            hotkey.Dispose();
        }

        [Test]
        public void CustomHotkey_SurvivesUnitySerialization()
        {
            var holder = ScriptableObject.CreateInstance<TestSettingsHolder>();
            holder.settings.toggleAction.AddBinding("<Keyboard>/f12");
            var clone = Object.Instantiate(holder);
            try
            {
                InputAction hotkey = clone.settings.CreateToggleActionInstance();
                Assert.AreEqual("<Keyboard>/f12", hotkey.bindings[0].path);
                hotkey.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void ToggleActionInstance_IsACopy_AndFallsBackToDefaults()
        {
            var settings = new FeedbackSettings();
            InputAction copy = settings.CreateToggleActionInstance();
            Assert.AreNotSame(settings.toggleAction, copy);
            copy.Dispose();

            settings.toggleAction = new InputAction("Empty");
            InputAction fallback = settings.CreateToggleActionInstance();
            Assert.AreEqual("<Keyboard>/f8", fallback.bindings[0].path);
            Assert.AreEqual(0, settings.toggleAction.bindings.Count);
            fallback.Dispose();
        }

        [Test]
        public void WebhookUrl_ValidationAndOverride()
        {
            Assert.IsTrue(FeedbackSettings.IsValidWebhookUrl("https://discord.com/api/webhooks/1/x"));
            Assert.IsTrue(FeedbackSettings.IsValidWebhookUrl("  https://ptb.discord.com/api/webhooks/1/x "));
            Assert.IsFalse(FeedbackSettings.IsValidWebhookUrl("https://example.com/hook"));
            Assert.IsFalse(FeedbackSettings.IsValidWebhookUrl(""));

            var settings = new FeedbackSettings { webhookUrl = " https://discord.com/api/webhooks/1/a " };
            Assert.AreEqual("https://discord.com/api/webhooks/1/a", settings.EffectiveWebhookUrl);
            FeedbackSettings.WebhookUrlOverride = "https://discord.com/api/webhooks/2/b";
            Assert.AreEqual("https://discord.com/api/webhooks/2/b", settings.EffectiveWebhookUrl);
        }

        [Test]
        public void EnabledInEditor_ControlsEditorAvailability()
        {
            Assert.IsTrue(new FeedbackSettings { enabledInEditor = true }.IsEnabledForThisBuild);
            Assert.IsFalse(new FeedbackSettings { enabledInEditor = false }.IsEnabledForThisBuild);
        }

        // ---------- Migration from the standalone package ----------

#pragma warning disable CS0618
        [Test]
        public void ConvertLegacy_CopiesEveryValue()
        {
            var legacy = ScriptableObject.CreateInstance<FeedbackSettingsAsset>();
            try
            {
                legacy.webhookUrl = "https://discord.com/api/webhooks/9/z";
                legacy.postToForumChannel = false;
                legacy.isolateUiInput = false;
                legacy.maxTextLength = 1234;
                legacy.categories = new List<FeedbackCategory> { new FeedbackCategory("Lag", Color.green, "~") };
                legacy.toggleAction = new InputAction("Toggle", InputActionType.Button, "<Keyboard>/f10");

                FeedbackSettings settings = PlaytestFeedbackProjectSettings.ConvertLegacy(legacy, out bool dropped);

                Assert.IsFalse(dropped);
                Assert.AreEqual(legacy.webhookUrl, settings.webhookUrl);
                Assert.IsFalse(settings.postToForumChannel);
                Assert.IsFalse(settings.isolateUiInput);
                Assert.AreEqual(1234, settings.maxTextLength);
                Assert.AreEqual("Lag", settings.categories[0].name);
                Assert.AreEqual("<Keyboard>/f10", settings.toggleAction.bindings[0].path);
            }
            finally
            {
                Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void ConvertLegacy_ReportsDroppedAssetReferences()
        {
            var legacy = ScriptableObject.CreateInstance<FeedbackSettingsAsset>();
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            try
            {
                legacy.panelSettingsOverride = panelSettings;

                PlaytestFeedbackProjectSettings.ConvertLegacy(legacy, out bool dropped);

                Assert.IsTrue(dropped);
            }
            finally
            {
                Object.DestroyImmediate(legacy);
                Object.DestroyImmediate(panelSettings);
            }
        }

        [Test]
        public void ConvertLegacy_WithoutToggleAction_UsesDefault()
        {
            var legacy = ScriptableObject.CreateInstance<FeedbackSettingsAsset>();
            try
            {
                legacy.toggleAction = null;

                FeedbackSettings settings = PlaytestFeedbackProjectSettings.ConvertLegacy(legacy, out _);

                Assert.IsNotNull(settings.toggleAction);
                Assert.IsTrue(settings.CreateToggleActionInstance().bindings.Count > 0);
            }
            finally
            {
                Object.DestroyImmediate(legacy);
            }
        }
#pragma warning restore CS0618

        // ---------- Build step ----------

        [Test]
        public void BuildStep_IncludesTool_UnlessDisabledForThatBuild()
        {
            var release = new BuildPlayerOptions { targetGroup = BuildTargetGroup.Standalone };
            var development = new BuildPlayerOptions { targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.Development };
            var disabled = new BuildPlayerOptions { targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.Development, extraScriptingDefines = new[] { "PLAYTEST_FEEDBACK_DISABLED" } };

            Assert.IsTrue(PlaytestFeedbackBuildStep.IncludesTool(release, new FeedbackSettings()));
            Assert.IsFalse(PlaytestFeedbackBuildStep.IncludesTool(release, new FeedbackSettings { developmentBuildsOnly = true }));
            Assert.IsTrue(PlaytestFeedbackBuildStep.IncludesTool(development, new FeedbackSettings { developmentBuildsOnly = true }));
            Assert.IsFalse(PlaytestFeedbackBuildStep.IncludesTool(disabled, new FeedbackSettings()));
        }

        // ---------- Discord payload ----------

        [Test]
        public void Payload_ForForum_HasThreadNameTagAndNoMentions()
        {
            var category = new FeedbackCategory("Bug", Color.red, "🐞") { forumTagId = "123" };
            var settings = new FeedbackSettings { postToForumChannel = true };

            string payload = PayloadOf(DiscordWebhook.Build(Report(category: category), settings));

            StringAssert.Contains("\"thread_name\":\"🐞 [Bug] The door doesn't open\"", payload);
            StringAssert.Contains("\"applied_tags\":[\"123\"]", payload);
            StringAssert.Contains("\"allowed_mentions\":{\"parse\":[]}", payload);
            StringAssert.Contains("\"title\":\"[Bug] The door doesn't open\"", payload);
        }

        [Test]
        public void Payload_ForTextChannel_HasNoThreadName()
        {
            var settings = new FeedbackSettings { postToForumChannel = false };

            string payload = PayloadOf(DiscordWebhook.Build(Report(), settings));

            StringAssert.DoesNotContain("thread_name", payload);
            StringAssert.DoesNotContain("applied_tags", payload);
        }

        [Test]
        public void Payload_LongTextAndManyFields_StayWithinDiscordLimits()
        {
            var context = new Dictionary<string, object>();
            for (int i = 0; i < 60; i++) context["Key " + i] = new string('v', 900);
            var settings = new FeedbackSettings();

            string payload = PayloadOf(DiscordWebhook.Build(Report(new string('x', 10000), context: context), settings));

            int threadStart = payload.IndexOf("\"thread_name\":\"", StringComparison.Ordinal) + 15;
            int threadEnd = payload.IndexOf('"', threadStart);
            Assert.LessOrEqual(threadEnd - threadStart, 100);
            Assert.LessOrEqual(CountOccurrences(payload, "\"inline\""), 25);
            // The whole embed stays under Discord's 6000-character limit (description + fields + title + footer).
            Assert.Less(payload.Length, 6000 + 2000);
        }

        [Test]
        public void Request_AttachesContextJson_AndLogOnlyWhenPresent()
        {
            var settings = new FeedbackSettings();

            string without = Encoding.UTF8.GetString(DiscordWebhook.Build(Report(), settings).Body);
            string with = Encoding.UTF8.GetString(DiscordWebhook.Build(Report(log: "line 1\nline 2"), settings).Body);

            StringAssert.Contains("filename=\"context.json\"", without);
            StringAssert.DoesNotContain("filename=\"log.txt\"", without);
            StringAssert.Contains("filename=\"log.txt\"", with);
            StringAssert.Contains("line 2", with);
        }

        [Test]
        public void Request_IncludesGameContext_InContextJson_EvenWhenNotInEmbed()
        {
            var context = new Dictionary<string, object> { ["Gold"] = 120 };
            var settings = new FeedbackSettings { showGameContextInEmbed = false };

            string body = Encoding.UTF8.GetString(DiscordWebhook.Build(Report(context: context), settings).Body);

            StringAssert.DoesNotContain("\"name\":\"Gold\"", PayloadOf(DiscordWebhook.Build(Report(context: context), settings)));
            StringAssert.Contains("\"Gold\": 120", body);
        }

        [Test]
        public void TestMessage_IsPlainJson()
        {
            DiscordWebhook.Request request = DiscordWebhook.BuildTest(new FeedbackSettings { postToForumChannel = true });

            Assert.AreEqual("application/json", request.ContentType);
            string json = Encoding.UTF8.GetString(request.Body);
            StringAssert.Contains("\"thread_name\"", json);
            StringAssert.Contains("\"allowed_mentions\":{\"parse\":[]}", json);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal)) count++;
            return count;
        }

        // ---------- MiniJson ----------

        [Test]
        public void MiniJson_EscapesStrings_AndUsesInvariantNumbers()
        {
            string json = MiniJson.Serialize(new Dictionary<string, object>
            {
                ["text"] = "say \"hi\"\n\\",
                ["number"] = 1.5f,
                ["whole"] = 3,
                ["flag"] = true,
                ["none"] = null,
                ["list"] = new[] { 1, 2 },
                ["position"] = new Vector3(1f, 2f, 3f),
            });

            StringAssert.Contains("\"text\":\"say \\\"hi\\\"\\n\\\\\"", json);
            StringAssert.Contains("\"number\":1.5", json);
            StringAssert.Contains("\"whole\":3", json);
            StringAssert.Contains("\"flag\":true", json);
            StringAssert.Contains("\"none\":null", json);
            StringAssert.Contains("\"list\":[1,2]", json);
            StringAssert.Contains("\"position\":[1,2,3]", json);
        }

        // ---------- Outbox ----------

        [Test]
        public void Outbox_SavesLoadsAndDeletes()
        {
            var request = new DiscordWebhook.Request(Encoding.UTF8.GetBytes("body"), "multipart/form-data; boundary=x");

            FeedbackOutbox.Save("report1", request);

            CollectionAssert.AreEqual(new[] { "report1" }, FeedbackOutbox.GetPending());
            Assert.IsTrue(FeedbackOutbox.TryLoad("report1", out DiscordWebhook.Request loaded));
            Assert.AreEqual("body", Encoding.UTF8.GetString(loaded.Body));
            Assert.AreEqual("multipart/form-data; boundary=x", loaded.ContentType);

            FeedbackOutbox.Delete("report1");
            CollectionAssert.IsEmpty(FeedbackOutbox.GetPending());
        }

        [Test]
        public void Outbox_MarkFailed_KeepsFileButStopsRetrying()
        {
            FeedbackOutbox.Save("bad", new DiscordWebhook.Request(new byte[] { 1 }, "application/json"));

            FeedbackOutbox.MarkFailed("bad");

            CollectionAssert.IsEmpty(FeedbackOutbox.GetPending());
            Assert.IsTrue(File.Exists(Path.Combine(_outbox, "bad.body.failed")));
        }

        [Test]
        public void Outbox_TryLoad_MissingItem_ReturnsFalse()
        {
            Assert.IsFalse(FeedbackOutbox.TryLoad("nope", out _));
        }
    }
}
