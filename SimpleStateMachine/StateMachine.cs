using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleStateMachine
{
    public class StateMachine<T>
    {
        private class StateTransition
        {
            public Type From { get; set; }
            public Type To { get; set; }
            public Func<bool> Condition { get; set; }

            public StateTransition(Type from, Type to, Func<bool> condition)
            {
                From = from;
                To = to;
                Condition = condition;
            }

            public override bool Equals(object obj)
            {
                if(obj != null && 
                    obj is StateTransition trans && 
                    trans.From == From && 
                    trans.To == To 
                    && trans.Condition == Condition) return true;
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(From, To, Condition);
            }
        }

        public T Binding { get; private set; }
        public State<T> CurrentState { get; private set; }
        public bool EnableLogging { get; set; } = false;
        public bool UpdateConditionsCheck { get; set; } = true;
        public bool FixedUpdateConditionsCheck { get; set; } = false;

        public event EventHandler<State<T>> OnStateChanged;

        private List<StateTransition> _transitions = new List<StateTransition>();

        public StateMachine(T binding)
        {
            Binding = binding;
        }

        public StateMachine(T binding, bool enableLogging) : this(binding)
        {
            EnableLogging = enableLogging;
        }

        public void AddTransition<U,V>(Func<bool> condition) where U : State<T> where V : State<T>
        {
            StateTransition transition = new StateTransition(typeof(U), typeof(V), condition);
            _transitions.Add(transition);
            Log($"{Binding} SM transition added: from {typeof(U)} to {typeof(V)}", Binding);
        }

        public void RemoveTransition<U,V>(Func<bool> condition) where U : State<T> where V : State<T>
        {
            StateTransition transition = new StateTransition(typeof(U), typeof(V), condition);
            if (_transitions.Contains(transition))
            {
                _transitions.Remove(transition);
                Log($"{Binding} SM transition removed: from {typeof(U)} to {typeof(V)}", Binding);
            }
        }

        public void NextState<S>() where S : State<T>
        {
            NextState(typeof(S));
        }

        private void NextState(Type newState)
        {
            if(CurrentState != null)
            {
                CurrentState.Exit();
                Log($"{Binding} SM Exiting State: {CurrentState}", Binding);
            }

            State<T> state = (State<T>)Activator.CreateInstance(newState, new object[] { this, Binding });
            CurrentState = state;
            CurrentState.Enter();
            Log($"{Binding} SM Entering State: {CurrentState}", Binding);
            OnStateChanged?.Invoke(this, CurrentState);
        }

        private void Log(string message, object target)
        {
            if (!EnableLogging) return;
            if (target is UnityEngine.Object uo) Debug.Log(message, uo);
            else Debug.Log(message);
        }

        public void FixedUpdate(float deltaTime)
        {
            CurrentState?.FixedUpdate(deltaTime);
            if (FixedUpdateConditionsCheck) CheckConditions();
        }

        public void Update(float deltaTime)
        {
            CurrentState?.Update(deltaTime);
            if (UpdateConditionsCheck) CheckConditions();
        }

        private void CheckConditions()
        {
            for (int i = _transitions.Count - 1; i >= 0; i--)
            {
                StateTransition transition = _transitions[i];
                if (CurrentState.GetType() == transition.From && transition.Condition.Invoke())
                {
                    NextState(transition.To);
                    return;
                }
            }

            return;
        }
    }
}