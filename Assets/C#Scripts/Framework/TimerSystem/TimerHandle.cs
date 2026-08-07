using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.TimerSystem
{
    /// <summary>
    /// 计时器所使用的时间类型。
    /// </summary>
    public enum TimerTimeMode
    {
        /// <summary>
        /// 游戏时间，受 <see cref="Time.timeScale"/> 影响。
        /// </summary>
        Scaled,

        /// <summary>
        /// 真实时间，不受 <see cref="Time.timeScale"/> 影响。
        /// </summary>
        Unscaled
    }

    /// <summary>
    /// 计时器当前状态。
    /// </summary>
    public enum TimerState
    {
        Running,
        Completed,
        Cancelled,
        Faulted
    }

    /// <summary>
    /// 计时器句柄。用于查询状态或取消计时器，不暴露具体调度实现。
    /// </summary>
    public sealed class TimerHandle
    {
        private TimerMgr manager;
        private Coroutine coroutine;
        private Object lifetimeOwner;

        internal TimerHandle(ulong id, TimerMgr manager, Object lifetimeOwner)
        {
            Id = id;
            this.manager = manager;
            this.lifetimeOwner = lifetimeOwner;
            HasLifetimeOwner = !ReferenceEquals(lifetimeOwner, null);
            State = TimerState.Running;
        }

        /// <summary>
        /// 计时器在当前管理器生命周期内的唯一编号。
        /// </summary>
        public ulong Id { get; }

        /// <summary>
        /// 计时器当前状态。
        /// </summary>
        public TimerState State { get; private set; }

        public bool IsRunning => State == TimerState.Running;
        public bool IsCompleted => State == TimerState.Completed;
        public bool IsCancelled => State == TimerState.Cancelled;
        public bool IsFaulted => State == TimerState.Faulted;

        internal Coroutine Coroutine => coroutine;
        internal bool HasLifetimeOwner { get; }
        internal bool IsLifetimeOwnerAlive => !HasLifetimeOwner || lifetimeOwner != null;

        /// <summary>
        /// 取消计时器。已经结束的计时器不会重复取消。
        /// </summary>
        /// <returns>本次调用是否成功将运行中的计时器取消。</returns>
        public bool Cancel()
        {
            if (!IsRunning)
            {
                return false;
            }

            TimerMgr currentManager = manager;
            if (currentManager == null)
            {
                return TrySetTerminalState(TimerState.Cancelled);
            }

            return currentManager.Cancel(this);
        }

        internal bool IsOwnedBy(TimerMgr targetManager)
        {
            return ReferenceEquals(manager, targetManager);
        }

        internal void AttachCoroutine(Coroutine targetCoroutine)
        {
            if (IsRunning)
            {
                coroutine = targetCoroutine;
            }
        }

        internal bool TrySetTerminalState(TimerState terminalState)
        {
            if (!IsRunning || terminalState == TimerState.Running)
            {
                return false;
            }

            State = terminalState;
            manager = null;
            coroutine = null;
            lifetimeOwner = null;
            return true;
        }
    }
}
