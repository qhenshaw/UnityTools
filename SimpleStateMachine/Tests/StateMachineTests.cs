using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SimpleStateMachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityTools.SimpleStateMachine.Tests
{
    /// <summary>Stands in for the MonoBehaviour a state machine is bound to.</summary>
    public class TestOwner
    {
        public readonly List<string> Log = new List<string>();
        public bool GoToB;
        public bool GoToC;
        public bool Dead;
        public System.Action OnEnterA;
        public System.Action OnExitA;
    }

    public class StateA : State<TestOwner>
    {
        public int Counter;

        public override void Enter()
        {
            Binding.Log.Add("A.Enter");
            Binding.OnEnterA?.Invoke();
        }

        public override void Exit()
        {
            Binding.Log.Add("A.Exit");
            Binding.OnExitA?.Invoke();
        }

        public override void Update(float deltaTime)
        {
            Counter++;
            Binding.Log.Add("A.Update");
        }

        public override void FixedUpdate(float deltaTime) => Binding.Log.Add("A.FixedUpdate");
    }

    public class StateB : State<TestOwner>
    {
        public bool Done;
        public override void Enter() => Binding.Log.Add("B.Enter");
        public override void Exit() => Binding.Log.Add("B.Exit");
    }

    public class StateC : State<TestOwner>
    {
        public override void Enter() => Binding.Log.Add("C.Enter");
        public override void Exit() => Binding.Log.Add("C.Exit");
    }

    public class DeadState : State<TestOwner>
    {
        public override void Enter() => Binding.Log.Add("Dead.Enter");
    }

    public class StateMachineTests
    {
        private TestOwner _owner;
        private StateMachine<TestOwner> _machine;

        [SetUp]
        public void SetUp()
        {
            _owner = new TestOwner();
            _machine = new StateMachine<TestOwner>(_owner);
        }

        // ---------- Entering states ----------

        [Test]
        public void NextState_EntersState_WithBindingAndMachineSet()
        {
            _machine.NextState<StateA>();

            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
            Assert.AreSame(_owner, _machine.CurrentState.Binding);
            Assert.AreSame(_machine, _machine.CurrentState.StateMachine);
            CollectionAssert.AreEqual(new[] { "A.Enter" }, _owner.Log);
        }

        [Test]
        public void NextState_ExitsCurrentBeforeEnteringNext()
        {
            _machine.NextState<StateA>();
            _machine.NextState<StateB>();

            CollectionAssert.AreEqual(new[] { "A.Enter", "A.Exit", "B.Enter" }, _owner.Log);
        }

        [Test]
        public void StateInstances_AreReusedPerType()
        {
            _machine.NextState<StateA>();
            State<TestOwner> first = _machine.CurrentState;
            _machine.NextState<StateB>();
            _machine.NextState<StateA>();

            Assert.AreSame(first, _machine.CurrentState);
        }

        [Test]
        public void StateFields_AreKeptBetweenEntries()
        {
            _machine.NextState<StateA>();
            _machine.Update(0.1f);
            _machine.Update(0.1f);
            _machine.NextState<StateB>();
            _machine.NextState<StateA>();

            Assert.AreEqual(2, ((StateA)_machine.CurrentState).Counter);
        }

        [Test]
        public void SeparateMachines_HaveSeparateStateInstances()
        {
            var other = new StateMachine<TestOwner>(new TestOwner());
            _machine.NextState<StateA>();
            other.NextState<StateA>();

            Assert.AreNotSame(_machine.CurrentState, other.CurrentState);
        }

        [Test]
        public void NextState_ToCurrentState_ReEntersIt()
        {
            _machine.NextState<StateA>();
            _machine.Update(1f);
            _owner.Log.Clear();

            _machine.NextState<StateA>();

            CollectionAssert.AreEqual(new[] { "A.Exit", "A.Enter" }, _owner.Log);
            Assert.AreEqual(0f, _machine.CurrentState.TimeInState);
        }

        [Test]
        public void PreviousState_IsTheStateBeforeTheLastChange()
        {
            _machine.NextState<StateA>();
            Assert.IsNull(_machine.PreviousState);

            _machine.NextState<StateB>();

            Assert.IsInstanceOf<StateA>(_machine.PreviousState);
            Assert.IsInstanceOf<StateB>(_machine.CurrentState);
        }

        [Test]
        public void OnStateChanged_FiresWithNewState()
        {
            var changes = new List<State<TestOwner>>();
            _machine.OnStateChanged += (sender, state) => changes.Add(state);

            _machine.NextState<StateA>();
            _machine.NextState<StateB>();

            Assert.AreEqual(2, changes.Count);
            Assert.IsInstanceOf<StateA>(changes[0]);
            Assert.IsInstanceOf<StateB>(changes[1]);
        }

        [Test]
        public void State_NextState_SwitchesTheMachine()
        {
            _machine.NextState<StateA>();

            _machine.CurrentState.NextState<StateB>();

            Assert.IsInstanceOf<StateB>(_machine.CurrentState);
        }

        // ---------- Switching from inside Enter/Exit ----------

        [Test]
        public void NextState_FromInsideEnter_HappensAfterTheCurrentSwitch()
        {
            _owner.OnEnterA = () => _machine.NextState<StateB>();
            var changes = new List<string>();
            _machine.OnStateChanged += (sender, state) => changes.Add(state.GetType().Name);

            _machine.NextState<StateA>();

            CollectionAssert.AreEqual(new[] { "A.Enter", "A.Exit", "B.Enter" }, _owner.Log);
            CollectionAssert.AreEqual(new[] { "StateA", "StateB" }, changes);
            Assert.IsInstanceOf<StateB>(_machine.CurrentState);
            Assert.IsInstanceOf<StateA>(_machine.PreviousState);
        }

        [Test]
        public void NextState_FromInsideExit_HappensAfterTheCurrentSwitch()
        {
            _machine.NextState<StateA>();
            _owner.OnExitA = () => _machine.NextState<StateC>();
            _owner.Log.Clear();

            _machine.NextState<StateB>();

            CollectionAssert.AreEqual(new[] { "A.Exit", "B.Enter", "B.Exit", "C.Enter" }, _owner.Log);
            Assert.IsInstanceOf<StateC>(_machine.CurrentState);
        }

        // ---------- Update ----------

        [Test]
        public void Update_BeforeFirstState_DoesNothing()
        {
            _machine.AddTransition<StateA, StateB>(() => true);

            Assert.DoesNotThrow(() => _machine.Update(0.1f));
            Assert.DoesNotThrow(() => _machine.FixedUpdate(0.1f));
            Assert.IsNull(_machine.CurrentState);
        }

        [Test]
        public void Update_And_FixedUpdate_ReachTheCurrentState()
        {
            _machine.NextState<StateA>();
            _owner.Log.Clear();

            _machine.Update(0.1f);
            _machine.FixedUpdate(0.02f);

            CollectionAssert.AreEqual(new[] { "A.Update", "A.FixedUpdate" }, _owner.Log);
        }

        [Test]
        public void TimeInState_CountsUpdateDeltaTime_NotFixedUpdate()
        {
            _machine.NextState<StateA>();

            _machine.Update(0.25f);
            _machine.Update(0.5f);
            _machine.FixedUpdate(10f);

            Assert.AreEqual(0.75f, _machine.CurrentState.TimeInState, 0.0001f);
        }

        [Test]
        public void TimeInState_ResetsOnEnter()
        {
            _machine.NextState<StateA>();
            _machine.Update(1f);
            _machine.NextState<StateB>();
            _machine.NextState<StateA>();

            Assert.AreEqual(0f, _machine.CurrentState.TimeInState);
        }

        // ---------- Transitions ----------

        [Test]
        public void Transition_FiresWhenConditionIsTrue()
        {
            _machine.AddTransition<StateA, StateB>(() => _owner.GoToB);
            _machine.NextState<StateA>();

            _machine.Update(0.1f);
            Assert.IsInstanceOf<StateA>(_machine.CurrentState);

            _owner.GoToB = true;
            _machine.Update(0.1f);
            Assert.IsInstanceOf<StateB>(_machine.CurrentState);
        }

        [Test]
        public void Transition_OnlyFiresFromItsOwnState()
        {
            _machine.AddTransition<StateB, StateC>(() => true);
            _machine.NextState<StateA>();

            _machine.Update(0.1f);

            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
        }

        [Test]
        public void Transitions_LastAddedWins()
        {
            _owner.GoToB = true;
            _owner.GoToC = true;
            _machine.AddTransition<StateA, StateB>(() => _owner.GoToB);
            _machine.AddTransition<StateA, StateC>(() => _owner.GoToC);
            _machine.NextState<StateA>();

            _machine.Update(0.1f);

            Assert.IsInstanceOf<StateC>(_machine.CurrentState);
        }

        [Test]
        public void FixedUpdateConditionsCheck_ChecksTransitionsInFixedUpdate()
        {
            _machine.UpdateConditionsCheck = false;
            _machine.FixedUpdateConditionsCheck = true;
            _machine.AddTransition<StateA, StateB>(() => true);
            _machine.NextState<StateA>();

            _machine.Update(0.1f);
            Assert.IsInstanceOf<StateA>(_machine.CurrentState);

            _machine.FixedUpdate(0.02f);
            Assert.IsInstanceOf<StateB>(_machine.CurrentState);
        }

        [Test]
        public void TypedCondition_GetsTheCurrentStateAsItsType()
        {
            _machine.AddTransition<StateB, StateA>(b => b.Done);
            _machine.NextState<StateB>();

            _machine.Update(0.1f);
            Assert.IsInstanceOf<StateB>(_machine.CurrentState);

            ((StateB)_machine.CurrentState).Done = true;
            _machine.Update(0.1f);
            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
        }

        [Test]
        public void TypedCondition_CanUseTimeInState()
        {
            _machine.AddTransition<StateA, StateB>(a => a.TimeInState >= 1f);
            _machine.NextState<StateA>();

            _machine.Update(0.6f);
            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
            _machine.Update(0.6f);
            Assert.IsInstanceOf<StateB>(_machine.CurrentState);
        }

        // ---------- Any-state transitions ----------

        [Test]
        public void AnyTransition_FiresFromEveryState()
        {
            _machine.AddTransitionFromAny<DeadState>(() => _owner.Dead);

            foreach (System.Action enter in new System.Action[] { _machine.NextState<StateA>, _machine.NextState<StateB> })
            {
                _owner.Dead = false;
                enter();
                _owner.Dead = true;
                _machine.Update(0.1f);
                Assert.IsInstanceOf<DeadState>(_machine.CurrentState);
            }
        }

        [Test]
        public void AnyTransition_IsCheckedBeforeNormalTransitions()
        {
            _owner.Dead = true;
            _machine.AddTransitionFromAny<DeadState>(() => _owner.Dead);
            _machine.AddTransition<StateA, StateB>(() => true);
            _machine.NextState<StateA>();

            _machine.Update(0.1f);

            Assert.IsInstanceOf<DeadState>(_machine.CurrentState);
        }

        [Test]
        public void AnyTransition_DoesNotReEnterItsTargetState()
        {
            _owner.Dead = true;
            _machine.AddTransitionFromAny<DeadState>(() => _owner.Dead);
            _machine.NextState<StateA>();
            _machine.Update(0.1f);
            _owner.Log.Clear();

            _machine.Update(0.1f);
            _machine.Update(0.1f);

            CollectionAssert.IsEmpty(_owner.Log);
        }

        // ---------- Removing transitions ----------

        [Test]
        public void RemoveTransition_WithHandle_RemovesIt()
        {
            var transition = _machine.AddTransition<StateA, StateB>(() => true);
            _machine.NextState<StateA>();

            Assert.IsTrue(_machine.RemoveTransition(transition));
            _machine.Update(0.1f);

            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
            Assert.IsFalse(_machine.RemoveTransition(transition));
        }

        [Test]
        public void RemoveTransition_WithHandle_RemovesAnyTransition()
        {
            var transition = _machine.AddTransitionFromAny<DeadState>(() => true);
            _machine.NextState<StateA>();

            _machine.RemoveTransition(transition);
            _machine.Update(0.1f);

            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
        }

        [Test]
        public void RemoveTransitions_RemovesEveryTransitionBetweenTwoStates()
        {
            _machine.AddTransition<StateA, StateB>(() => true);
            _machine.AddTransition<StateA, StateB>(() => true);
            _machine.AddTransition<StateA, StateC>(() => false);

            Assert.AreEqual(2, _machine.RemoveTransitions<StateA, StateB>());
            _machine.NextState<StateA>();
            _machine.Update(0.1f);

            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
        }

        [Test]
        public void ObsoleteRemoveTransition_WithSameDelegate_RemovesIt()
        {
            System.Func<bool> condition = () => true;
            _machine.AddTransition<StateA, StateB>(condition);
#pragma warning disable CS0618
            _machine.RemoveTransition<StateA, StateB>(condition);
#pragma warning restore CS0618
            _machine.NextState<StateA>();
            _machine.Update(0.1f);

            Assert.IsInstanceOf<StateA>(_machine.CurrentState);
        }

        [Test]
        public void ObsoleteRemoveTransition_WithNewLambda_WarnsThatNothingWasRemoved()
        {
            _machine.AddTransition<StateA, StateB>(() => true);

            LogAssert.Expect(LogType.Warning, new Regex("nothing was removed"));
#pragma warning disable CS0618
            _machine.RemoveTransition<StateA, StateB>(() => true);
#pragma warning restore CS0618
        }

        [Test]
        public void TransitionHandle_ReportsItsStates()
        {
            var transition = _machine.AddTransition<StateA, StateB>(() => true);
            var any = _machine.AddTransitionFromAny<DeadState>(() => true);

            Assert.AreEqual(typeof(StateA), transition.From);
            Assert.AreEqual(typeof(StateB), transition.To);
            Assert.IsNull(any.From);
        }

        [Test]
        public void ConditionThatRemovesTransitions_DoesNotBreakTheCheck()
        {
            StateMachine<TestOwner>.Transition handle = null;
            _machine.AddTransition<StateA, StateB>(() => false);
            handle = _machine.AddTransition<StateA, StateC>(() =>
            {
                _machine.RemoveTransition(handle);
                return false;
            });
            _machine.NextState<StateA>();

            Assert.DoesNotThrow(() => _machine.Update(0.1f));
        }
    }
}
