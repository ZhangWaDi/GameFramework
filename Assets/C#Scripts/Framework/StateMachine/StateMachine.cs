using System;
using System.Collections.Generic;

namespace GameFramework.StateMachine
{
    /// <summary>
    /// 使用枚举标识状态、由外部主动驱动的纯 C# 轻量状态机。
    /// </summary>
    /// <typeparam name="TStateId">状态枚举类型。</typeparam>
    /// <typeparam name="TContext">所有状态共享的业务上下文类型。</typeparam>
    public sealed class StateMachine<TStateId, TContext> where TStateId : struct, Enum
    {
        public const int DefaultMaxTransitionsPerOperation = 32;

        private readonly Dictionary<TStateId, State<TContext>> states = new();
        private readonly Queue<TransitionRequest> pendingTransitions = new();
        private readonly TContext context;
        private readonly int maxTransitionsPerOperation;

        private State<TContext> currentState;
        private TStateId currentStateId;
        private bool hasCurrentState;
        private bool isTransitioning;

        /// <summary>
        /// 创建状态机并保存所有状态共享的业务上下文。
        /// </summary>
        public StateMachine(TContext context, int maxTransitionsPerOperation = DefaultMaxTransitionsPerOperation)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (maxTransitionsPerOperation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTransitionsPerOperation), maxTransitionsPerOperation, "单次操作允许的最大连续切换次数必须大于 0。");
            }

            this.context = context;
            this.maxTransitionsPerOperation = maxTransitionsPerOperation;
        }

        /// <summary>
        /// 获取所有状态共享的业务上下文。
        /// </summary>
        public TContext Context => context;

        /// <summary>
        /// 当前是否存在已经进入的状态。
        /// </summary>
        public bool IsRunning => hasCurrentState;

        /// <summary>
        /// 获取当前状态标识；状态机未启动或已经停止时返回 null。
        /// </summary>
        public TStateId? CurrentStateId => hasCurrentState ? currentStateId : null;

        /// <summary>
        /// 获取当前状态实例；状态机未启动或已经停止时返回 null。
        /// </summary>
        public State<TContext> CurrentState => currentState;

        /// <summary>
        /// 获取已注册状态数量。
        /// </summary>
        public int StateCount => states.Count;

        /// <summary>
        /// 注册状态。状态实例只能注册一次，也不能在多个状态机间复用。
        /// </summary>
        public void AddState(TStateId stateId, State<TContext> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (states.ContainsKey(stateId))
            {
                throw new InvalidOperationException($"状态标识“{stateId}”已经注册。");
            }

            state.Attach(context);
            states.Add(stateId, state);
        }

        /// <summary>
        /// 判断指定状态是否已经注册。
        /// </summary>
        public bool ContainsState(TStateId stateId)
        {
            return states.ContainsKey(stateId);
        }

        /// <summary>
        /// 进入初始状态。状态机已经运行时不能再次启动。
        /// </summary>
        public void Start(TStateId initialStateId)
        {
            if (hasCurrentState || isTransitioning)
            {
                throw new InvalidOperationException("状态机已经启动，不能重复调用 Start。");
            }

            EnsureStateRegistered(initialStateId);
            ProcessTransitions(TransitionRequest.Change(initialStateId));
        }

        /// <summary>
        /// 切换状态。目标为当前状态时不重复执行生命周期并返回 false。
        /// </summary>
        public bool ChangeState(TStateId targetStateId)
        {
            if (!hasCurrentState)
            {
                throw new InvalidOperationException("状态机尚未启动，请先调用 Start。");
            }

            EnsureStateRegistered(targetStateId);
            if (EqualityComparer<TStateId>.Default.Equals(currentStateId, targetStateId))
            {
                return false;
            }

            SubmitTransition(TransitionRequest.Change(targetStateId));
            return true;
        }

        /// <summary>
        /// 退出并重新进入请求时的当前状态。
        /// </summary>
        public void RestartCurrentState()
        {
            if (!hasCurrentState)
            {
                throw new InvalidOperationException("状态机尚未启动，不能重新进入当前状态。");
            }

            SubmitTransition(TransitionRequest.Restart(currentStateId));
        }

        /// <summary>
        /// 驱动当前状态更新。状态机未运行时不执行任何操作。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!hasCurrentState)
            {
                return;
            }

            currentState.Tick(deltaTime);
        }

        /// <summary>
        /// 退出当前状态并停止状态机。已经停止时返回 false。
        /// </summary>
        public bool Stop()
        {
            if (!hasCurrentState)
            {
                return false;
            }

            SubmitTransition(TransitionRequest.Stop());
            return true;
        }

        private void SubmitTransition(TransitionRequest request)
        {
            if (isTransitioning)
            {
                pendingTransitions.Enqueue(request);
                return;
            }

            ProcessTransitions(request);
        }

        private void ProcessTransitions(TransitionRequest initialRequest)
        {
            pendingTransitions.Enqueue(initialRequest);
            isTransitioning = true;
            int transitionCount = 0;

            try
            {
                while (pendingTransitions.Count > 0)
                {
                    if (transitionCount >= maxTransitionsPerOperation)
                    {
                        throw new InvalidOperationException($"单次状态机操作连续切换超过 {maxTransitionsPerOperation} 次，可能存在循环切换。");
                    }

                    TransitionRequest request = pendingTransitions.Dequeue();
                    transitionCount++;
                    if (request.Type == TransitionRequestType.Stop)
                    {
                        ExecuteStop();
                    }
                    else
                    {
                        ExecuteChange(request.StateId, request.Type == TransitionRequestType.Restart);
                    }
                }
            }
            finally
            {
                pendingTransitions.Clear();
                isTransitioning = false;
            }
        }

        private void ExecuteChange(TStateId targetStateId, bool forceRestart)
        {
            if (hasCurrentState && !forceRestart && EqualityComparer<TStateId>.Default.Equals(currentStateId, targetStateId))
            {
                return;
            }

            State<TContext> targetState = states[targetStateId];
            if (hasCurrentState)
            {
                currentState.Exit();
            }

            currentState = targetState;
            currentStateId = targetStateId;
            hasCurrentState = true;

            try
            {
                targetState.Enter();
            }
            catch
            {
                currentState = null;
                currentStateId = default;
                hasCurrentState = false;
                throw;
            }
        }

        private void ExecuteStop()
        {
            if (!hasCurrentState)
            {
                return;
            }

            currentState.Exit();
            currentState = null;
            currentStateId = default;
            hasCurrentState = false;
            pendingTransitions.Clear();
        }

        private void EnsureStateRegistered(TStateId stateId)
        {
            if (!states.ContainsKey(stateId))
            {
                throw new KeyNotFoundException($"状态标识“{stateId}”尚未注册。");
            }
        }

        private enum TransitionRequestType
        {
            Change,
            Restart,
            Stop
        }

        private readonly struct TransitionRequest
        {
            private TransitionRequest(TransitionRequestType type, TStateId stateId)
            {
                Type = type;
                StateId = stateId;
            }

            public TransitionRequestType Type { get; }
            public TStateId StateId { get; }

            public static TransitionRequest Change(TStateId stateId)
            {
                return new(TransitionRequestType.Change, stateId);
            }

            public static TransitionRequest Restart(TStateId stateId)
            {
                return new(TransitionRequestType.Restart, stateId);
            }

            public static TransitionRequest Stop()
            {
                return new(TransitionRequestType.Stop, default);
            }
        }
    }
}
