using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.DebugTools
{
    /// <summary>
    /// Registry for the in-game debug menu. Features call <see cref="Register"/> (usually in OnEnable) to add a
    /// section of controls, and <see cref="Unregister"/> (in OnDisable) to remove it:
    /// <code>
    /// private void OnEnable() => DebugMenu.Register(this, "Player", section => section
    ///     .Button("Heal", () => health.Heal())
    ///     .Toggle("God mode", () => godMode, v => godMode = v)
    ///     .Slider("Speed", 0f, 20f, () => speed, v => speed = v)
    ///     .Readout("Health", () => $"{health.Current}/{health.Max}"));
    ///
    /// private void OnDisable() => DebugMenu.Unregister(this);
    /// </code>
    /// The menu exists in the Editor and Development builds, and in release builds when the DEBUG_MENU scripting
    /// define is set. Otherwise both calls, and the lambdas passed to them, are compiled out.
    /// </summary>
    public static class DebugMenu
    {
        private static readonly List<DebugSection> _sections = new List<DebugSection>();

        /// <summary>Raised when sections are added or removed, so the menu can rebuild.</summary>
        public static event Action SectionsChanged;

        /// <summary>All registered sections, in registration order.</summary>
        public static IReadOnlyList<DebugSection> Sections => _sections;

        /// <summary>
        /// Adds a section of controls titled <paramref name="title"/>, owned by <paramref name="owner"/>.
        /// <paramref name="build"/> runs once, right away; an empty title uses the owner's type name.
        /// Sections from several owners with the same title are shown together.
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("DEBUG_MENU")]
        public static void Register(object owner, string title, Action<DebugSection> build)
        {
            if (owner == null || build == null) return;
            var section = new DebugSection(owner, string.IsNullOrEmpty(title) ? owner.GetType().Name : title);
            build(section);
            _sections.Add(section);
            SectionsChanged?.Invoke();
        }

        /// <summary>Removes every section registered by <paramref name="owner"/>.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("DEBUG_MENU")]
        public static void Unregister(object owner)
        {
            if (owner == null) return;
            if (_sections.RemoveAll(s => ReferenceEquals(s.Owner, owner)) > 0) SectionsChanged?.Invoke();
        }

        /// <summary>Removes sections whose owner is a destroyed Unity object (one that never called Unregister).</summary>
        internal static void RemoveDestroyedOwners()
        {
            if (_sections.RemoveAll(s => s.Owner is Object obj && obj == null) > 0) SectionsChanged?.Invoke();
        }

        // Clears stale sections when Play starts, including with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            _sections.Clear();
            SectionsChanged = null;
        }
    }

    /// <summary>The kind of UI a <see cref="DebugControl"/> becomes.</summary>
    public enum DebugControlKind
    {
        Button,
        Toggle,
        Slider,
        IntSlider,
        Dropdown,
        Readout
    }

    /// <summary>One feature's group of debug controls, built with the methods below.</summary>
    public class DebugSection
    {
        internal DebugSection(object owner, string title)
        {
            Owner = owner;
            Title = title;
        }

        /// <summary>The object passed to <see cref="DebugMenu.Register"/>; <see cref="DebugMenu.Unregister"/> removes its sections.</summary>
        public object Owner { get; }
        /// <summary>The section's title, shown as the foldout label.</summary>
        public string Title { get; }
        /// <summary>The section's controls, in the order they were added.</summary>
        public List<DebugControl> Controls { get; } = new List<DebugControl>();

        /// <summary>A button that runs <paramref name="onClick"/>.</summary>
        public DebugSection Button(string label, Action onClick)
        {
            Controls.Add(new DebugControl(DebugControlKind.Button, label) { OnClick = onClick });
            return this;
        }

        /// <summary>A checkbox bound to a getter and setter.</summary>
        public DebugSection Toggle(string label, Func<bool> get, Action<bool> set)
        {
            Controls.Add(new DebugControl(DebugControlKind.Toggle, label) { GetBool = get, SetBool = set });
            return this;
        }

        /// <summary>A slider for a float, bound to a getter and setter.</summary>
        public DebugSection Slider(string label, float min, float max, Func<float> get, Action<float> set)
        {
            Controls.Add(new DebugControl(DebugControlKind.Slider, label) { Min = min, Max = max, GetFloat = get, SetFloat = set });
            return this;
        }

        /// <summary>A slider for whole numbers (counts, levels, waves), bound to a getter and setter.</summary>
        public DebugSection SliderInt(string label, int min, int max, Func<int> get, Action<int> set)
        {
            Controls.Add(new DebugControl(DebugControlKind.IntSlider, label) { Min = min, Max = max, GetInt = get, SetInt = set });
            return this;
        }

        /// <summary>A dropdown of <paramref name="options"/>, bound to the selected index.</summary>
        public DebugSection Dropdown(string label, IReadOnlyList<string> options, Func<int> getIndex, Action<int> setIndex)
        {
            Controls.Add(new DebugControl(DebugControlKind.Dropdown, label)
            {
                Options = options ?? Array.Empty<string>(),
                GetInt = getIndex,
                SetInt = setIndex
            });
            return this;
        }

        /// <summary>A dropdown of every value of the enum <typeparamref name="TEnum"/>, bound to a getter and setter.</summary>
        public DebugSection Dropdown<TEnum>(string label, Func<TEnum> get, Action<TEnum> set) where TEnum : struct, Enum
        {
            var values = (TEnum[])Enum.GetValues(typeof(TEnum));
            var names = Array.ConvertAll(values, v => v.ToString());
            return Dropdown(label, names,
                () => get == null ? 0 : Math.Max(0, Array.IndexOf(values, get())),
                index => { if (set != null && index >= 0 && index < values.Length) set(values[index]); });
        }

        /// <summary>A read-only line of live state, refreshed a few times a second.</summary>
        public DebugSection Readout(string label, Func<string> get)
        {
            Controls.Add(new DebugControl(DebugControlKind.Readout, label) { GetText = get });
            return this;
        }
    }

    /// <summary>Description of one control; the menu turns it into UI.</summary>
    public class DebugControl
    {
        internal DebugControl(DebugControlKind kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public DebugControlKind Kind { get; }
        public string Label { get; }

        public Action OnClick { get; internal set; }
        public Func<bool> GetBool { get; internal set; }
        public Action<bool> SetBool { get; internal set; }
        public float Min { get; internal set; }
        public float Max { get; internal set; }
        public Func<float> GetFloat { get; internal set; }
        public Action<float> SetFloat { get; internal set; }
        public Func<int> GetInt { get; internal set; }
        public Action<int> SetInt { get; internal set; }
        public IReadOnlyList<string> Options { get; internal set; }
        public Func<string> GetText { get; internal set; }
    }
}
