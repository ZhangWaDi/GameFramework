using System;
using System.Collections.Generic;
using GameFramework.StateMachine;
using NUnit.Framework;

public sealed class StateMachineTests
{
    private enum TestStateId
    {
        First,
        Second,
        Third
    }

    [Test]
    public void Lifecycle_StartChangeTickAndStop_UsesExpectedOrder()
    {
        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        TestState firstState = new("First");
        TestState secondState = new("Second");
        stateMachine.AddState(TestStateId.First, firstState);
        stateMachine.AddState(TestStateId.Second, secondState);

        stateMachine.Start(TestStateId.First);
        stateMachine.Tick(0.25f);
        bool changed = stateMachine.ChangeState(TestStateId.Second);
        bool stopped = stateMachine.Stop();

        CollectionAssert.AreEqual(new[] { "First.Enter", "First.Tick", "First.Exit", "Second.Enter", "Second.Exit" }, context.Events);
        Assert.IsTrue(changed);
        Assert.IsTrue(stopped);
        Assert.AreEqual(0.25f, firstState.LastDeltaTime);
        Assert.IsFalse(stateMachine.IsRunning);
        Assert.IsNull(stateMachine.CurrentState);
        Assert.IsNull(stateMachine.CurrentStateId);
        Assert.IsFalse(stateMachine.Stop());
    }

    [Test]
    public void Registration_RejectsInvalidDuplicateAndReusedStates()
    {
        Assert.Throws<ArgumentNullException>(() => new StateMachine<TestStateId, TestContext>(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StateMachine<TestStateId, TestContext>(new TestContext(), 0));

        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        TestState state = new("State");
        Assert.Throws<ArgumentNullException>(() => stateMachine.AddState(TestStateId.First, null));
        stateMachine.AddState(TestStateId.First, state);
        Assert.Throws<InvalidOperationException>(() => stateMachine.AddState(TestStateId.First, new TestState("Duplicate")));
        Assert.Throws<InvalidOperationException>(() => stateMachine.AddState(TestStateId.Second, state));

        StateMachine<TestStateId, TestContext> anotherStateMachine = new(new TestContext());
        Assert.Throws<InvalidOperationException>(() => anotherStateMachine.AddState(TestStateId.First, state));
        Assert.AreEqual(1, stateMachine.StateCount);
        Assert.IsTrue(stateMachine.ContainsState(TestStateId.First));
        Assert.AreSame(context, stateMachine.Context);
    }

    [Test]
    public void StartAndChange_RejectInvalidLifecycleAndUnknownStates()
    {
        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        stateMachine.AddState(TestStateId.First, new TestState("First"));

        Assert.Throws<InvalidOperationException>(() => stateMachine.ChangeState(TestStateId.First));
        Assert.Throws<InvalidOperationException>(() => stateMachine.RestartCurrentState());
        Assert.Throws<KeyNotFoundException>(() => stateMachine.Start(TestStateId.Second));

        stateMachine.Start(TestStateId.First);
        Assert.Throws<InvalidOperationException>(() => stateMachine.Start(TestStateId.First));
        Assert.Throws<KeyNotFoundException>(() => stateMachine.ChangeState(TestStateId.Second));
    }

    [Test]
    public void SameState_IsIgnoredUntilExplicitRestart()
    {
        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        stateMachine.AddState(TestStateId.First, new TestState("First"));
        stateMachine.Start(TestStateId.First);

        bool changed = stateMachine.ChangeState(TestStateId.First);
        stateMachine.RestartCurrentState();

        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(new[] { "First.Enter", "First.Exit", "First.Enter" }, context.Events);
    }

    [Test]
    public void ReentrantTransitions_AreQueuedAndProcessedInOrder()
    {
        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        context.StateMachine = stateMachine;
        stateMachine.AddState(TestStateId.First, new TestState("First", () => context.StateMachine.ChangeState(TestStateId.Second)));
        stateMachine.AddState(TestStateId.Second, new TestState("Second", () => context.StateMachine.ChangeState(TestStateId.Third)));
        TestState thirdState = new("Third");
        stateMachine.AddState(TestStateId.Third, thirdState);

        stateMachine.Start(TestStateId.First);

        CollectionAssert.AreEqual(new[] { "First.Enter", "First.Exit", "Second.Enter", "Second.Exit", "Third.Enter" }, context.Events);
        Assert.AreEqual(TestStateId.Third, stateMachine.CurrentStateId);
        Assert.AreSame(thirdState, stateMachine.CurrentState);
    }

    [Test]
    public void ReentrantTransitionLoop_StopsAtConfiguredLimit()
    {
        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context, 4);
        context.StateMachine = stateMachine;
        stateMachine.AddState(TestStateId.First, new TestState("First", () => context.StateMachine.ChangeState(TestStateId.Second)));
        stateMachine.AddState(TestStateId.Second, new TestState("Second", () => context.StateMachine.ChangeState(TestStateId.First)));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => stateMachine.Start(TestStateId.First));

