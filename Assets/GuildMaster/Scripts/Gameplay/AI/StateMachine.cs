namespace GuildMaster.AI
{
    /// <summary>Base class for FSM states. States are plain objects owned by one agent.</summary>
    public abstract class State<TOwner>
    {
        protected TOwner Owner { get; private set; }
        protected StateMachine<TOwner> Machine { get; private set; }

        /// <summary>Time spent in this state (scaled seconds).</summary>
        protected float TimeInState { get; private set; }
        /// <summary>The same, for watchers outside the state (debug: <c>Session.AnomalyWatch</c>).</summary>
        public float Elapsed => TimeInState;

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

        // ---------------- Save (P5-4) ----------------

        /// <summary>What this state needs to carry on after a load (JSON), or null when entering afresh will do.</summary>
        public virtual string SaveState() => null;

        /// <summary>
        /// Takes up the state again after a load. Default: <see cref="Enter"/> — right for states whose
        /// Enter only plans (timers, a path). States whose Enter acts (sells, pays, speaks) or that
        /// carry something they cannot work out again override this.
        /// </summary>
        public virtual void ResumeState(string json, Core.SaveContext ctx) => Enter();

        internal void RestoreElapsed(float seconds) => TimeInState = seconds;

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

        public void Tick(float dt)
        {
            var state = Current;
            if (state == null) return;
            using (MarkerFor(state.GetType()).Auto()) state.InternalTick(dt);
        }

        /// <summary>One profiler marker per state type ("State.MonsterEngageState"), so the profiler shows what each state costs (P5-2).</summary>
        static readonly System.Collections.Generic.Dictionary<System.Type, Unity.Profiling.ProfilerMarker> s_markers = new();

        static Unity.Profiling.ProfilerMarker MarkerFor(System.Type type)
        {
            if (!s_markers.TryGetValue(type, out var marker))
                s_markers[type] = marker = new Unity.Profiling.ProfilerMarker("State." + type.Name);
            return marker;
        }

        public bool IsIn<TState>() where TState : State<TOwner> => Current is TState;

        /// <summary>
        /// Puts a saved state back as the current one (P5-4): no Exit of what was there (a boot
        /// placeholder), the time spent in it restored, then <see cref="State{TOwner}.ResumeState"/>.
        /// </summary>
        public void Resume(State<TOwner> state, float elapsed, string json, Core.SaveContext ctx)
        {
            if (state == null) return;
            Previous = null;
            Current = state;
            state.Bind(_owner, this);
            state.RestoreElapsed(elapsed);
            state.ResumeState(json, ctx);
        }
    }
}
