using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleStateMachine
{
    /// <summary>
    /// A small state machine bound to an object (usually the MonoBehaviour that owns it). Add transitions, enter the
    /// first state with <see cref="NextState{S}"/>, then call <see cref="Update"/> and <see cref="FixedUpdate"/> every frame.
    /// When several transitions are possible at once, the one added last wins, and transitions from any state are
    /// checked before the others.
    /// </summary>
    public class StateMachine<T>
    {
        /// <summary>A transition added to the machine. Keep it to remove the transition later with <see cref="RemoveTransition(Transition)"/>.</summary>
        public sealed class Transition
        {
            /// <summary>The state this transition leaves from, or null for a transition from any state.</summary>
            public Type From { get; }
            public Type To { get; }

            internal readonly Func<State<T>> Create;
            internal readonly Func<State<T>, bool> Condition;
            internal readonly Delegate Source;

            internal Transition(Type from, Type to, Func<State<T>> create, Func<State<T>, bool> condition, Delegate source)
            {
                From = from;
                To = to;
                Create = create;
                Condition = condition;
                Source = source;
            }
        }

        public T Binding { get; private set; }
        public State<T> CurrentState { get; private set; }

        /// <summary>The state that was current before the last state change, or null.</summary>
        public State<T> PreviousState { get; private set; }

        public bool EnableLogging { get; set; } = false;
        public bool UpdateConditionsCheck { get; set; } = true;
        public bool FixedUpdateConditionsCheck { get; set; } = false;

        public event EventHandler<State<T>> OnStateChanged;

        private readonly List<Transition> _transitions = new List<Transition>();
        private readonly List<Transition> _anyTransitions = new List<Transition>();
        private readonly Dictionary<Type, State<T>> _states = new Dictionary<Type, State<T>>();

        private bool _switching;
        private Type _queuedType;
        private Func<State<T>> _queuedCreate;

        public StateMachine(T binding)
        {
            Binding = binding;
        }

        public StateMachine(T binding, bool enableLogging) : this(binding)
        {
            EnableLogging = enableLogging;
        }

        /// <summary>Goes from <typeparamref name="U"/> to <typeparamref name="V"/> when <paramref name="condition"/> is true.</summary>
        public Transition AddTransition<U, V>(Func<bool> condition) where U : State<T> where V : State<T>, new()
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return Add(_transitions, new Transition(typeof(U), typeof(V), () => new V(), _ => condition(), condition));
        }

        /// <summary>
        /// Goes from <typeparamref name="U"/> to <typeparamref name="V"/> when <paramref name="condition"/> is true.
        /// The condition gets the current state as a <typeparamref name="U"/>, e.g. <c>attack => attack.IsAttackCompleted</c>.
        /// </summary>
        public Transition AddTransition<U, V>(Func<U, bool> condition) where U : State<T> where V : State<T>, new()
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return Add(_transitions, new Transition(typeof(U), typeof(V), () => new V(), state => condition((U)state), condition));
        }

        /// <summary>
        /// Goes to <typeparamref name="V"/> from any other state when <paramref name="condition"/> is true, e.g. to a
        /// death state. Checked before normal transitions, and never while already in <typeparamref name="V"/>.
        /// </summary>
        public Transition AddTransitionFromAny<V>(Func<bool> condition) where V : State<T>, new()
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return Add(_anyTransitions, new Transition(null, typeof(V), () => new V(), _ => condition(), condition));
        }

        private Transition Add(List<Transition> list, Transition transition)
        {
            list.Add(transition);
            Log($"{Binding} SM transition added: from {(transition.From != null ? transition.From.Name : "any state")} to {transition.To.Name}");
            return transition;
        }

        /// <summary>Removes a transition returned by AddTransition or AddTransitionFromAny. Returns false if it wasn't there.</summary>
        public bool RemoveTransition(Transition transition)
        {
            if (transition == null) return false;
            bool removed = _transitions.Remove(transition) || _anyTransitions.Remove(transition);
            if (removed) Log($"{Binding} SM transition removed: from {(transition.From != null ? transition.From.Name : "any state")} to {transition.To.Name}");
            return removed;
        }

        /// <summary>Removes every transition from <typeparamref name="U"/> to <typeparamref name="V"/>. Returns how many were removed.</summary>
        public int RemoveTransitions<U, V>() where U : State<T> where V : State<T>
        {
            int removed = _transitions.RemoveAll(t => t.From == typeof(U) && t.To == typeof(V));
            if (removed > 0) Log($"{Binding} SM {removed} transition(s) removed: from {typeof(U).Name} to {typeof(V).Name}");
            return removed;
        }

        [Obsolete("A lambda never matches the one you added, because each lambda is a new object. Keep the Transition returned by AddTransition and call RemoveTransition(transition), or use RemoveTransitions<U, V>().")]
        public void RemoveTransition<U, V>(Func<bool> condition) where U : State<T> where V : State<T>
        {
            int index = _transitions.FindIndex(t => t.From == typeof(U) && t.To == typeof(V) && Equals(t.Source, condition));
            if (index < 0)
            {
                Debug.LogWarning($"{Binding} SM: no transition from {typeof(U).Name} to {typeof(V).Name} with that condition was found, so nothing was removed. " +
                                 "Keep the Transition returned by AddTransition and pass it to RemoveTransition instead.");
                return;
            }
            RemoveTransition(_transitions[index]);
        }

        /// <summary>
        /// Switches to <typeparamref name="S"/>: Exit on the current state, then Enter on <typeparamref name="S"/>.
        /// Calling it with the current state re-enters it. Called from inside Enter or Exit, the switch happens right
        /// after the current one finishes.
        /// </summary>
        public void NextState<S>() where S : State<T>, new()
        {
            ChangeState(typeof(S), () => new S());
        }

        private void ChangeState(Type type, Func<State<T>> create)
        {
            if (_switching)
            {
                _queuedType = type;
                _queuedCreate = create;
                return;
            }

            _switching = true;
            try
            {
                while (type != null)
                {
                    State<T> next = GetState(type, create);

                    if (CurrentState != null)
                    {
                        Log($"{Binding} SM Exiting State: {CurrentState.GetType().Name}");
                        CurrentState.Exit();
                    }

                    PreviousState = CurrentState;
                    CurrentState = next;
                    next.TimeInState = 0f;
                    Log($"{Binding} SM Entering State: {next.GetType().Name}");
                    next.Enter();
                    OnStateChanged?.Invoke(this, next);

                    type = _queuedType;
                    create = _queuedCreate;
                    _queuedType = null;
                    _queuedCreate = null;
                }
            }
            finally
            {
                _switching = false;
                _queuedType = null;
                _queuedCreate = null;
            }
        }

        private State<T> GetState(Type type, Func<State<T>> create)
        {
            if (!_states.TryGetValue(type, out State<T> state))
            {
                state = create();
                state.Initialize(this, Binding);
                _states.Add(type, state);
            }
            return state;
        }

        public void FixedUpdate(float deltaTime)
        {
            if (CurrentState == null) return;
            CurrentState.FixedUpdate(deltaTime);
            if (FixedUpdateConditionsCheck) CheckConditions();
        }

        public void Update(float deltaTime)
        {
            if (CurrentState == null) return;
            CurrentState.TimeInState += deltaTime;
            CurrentState.Update(deltaTime);
            if (UpdateConditionsCheck) CheckConditions();
        }

        private void CheckConditions()
        {
            if (CurrentState == null) return;
            Type current = CurrentState.GetType();

            for (int i = _anyTransitions.Count - 1; i >= 0; i--)
            {
                if (i >= _anyTransitions.Count) continue;
                Transition transition = _anyTransitions[i];
                if (transition.To == current) continue;
                if (transition.Condition(CurrentState))
                {
                    ChangeState(transition.To, transition.Create);
                    return;
                }
            }

            for (int i = _transitions.Count - 1; i >= 0; i--)
            {
                if (i >= _transitions.Count) continue;
                Transition transition = _transitions[i];
                if (transition.From != current) continue;
                if (transition.Condition(CurrentState))
                {
                    ChangeState(transition.To, transition.Create);
                    return;
                }
            }
        }

        /// <summary>
        /// Editor only: draws the current state, its time and the previous state as a label in the Scene view.
        /// Call it from your MonoBehaviour's OnDrawGizmos, e.g. <c>_stateMachine?.DrawDebugLabel(transform.position + Vector3.up * 2f);</c>
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DrawDebugLabel(Vector3 position)
        {
#if UNITY_EDITOR
            string text = CurrentState == null ? "(no state)" : $"{CurrentState.GetType().Name} {CurrentState.TimeInState:0.0}s";
            if (PreviousState != null) text += $"\nfrom {PreviousState.GetType().Name}";
            UnityEditor.Handles.Label(position, text, DebugLabelStyle);
#endif
        }

#if UNITY_EDITOR
        private static GUIStyle _debugLabelStyle;
        private static Texture2D _debugLabelBackground;

        // White text on a dark, slightly transparent box, readable over any scene.
        private static GUIStyle DebugLabelStyle
        {
            get
            {
                if (_debugLabelStyle == null || _debugLabelBackground == null)
                {
                    _debugLabelBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _debugLabelBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
                    _debugLabelBackground.Apply();

                    _debugLabelStyle = new GUIStyle(UnityEditor.EditorStyles.label)
                    {
                        fontSize = 12,
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(6, 6, 3, 3),
                    };
                    _debugLabelStyle.normal.background = _debugLabelBackground;
                    _debugLabelStyle.normal.textColor = Color.white;
                }
                return _debugLabelStyle;
            }
        }
#endif

        private void Log(string message)
        {
            if (!EnableLogging) return;
            if (Binding is UnityEngine.Object context) Debug.Log(message, context);
            else Debug.Log(message);
        }
    }
}