        StringAssert.Contains("连续切换超过 4 次", exception.Message);
        Assert.IsTrue(stateMachine.IsRunning);
        Assert.AreEqual(TestStateId.Second, stateMachine.CurrentStateId);
    }

    [Test]
    public void StopRequestedDuringEnter_DiscardsLaterQueuedTransitions()
    {
        TestContext context = new();
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        context.StateMachine = stateMachine;
        stateMachine.AddState(TestStateId.First, new TestState("First", () =>
        {
            context.StateMachine.Stop();
            context.StateMachine.ChangeState(TestStateId.Second);
        }));
        stateMachine.AddState(TestStateId.Second, new TestState("Second"));

        stateMachine.Start(TestStateId.First);

        CollectionAssert.AreEqual(new[] { "First.Enter", "First.Exit" }, context.Events);
        Assert.IsFalse(stateMachine.IsRunning);
        Assert.IsNull(stateMachine.CurrentStateId);
    }

    [Test]
    public void EnterFailure_ClearsCurrentStateAndLeavesMachineReusable()
    {
        TestContext context = new();
        bool shouldThrow = true;
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        stateMachine.AddState(TestStateId.First, new TestState("First", () =>
        {
            if (shouldThrow)
            {
                throw new TestStateException();
            }
        }));

        Assert.Throws<TestStateException>(() => stateMachine.Start(TestStateId.First));
        Assert.IsFalse(stateMachine.IsRunning);
        Assert.IsNull(stateMachine.CurrentState);

        shouldThrow = false;
        stateMachine.Start(TestStateId.First);
        Assert.IsTrue(stateMachine.IsRunning);
    }

    [Test]
    public void ExitFailure_KeepsPreviousStateAndReleasesTransitionGuard()
    {
        TestContext context = new();
        bool shouldThrow = true;
        StateMachine<TestStateId, TestContext> stateMachine = new(context);
        TestState firstState = new("First", onExit: () =>
        {
            if (shouldThrow)
            {
                throw new TestStateException();
            }
        });
        stateMachine.AddState(TestStateId.First, firstState);
        stateMachine.AddState(TestStateId.Second, new TestState("Second"));
        stateMachine.Start(TestStateId.First);

        Assert.Throws<TestStateException>(() => stateMachine.ChangeState(TestStateId.Second));
        Assert.AreSame(firstState, stateMachine.CurrentState);
        Assert.AreEqual(TestStateId.First, stateMachine.CurrentStateId);

        shouldThrow = false;
        Assert.IsTrue(stateMachine.ChangeState(TestStateId.Second));
        Assert.AreEqual(TestStateId.Second, stateMachine.CurrentStateId);
    }

    private sealed class TestContext
    {
        public List<string> Events { get; } = new();
        public StateMachine<TestStateId, TestContext> StateMachine { get; set; }
    }

    private sealed class TestState : State<TestContext>
    {
        private readonly string stateName;
        private readonly Action onEnter;
        private readonly Action onExit;

        public TestState(string stateName, Action onEnter = null, Action onExit = null)
        {
            this.stateName = stateName;
            this.onEnter = onEnter;
            this.onExit = onExit;
        }

        public float LastDeltaTime { get; private set; }

        protected override void OnEnter()
        {
            Context.Events.Add($"{stateName}.Enter");
            onEnter?.Invoke();
        }

        protected override void OnTick(float deltaTime)
        {
            LastDeltaTime = deltaTime;
            Context.Events.Add($"{stateName}.Tick");
        }

        protected override void OnExit()
        {
            Context.Events.Add($"{stateName}.Exit");
            onExit?.Invoke();
        }
    }

    private sealed class TestStateException : Exception
    {
    }
}
