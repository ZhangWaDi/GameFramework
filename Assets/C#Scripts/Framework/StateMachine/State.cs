using System;

namespace GameFramework.StateMachine
{
    /// <summary>
    /// 持有业务上下文并接收状态生命周期的纯 C# 状态基类。
    /// </summary>
    /// <typeparam name="TContext">状态所属的业务上下文类型。</typeparam>
    public abstract class State<TContext>
    {
        private bool isAttached;

        /// <summary>
        /// 获取当前状态机持有的业务上下文。
        /// </summary>
        protected TContext Context { get; private set; }

        internal void Attach(TContext context)
        {
            if (isAttached)
            {
                throw new InvalidOperationException($"状态“{GetType().FullName}”已经注册到状态机，不能重复注册或跨状态机复用。");
            }

            Context = context;
            isAttached = true;
        }

        internal void Enter()
        {
            OnEnter();
        }

        internal void Tick(float deltaTime)
        {
            OnTick(deltaTime);
        }

        internal void Exit()
        {
            OnExit();
        }

        /// <summary>
        /// 当前状态被进入时调用。
        /// </summary>
        protected virtual void OnEnter()
        {
        }

        /// <summary>
        /// 当前状态由外部状态机驱动更新时调用。
        /// </summary>
        protected virtual void OnTick(float deltaTime)
        {
        }

        /// <summary>
        /// 当前状态被退出或状态机停止时调用。
        /// </summary>
        protected virtual void OnExit()
        {
        }
    }
}
