using UnityEngine;

namespace SimpleStateMachine
{
    public abstract class State<T>
    {
        public StateMachine<T> StateMachine { get; private set; }
        public T Binding { get; private set; }

        public State(StateMachine<T> stateMachine, T binding)
        {
            StateMachine = stateMachine;
            Binding = binding;
        }

        public void NextState<S>() where S : State<T>
        {
            StateMachine.NextState<S>();
        }

        public virtual void Enter() { }

        public virtual void FixedUpdate(float deltaTime) { }

        public virtual void Update(float deltaTime) { }

        public virtual void Exit() { }
    }
}