using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityTools.PlaytestFeedback.Internal
{
    /// <summary>
    /// Builds and sends Discord webhook messages.
    /// Docs: https://discord.com/developers/docs/resources/webhook#execute-webhook
    /// </summary>
    internal static class DiscordWebhook
    {
        // Discord limits.
        const int TitleLimit = 256;
        const int DescriptionLimit = 4096;
        const int FieldNameLimit = 256;
        const int FieldValueLimit = 1024;
        const int MaxFields = 25;
        const int EmbedTotalLimit = 5900; // Real limit is 6000 characters across the whole embed.
        const int ThreadNameLimit = 100;
        const int MaxUploadBytes = 9 * 1024 * 1024; // Default upload limit is 10 MB per message.

        static readonly Color DefaultColor = new Color(0.35f, 0.4f, 0.95f);

        public readonly struct Request
        {
            public readonly byte[] Body;
            public readonly string ContentType;

            public Request(byte[] body, string contentType)
            {
                Body = body;
                ContentType = contentType;
            }
        }

        public struct Result
        {
            public bool Success;
            /// <summary>True if trying again later might work (offline, rate limited, server error).</summary>
            public bool Retryable;
            public long StatusCode;
            public string Message;
        }

        readonly struct FilePart
        {
            public readonly string FileName;
            public readonly string ContentType;
            public readonly byte[] Data;

            public FilePart(string fileName, string contentType, byte[] data)
            {
                FileName = fileName;
                ContentType = contentType;
                Data = data;
            }
        }

        // ------------------------------------------------------------------ Building

        public static Request Build(FeedbackReport report, FeedbackSettings settings)
        {
            var files = new List<FilePart>();

            if (report.Screenshot != null)
            {
                byte[] jpg = report.Screenshot.EncodeToJPG(Mathf.Clamp(settings.jpgQuality, 1, 100));
                if (jpg.Length > MaxUploadBytes / 2) jpg = report.Screenshot.EncodeToJPG(60);
                files.Add(new FilePart("screenshot.jpg", "image/jpeg", jpg));
            }

            if (!string.IsNullOrEmpty(report.Log))
            {
                files.Add(new FilePart("log.txt", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(report.Log)));
            }

            string contextJson = MiniJson.Serialize(BuildContextJson(report), pretty: true);
            files.Add(new FilePart("context.json", "application/json", Encoding.UTF8.GetBytes(contextJson)));

            // Stay under the upload limit: drop the log before anything else.
            if (TotalSize(files) > MaxUploadBytes)
            {
                files.RemoveAll(f => f.FileName == "log.txt");
            }

            string payload = MiniJson.Serialize(BuildPayload(report, settings, files));
            return BuildMultipart(payload, files);
        }

        /// <summary>A plain text message used by the "Send Test Message" button in the settings inspector.</summary>
        public static Request BuildTest(FeedbackSettings settings)
        {
            var payload = new Dictionary<string, object>
            {
                ["username"] = Username(settings),
                ["content"] = $"✅ Playtest Feedback is connected for **{Application.productName}** (v{Application.version}).",
                ["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = Array.Empty<string>() },
            };
            if (!string.IsNullOrWhiteSpace(settings.botAvatarUrl)) payload["avatar_url"] = settings.botAvatarUrl.Trim();
            if (settings.postToForumChannel) payload["thread_name"] = "Playtest Feedback test message";

            return new Request(Encoding.UTF8.GetBytes(MiniJson.Serialize(payload)), "application/json");
        }

        static Dictionary<string, object> BuildPayload(FeedbackReport report, FeedbackSettings settings, List<FilePart> files)
        {
            var category = report.Category;
            string label = category != null && !string.IsNullOrWhiteSpace(category.name) ? category.name.Trim() : "Feedback";

            string firstLine = FirstLine(report.Text, 80);
            if (firstLine.Length == 0) firstLine = report.Screenshot != null ? "Screenshot" : "(no description)";
            string title = $"[{label}] {firstLine}";

            int budget = EmbedTotalLimit;
            var embed = new Dictionary<string, object>();

            string embedTitle = Truncate(title, TitleLimit);
            embed["title"] = embedTitle;
            budget -= embedTitle.Length;

            string footer = $"v{Get(report.SystemInfo, "gameVersion")} · Session {Get(report.SystemInfo, "sessionId")} · Report {report.Id}";
            embed["footer"] = new Dictionary<string, object> { ["text"] = footer };
            budget -= footer.Length;

            if (!string.IsNullOrWhiteSpace(report.PlayerName))
            {
                string author = Truncate(report.PlayerName, 256);
                embed["author"] = new Dictionary<string, object> { ["name"] = author };
                budget -= author.Length;
            }

            // Leave room for the standard fields; anything cut here is still in context.json.
            string description = string.IsNullOrWhiteSpace(report.Text) ? "*No description, see screenshot.*" : report.Text.Trim();
            description = Truncate(description, Mathf.Min(DescriptionLimit, budget - 1000));
            embed["description"] = description;
            budget -= description.Length;

            embed["color"] = ColorToInt(category?.color ?? DefaultColor);
            embed["timestamp"] = report.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            if (report.Screenshot != null && files.Exists(f => f.FileName == "screenshot.jpg"))
            {
                embed["image"] = new Dictionary<string, object> { ["url"] = "attachment://screenshot.jpg" };
            }

            var fields = new List<object>();
            void AddField(string name, string value, bool inline = true)
            {
                if (fields.Count >= MaxFields) return;
                name = Truncate(string.IsNullOrWhiteSpace(name) ? "-" : name, FieldNameLimit);
                value = Truncate(string.IsNullOrWhiteSpace(value) ? "-" : value, FieldValueLimit);
                if (name.Length + value.Length > budget) return;
                budget -= name.Length + value.Length;
                fields.Add(new Dictionary<string, object> { ["name"] = name, ["value"] = value, ["inline"] = inline });
            }

            var sys = report.SystemInfo;
            bool dev = Get(sys, "developmentBuild") == "True";
            bool editor = Get(sys, "isEditor") == "True";
            AddField("Version", Get(sys, "gameVersion") + (editor ? " (editor)" : dev ? " (dev)" : ""));
            AddField("Scene", Get(sys, "activeScene"));
            AddField("Playtime", FormatDuration(Get(sys, "playtimeSeconds")));
            AddField("Platform", Get(sys, "platform"));
            AddField("GPU", Get(sys, "gpu"));
            AddField("Resolution", $"{Get(sys, "resolution")} {Get(sys, "fullScreenMode")}");
            AddField("Performance", $"{Get(sys, "averageFps")} FPS avg\nworst frame {Get(sys, "worstFrameMs")} ms");
            AddField("Log", $"{Get(sys, "logErrors")} errors\n{Get(sys, "logExceptions")} exceptions");
            AddField("OS", Get(sys, "operatingSystem"));

            if (settings.showGameContextInEmbed && report.GameContext != null)
            {
                foreach (var pair in report.GameContext)
                {
                    AddField(pair.Key, FormatValue(pair.Value));
                }
            }

            embed["fields"] = fields;

            var attachments = new List<object>();
            for (int i = 0; i < files.Count; i++)
            {
                attachments.Add(new Dictionary<string, object> { ["id"] = i, ["filename"] = files[i].FileName });
            }

            var payload = new Dictionary<string, object>
            {
                ["username"] = Username(settings),
                ["embeds"] = new object[] { embed },
                // Never let player-written text ping @everyone or roles.
                ["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = Array.Empty<string>() },
                ["attachments"] = attachments,
            };

            if (!string.IsNullOrWhiteSpace(settings.botAvatarUrl)) payload["avatar_url"] = settings.botAvatarUrl.Trim();

            if (settings.postToForumChannel)
            {
                string emoji = category != null && !string.IsNullOrWhiteSpace(category.discordEmoji) ? category.discordEmoji.Trim() + " " : "";
                payload["thread_name"] = Truncate(emoji + title, ThreadNameLimit);

                if (category != null && !string.IsNullOrWhiteSpace(category.forumTagId))
                {
                    payload["applied_tags"] = new[] { category.forumTagId.Trim() };
                }
            }

            return payload;
        }

        static Dictionary<string, object> BuildContextJson(FeedbackReport report)
        {
            return new Dictionary<string, object>
            {
                ["reportId"] = report.Id,
                ["timestampUtc"] = report.TimestampUtc,
                ["category"] = report.Category?.name,
                ["playerName"] = report.PlayerName,
                ["feedback"] = report.Text,
                ["hasScreenshot"] = report.Screenshot != null,
                ["system"] = report.SystemInfo,
                ["game"] = report.GameContext,
            };
        }

        static Request BuildMultipart(string payloadJson, List<FilePart> files)
        {
            string boundary = "----PlaytestFeedback" + Guid.NewGuid().ToString("N");
            using var stream = new MemoryStream();

            void Write(string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }

            Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n");
            Write(payloadJson);
            Write("\r\n");

            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"files[{i}]\"; filename=\"{file.FileName}\"\r\nContent-Type: {file.ContentType}\r\n\r\n");
                stream.Write(file.Data, 0, file.Data.Length);
                Write("\r\n");
            }

            Write($"--{boundary}--\r\n");
            return new Request(stream.ToArray(), $"multipart/form-data; boundary={boundary}");
        }

        // ------------------------------------------------------------------ Sending

        public static IEnumerator Send(string url, Request request, Action<Result> onComplete, int maxRateLimitRetries = 2)
        {
            for (int attempt = 0; ; attempt++)
            {
                using (var www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                {
                    www.uploadHandler = new UploadHandlerRaw(request.Body) { contentType = request.ContentType };
                    www.downloadHandler = new DownloadHandlerBuffer();
                    www.SetRequestHeader("Content-Type", request.ContentType);
                    www.timeout = 30;

                    yield return www.SendWebRequest();

                    long code = www.responseCode;
                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        onComplete?.Invoke(new Result { Success = true, StatusCode = code });
                        yield break;
                    }

                    string body = www.downloadHandler?.text;

                    if (code == 429 && attempt < maxRateLimitRetries)
                    {
                        yield return new WaitForSecondsRealtime(RetryAfterSeconds(www, body));
                        continue;
                    }

                    onComplete?.Invoke(new Result
                    {
                        Success = false,
                        Retryable = www.result == UnityWebRequest.Result.ConnectionError || code == 0 || code == 429 || code >= 500,
                        StatusCode = code,
                        Message = $"{code} {www.error} {Truncate(body ?? "", 300)}".Trim(),
                    });
                    yield break;
                }
            }
        }

        static float RetryAfterSeconds(UnityWebRequest www, string body)
        {
            float seconds = 2f;
            string header = www.GetResponseHeader("Retry-After");
            if (!string.IsNullOrEmpty(header) && float.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            {
                seconds = h;
            }
            else if (!string.IsNullOrEmpty(body))
            {
                var match = Regex.Match(body, "\"retry_after\"\\s*:\\s*([0-9.]+)");
                if (match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                {
                    seconds = b;
                }
            }
            return Mathf.Clamp(seconds + 0.25f, 0.5f, 30f);
        }

        // ------------------------------------------------------------------ Helpers

        static string Username(FeedbackSettings settings) =>
            Truncate(string.IsNullOrWhiteSpace(settings.botUsername) ? "Playtest Feedback" : settings.botUsername.Trim(), 80);

        static int TotalSize(List<FilePart> files)
        {
            int total = 0;
            foreach (var f in files) total += f.Data.Length;
            return total;
        }

        static string Get(IReadOnlyDictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var value) || value == null) return "";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                case Vector2 v2: return v2.ToString("F2");
                case Vector3 v3: return v3.ToString("F2");
                case IFormattable formattable when !(value is System.Collections.IEnumerable):
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return MiniJson.Serialize(value);
            }
        }

        static string FormatDuration(string seconds)
        {
            if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return seconds;
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";
        }

        static int ColorToInt(Color color)
        {
            Color32 c = color;
            return (c.r << 16) | (c.g << 8) | c.b;
        }

        static string FirstLine(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string line = text.Trim();
            int newline = line.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0) line = line.Substring(0, newline).Trim();
            return Truncate(line, maxLength);
        }

        internal static string Truncate(string text, int maxLength)
        {
            if (maxLength <= 0) return "";
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text ?? "";
            return text.Substring(0, Mathf.Max(0, maxLength - 1)) + "…";
        }
    }
}
