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
        /// <summary>The machine this state belongs to.</summary>
        public StateMachine<T> StateMachine { get; private set; }

        /// <summary>The object the state machine was created for, e.g. your EnemyController.</summary>
        public T Binding { get; private set; }

        /// <summary>
        /// Seconds since this state was last entered, counted from the deltaTime passed to the machine's Update
        /// (FixedUpdate doesn't advance it).
        /// </summary>
        public float TimeInState { get; internal set; }

        internal void Initialize(StateMachine<T> stateMachine, T binding)
        {
            StateMachine = stateMachine;
            Binding = binding;
        }

        /// <summary>Switches the machine to <typeparamref name="S"/>, the same as <c>StateMachine.NextState&lt;S&gt;()</c>.</summary>
        public void NextState<S>() where S : State<T>, new()
        {
            StateMachine.NextState<S>();
        }

        /// <summary>Called when the state is entered. Reset any fields the state changes here.</summary>
        public virtual void Enter() { }

        /// <summary>Called from the machine's FixedUpdate while this is the current state.</summary>
        public virtual void FixedUpdate(float deltaTime) { }

        /// <summary>Called from the machine's Update while this is the current state.</summary>
        public virtual void Update(float deltaTime) { }

        /// <summary>Called when the machine leaves this state, before the next state's Enter.</summary>
        public virtual void Exit() { }
    }
}
