using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UnityTools.PlaytestFeedback.Internal
{
    /// <summary>
    /// Stores reports that couldn't be sent so they can be retried later.
    /// Each item is the finished request body plus its content type.
    /// </summary>
    internal static class FeedbackOutbox
    {
        const int MaxItems = 50;
        const string BodyExtension = ".body";
        const string TypeExtension = ".type";
        const string FailedExtension = ".failed";

        // Tests point the outbox at a temporary folder instead of the real one.
        internal static string FolderOverride;

        public static string FolderPath => FolderOverride ?? Path.Combine(Application.persistentDataPath, "PlaytestFeedback", "Outbox");

        public static void Save(string id, DiscordWebhook.Request request)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);

                var existing = GetPending();
                for (int i = 0; i <= existing.Count - MaxItems; i++) Delete(existing[i]);

                File.WriteAllBytes(Path.Combine(FolderPath, id + BodyExtension), request.Body);
                File.WriteAllText(Path.Combine(FolderPath, id + TypeExtension), request.ContentType);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlaytestFeedback] Couldn't save report to the outbox: {e.Message}");
            }
        }

        /// <summary>Pending item IDs, oldest first.</summary>
        public static List<string> GetPending()
        {
            try
            {
                if (!Directory.Exists(FolderPath)) return new List<string>();
                return new DirectoryInfo(FolderPath)
                    .GetFiles("*" + BodyExtension)
                    .OrderBy(f => f.CreationTimeUtc)
                    .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        public static bool TryLoad(string id, out DiscordWebhook.Request request)
        {
            request = default;
            try
            {
                string body = Path.Combine(FolderPath, id + BodyExtension);
                string type = Path.Combine(FolderPath, id + TypeExtension);
                if (!File.Exists(body) || !File.Exists(type)) return false;
                request = new DiscordWebhook.Request(File.ReadAllBytes(body), File.ReadAllText(type).Trim());
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Delete(string id)
        {
            TryDelete(Path.Combine(FolderPath, id + BodyExtension));
            TryDelete(Path.Combine(FolderPath, id + TypeExtension));
        }

        /// <summary>Keeps the item on disk for inspection but stops retrying it.</summary>
        public static void MarkFailed(string id)
        {
            try
            {
                string body = Path.Combine(FolderPath, id + BodyExtension);
                if (File.Exists(body)) File.Move(body, body + FailedExtension);
                TryDelete(Path.Combine(FolderPath, id + TypeExtension));
            }
            catch (Exception)
            {
                Delete(id);
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // Ignored: worst case the item is retried again.
            }
        }
    }
}
