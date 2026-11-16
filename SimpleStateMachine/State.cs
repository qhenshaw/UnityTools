namespace SimpleStateMachine
{
    /// <summary>
    /// One state of a <see cref="StateMachine{T}"/>. Each state type has a single instance per machine that is
    /// reused every time the state is entered, so reset any fields you change in <see cref="Enter"/>.
    /// States need a parameterless constructor; <see cref="StateMachine"/> and <see cref="Binding"/> are set
    /// before the first <see cref="Enter"/>, so don't use them in the constructor.
    /// </summary>
    public abstract class State<T>
    {
        public StateMachine<T> StateMachine { get; private set; }

        /// <summary>The object the state machine was created for, e.g. your EnemyController.</summary>
        public T Binding { get; private set; }

        /// <summary>Seconds since this state was last entered, counted from the deltaTime passed to Update.</summary>
        public float TimeInState { get; internal set; }

        internal void Initialize(StateMachine<T> stateMachine, T binding)
        {
            StateMachine = stateMachine;
            Binding = binding;
        }

        public void NextState<S>() where S : State<T>, new()
        {
            StateMachine.NextState<S>();
        }

        /// <summary>Called when the state is entered. Reset any fields the state changes here.</summary>
        public virtual void Enter() { }

        public virtual void FixedUpdate(float deltaTime) { }

        public virtual void Update(float deltaTime) { }

        public virtual void Exit() { }
    }
}
