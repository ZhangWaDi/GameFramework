using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace GameFramework
{
    /// <summary>
    /// 提供统一格式的轻量日志输出。
    /// 日志调用仅在 Unity Editor、Development Build，
    /// 或定义了 ENABLE_DEBUG_LOG 时保留。
    /// </summary>
    /// <remarks>
    /// 调用来源由编译器提供的源文件名和成员名组成，
    /// 不会在运行时创建或遍历调用堆栈。
    /// </remarks>
    public sealed class Logger : Singleton<Logger>
    {
        private const string EnableDebugLogSymbol = "ENABLE_DEBUG_LOG";

        #region 日志开关

        /// <summary>
        /// 是否允许输出已被编译保留的日志。
        /// 将其设置为 <see langword="false"/> 不影响条件编译结果。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        #endregion

        #region 日志输出

        /// <summary>
        /// 输出普通信息日志。
        /// </summary>
        /// <param name="message">日志内容。</param>
        /// <param name="context">
        /// 可选的 Unity 对象上下文，用于在 Console 中关联对象。
        /// </param>
        /// <param name="callerFilePath">由编译器填充的调用方源文件路径。</param>
        /// <param name="callerMemberName">由编译器填充的调用方成员名称。</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional(EnableDebugLogSymbol)]
        public void LogInfo(
            string message,
            Object context = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            if (!IsEnabled)
            {
                return;
            }

            Debug.Log(FormatMessage(message, callerFilePath, callerMemberName), context);
        }

        /// <summary>
        /// 输出警告日志。
        /// </summary>
        /// <param name="message">日志内容。</param>
        /// <param name="context">
        /// 可选的 Unity 对象上下文，用于在 Console 中关联对象。
        /// </param>
        /// <param name="callerFilePath">由编译器填充的调用方源文件路径。</param>
        /// <param name="callerMemberName">由编译器填充的调用方成员名称。</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional(EnableDebugLogSymbol)]
        public void LogWarning(
            string message,
            Object context = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            if (!IsEnabled)
            {
                return;
            }

            Debug.LogWarning(
                FormatMessage(message, callerFilePath, callerMemberName),
                context);
        }

        /// <summary>
        /// 输出错误日志。
        /// </summary>
        /// <param name="message">日志内容。</param>
        /// <param name="context">
        /// 可选的 Unity 对象上下文，用于在 Console 中关联对象。
        /// </param>
        /// <param name="callerFilePath">由编译器填充的调用方源文件路径。</param>
        /// <param name="callerMemberName">由编译器填充的调用方成员名称。</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional(EnableDebugLogSymbol)]
        public void LogError(
            string message,
            Object context = null,
            [CallerFilePath] string callerFilePath = "",
            [CallerMemberName] string callerMemberName = "")
        {
            if (!IsEnabled)
            {
                return;
            }

            Debug.LogError(
                FormatMessage(message, callerFilePath, callerMemberName),
                context);
        }
        #endregion

        #region 内部方法

        private static string FormatMessage(
            string message,
            string callerFilePath,
            string callerMemberName)
        {
            string sourceName = string.IsNullOrEmpty(callerFilePath)
                ? "UnknownSource"
                : Path.GetFileNameWithoutExtension(callerFilePath);
            string memberName = string.IsNullOrEmpty(callerMemberName)
                ? "UnknownMember"
                : callerMemberName;

            return $"[{sourceName}.{memberName}] {message}";
        }

        #endregion
    }
}
