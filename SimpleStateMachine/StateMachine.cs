using System;

namespace SimpleStateMachine
{
    public class StateMachine<T>
    {
        public T Binding { get; private set; }
        public State<T> CurrentState { get; private set; }

        public StateMachine(T target)
        {
            Binding = target;
        }

        public void NextState<S>() where S : State<T>
        {
            if (CurrentState != null) CurrentState.Exit();

            State<T> state = (S)Activator.CreateInstance(typeof(S), new object[] { this, Binding });
            CurrentState = state;
            CurrentState.Enter();
        }

        public void FixedUpdate(float deltaTime)
        {
            CurrentState?.FixedUpdate(deltaTime);
        }

        public void Update(float deltaTime)
        {
            CurrentState?.Update(deltaTime);
        }
    }
}