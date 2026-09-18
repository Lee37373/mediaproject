namespace GuildMaster.AI
{
    /// <summary>Base class for FSM states. States are plain objects owned by one agent.</summary>
    public abstract class State<TOwner>
    {
        protected TOwner Owner { get; private set; }
        protected StateMachine<TOwner> Machine { get; private set; }

        /// <summary>Time spent in this state (scaled seconds).</summary>
        protected float TimeInState { get; private set; }

        internal void Bind(TOwner owner, StateMachine<TOwner> machine)
        {
            Owner = owner;
            Machine = machine;
        }

        internal void InternalEnter() { TimeInState = 0f; Enter(); }
        internal void InternalTick(float dt) { TimeInState += dt; Tick(dt); }

        public virtual void Enter() { }
        public virtual void Tick(float dt) { }
        public virtual void Exit() { }

        /// <summary>Short Korean label shown in UI (e.g. "사냥 중").</summary>
        public virtual string Label => GetType().Name;
    }

    /// <summary>Generic finite state machine.</summary>
    public class StateMachine<TOwner>
    {
        readonly TOwner _owner;

        public State<TOwner> Current { get; private set; }
        public State<TOwner> Previous { get; private set; }

        public StateMachine(TOwner owner) { _owner = owner; }

        public void ChangeState(State<TOwner> next)
        {
            if (next == null) return;
            Current?.Exit();
            Previous = Current;
            Current = next;
            next.Bind(_owner, this);
            next.InternalEnter();
        }

        public void Tick(float dt) => Current?.InternalTick(dt);

        public bool IsIn<TState>() where TState : State<TOwner> => Current is TState;
    }
}
