using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace UnityTools.PlaytestFeedback.Internal
{
    /// <summary>
    /// Keeps the most recent log messages in a ring buffer so they can be attached to reports.
    /// Starts before the first scene loads so early errors are captured too.
    /// </summary>
    internal static class LogBuffer
    {
        const int Capacity = 1000;

        struct Entry
        {
            public double Time;
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        static readonly object s_Lock = new object();
        static readonly Entry[] s_Entries = new Entry[Capacity];
        static readonly Stopwatch s_Clock = new Stopwatch();
        static int s_Next;
        static int s_Count;

        public static int ErrorCount { get; private set; }
        public static int ExceptionCount { get; private set; }
        public static int WarningCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            Application.logMessageReceivedThreaded -= OnLogMessage;
            lock (s_Lock)
            {
                Array.Clear(s_Entries, 0, s_Entries.Length);
                s_Next = 0;
                s_Count = 0;
                ErrorCount = ExceptionCount = WarningCount = 0;
            }
            s_Clock.Restart();
            Application.logMessageReceivedThreaded += OnLogMessage;
        }

        static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            lock (s_Lock)
            {
                s_Entries[s_Next] = new Entry
                {
                    Time = s_Clock.Elapsed.TotalSeconds,
                    Type = type,
                    Message = message,
                    StackTrace = stackTrace,
                };
                s_Next = (s_Next + 1) % Capacity;
                if (s_Count < Capacity) s_Count++;

                switch (type)
                {
                    case LogType.Error:
                    case LogType.Assert:
                        ErrorCount++;
                        break;
                    case LogType.Exception:
                        ExceptionCount++;
                        break;
                    case LogType.Warning:
                        WarningCount++;
                        break;
                }
            }
        }

        public static string Dump(int maxLines, bool includeStackTraces)
        {
            var sb = new StringBuilder(16 * 1024);
            lock (s_Lock)
            {
                int count = Mathf.Min(s_Count, Mathf.Max(1, maxLines));
                int start = (s_Next - count + Capacity) % Capacity;
                for (int i = 0; i < count; i++)
                {
                    var e = s_Entries[(start + i) % Capacity];
                    sb.Append('[').Append(TimeSpan.FromSeconds(e.Time).ToString(@"hh\:mm\:ss\.fff")).Append("] ");
                    sb.Append('[').Append(e.Type).Append("] ");
                    sb.AppendLine(e.Message);

                    bool isError = e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert;
                    if (includeStackTraces && isError && !string.IsNullOrEmpty(e.StackTrace))
                    {
                        foreach (var line in e.StackTrace.Split('\n'))
                        {
                            if (line.Length > 0) sb.Append("    ").AppendLine(line.TrimEnd('\r'));
                        }
                    }
                }
            }
            return sb.ToString();
        }
    }
}
