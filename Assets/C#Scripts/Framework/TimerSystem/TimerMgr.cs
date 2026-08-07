using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.TimerSystem
{
    /// <summary>
    /// 为运行中的项目提供统一的主线程计时入口。
    /// </summary>
    public sealed class TimerMgr : SingletonMono<TimerMgr>
    {
        private readonly Dictionary<ulong, TimerHandle> activeTimers = new();
        private ulong nextTimerId = 1;
        public int ActiveTimerCount => activeTimers.Count;

        protected override void OnDestroy()
        {
            CancelAllTimers(false);
            base.OnDestroy();
        }

        #region 外部接口
        /// <summary>
        /// 延迟指定时间后执行一次回调。
        /// 当 <paramref name="seconds"/> 为零时，回调会在后续帧执行。
        /// </summary>
        public TimerHandle Delay(
            float seconds,
            Action callback,
            TimerTimeMode timeMode = TimerTimeMode.Scaled,
            Object owner = null)
        {
            return StartTimer(
                seconds,
                nameof(seconds),
                1,
                false,
                callback,
                timeMode,
                null,
                owner);
        }

        /// <summary>
        /// 按固定间隔执行指定次数的回调。
        /// <paramref name="repeatCount"/> 表示回调的执行次数。
        /// </summary>
        public TimerHandle Repeat(
            float interval,
            int repeatCount,
            Action callback,
            TimerTimeMode timeMode = TimerTimeMode.Scaled,
            Action onCompleted = null,
            Object owner = null)
        {
            if (repeatCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(repeatCount), repeatCount, "重复次数必须大于零。");
            }

            return StartTimer(
                interval,
                nameof(interval),
                repeatCount,
                false,
                callback,
                timeMode,
                onCompleted,
                owner);
        }

        /// <summary>
        /// 按固定间隔持续执行回调，直到计时器被取消。
        /// </summary>
        public TimerHandle RepeatForever(
            float interval,
            Action callback,
            TimerTimeMode timeMode = TimerTimeMode.Scaled,
            Object owner = null)
        {
            return StartTimer(
                interval,
                nameof(interval),
                0,
                true,
                callback,
                timeMode,
                null,
                owner);
        }

        /// <summary>
        /// 取消指定计时器。
        /// </summary>
        /// <returns>本次调用是否成功将运行中的计时器取消。</returns>
        public bool Cancel(TimerHandle handle)
        {
            if (handle == null || !handle.IsOwnedBy(this) || !handle.IsRunning)
            {
                return false;
            }

            return CancelTimer(handle, true);
        }

        /// <summary>
        /// 取消当前管理器中的所有计时器。
        /// 取消不会触发计时器的完成回调。
        /// </summary>
        public void CancelAll()
        {
            CancelAllTimers(true);
        }
        #endregion

        #region 内部实现
        private TimerHandle StartTimer(
            float interval,
            string intervalParameterName,
            int repeatCount,
            bool repeatForever,
            Action callback,
            TimerTimeMode timeMode,
            Action onCompleted,
            Object owner)
        {
            ValidateArguments(
                interval,
                intervalParameterName,
                callback,
                timeMode,
                owner);

            ulong timerId = GetNextTimerId();
            TimerHandle handle = new(timerId, this, owner);
            activeTimers.Add(timerId, handle);

            try
            {
                Coroutine coroutine = StartCoroutine(RunTimer(handle, interval, repeatCount, repeatForever, callback, timeMode, onCompleted));
                handle.AttachCoroutine(coroutine);
                return handle;
            }
            catch
            {
                activeTimers.Remove(timerId);
                handle.TrySetTerminalState(TimerState.Faulted);
                throw;
            }
        }

        private IEnumerator RunTimer(
            TimerHandle handle,
            float interval,
            int repeatCount,
            bool repeatForever,
            Action callback,
            TimerTimeMode timeMode,
            Action onCompleted)
        {
            object waitInstruction = CreateWaitInstruction(interval, timeMode);
            int executedCount = 0;

            try
            {
                while (handle.IsRunning && (repeatForever || executedCount < repeatCount))
                {
                    yield return waitInstruction;

                    if (!handle.IsRunning)
                    {
                        yield break;
                    }

                    if (!handle.IsLifetimeOwnerAlive)
                    {
                        CancelTimer(handle, false);
                        yield break;
                    }

                    if (!TryInvokeCallback(handle, callback))
                    {
                        yield break;
                    }

                    executedCount++;
                }

                if (handle.IsRunning)
                {
                    CompleteTimer(handle, onCompleted);
                }
            }
            finally
            {
                // 防止协程被管理器之外的代码停止时留下失效句柄。
                if (handle.IsRunning)
                {
                    CancelTimer(handle, false);
                }
            }
        }

        private bool TryInvokeCallback(TimerHandle handle, Action callback)
        {
            try
            {
                callback.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                if (FinishTimer(handle, TimerState.Faulted))
                {
                    Debug.LogException(exception, this);
                }

                return false;
            }
        }

        private void CompleteTimer(TimerHandle handle, Action onCompleted)
        {
            if (!FinishTimer(handle, TimerState.Completed) || onCompleted == null)
            {
                return;
            }

            try
            {
                onCompleted.Invoke();
            }
            catch (Exception exception)
            {
                // 计时任务已经完成；完成回调异常不回滚计时器状态。
                Debug.LogException(exception, this);
            }
        }

        private bool CancelTimer(TimerHandle handle, bool stopCoroutine)
        {
            Coroutine coroutine = handle.Coroutine;
            if (!FinishTimer(handle, TimerState.Cancelled))
            {
                return false;
            }

            if (stopCoroutine && coroutine != null)
            {
                StopCoroutine(coroutine);
            }

            return true;
        }

        private bool FinishTimer(TimerHandle handle, TimerState terminalState)
        {
            if (!handle.IsOwnedBy(this) || !handle.TrySetTerminalState(terminalState))
            {
                return false;
            }

            activeTimers.Remove(handle.Id);
            return true;
        }

        private void CancelAllTimers(bool stopCoroutines)
        {
            if (activeTimers.Count == 0)
            {
                return;
            }

            TimerHandle[] handles = new TimerHandle[activeTimers.Count];
            activeTimers.Values.CopyTo(handles, 0);

            foreach (TimerHandle handle in handles)
            {
                CancelTimer(handle, stopCoroutines);
            }
        }

        private ulong GetNextTimerId()
        {
            ulong timerId = nextTimerId++;
            if (nextTimerId == 0)
            {
                nextTimerId = 1;
            }

            return timerId;
        }

        private static object CreateWaitInstruction(float interval, TimerTimeMode timeMode)
        {
            return timeMode == TimerTimeMode.Scaled
                ? (object)new WaitForSeconds(interval)
                : new WaitForSecondsRealtime(interval);
        }

        private static void ValidateArguments(
            float interval,
            string intervalParameterName,
            Action callback,
            TimerTimeMode timeMode,
            Object owner)
        {
            if (float.IsNaN(interval) || float.IsInfinity(interval) || interval < 0f)
            {
                throw new ArgumentOutOfRangeException(intervalParameterName, interval, "计时时间必须是大于或等于零的有限值。");
            }

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (!Enum.IsDefined(typeof(TimerTimeMode), timeMode))
            {
                throw new ArgumentOutOfRangeException(nameof(timeMode), timeMode, "未知的计时时间类型。");
            }

            if (!ReferenceEquals(owner, null) && owner == null)
            {
                throw new ArgumentException("计时器生命周期拥有者已经被销毁。", nameof(owner));
            }
        }
        #endregion
    }
}
