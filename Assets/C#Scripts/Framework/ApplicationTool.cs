using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// Unity 应用运行环境、平台、网络可达性和产品信息的统一访问入口。
    /// </summary>
    public static class ApplicationTool
    {
        #region 运行环境

        /// <summary>
        /// 当前是否运行在任意构建后的 Player 中，
        /// 或运行在 Unity Editor 的播放模式中。
        /// </summary>
        public static bool IsPlaying => Application.isPlaying;

        /// <summary>
        /// 当前游戏是否运行在 Unity Editor 内。
        /// </summary>
        public static bool IsEditor => Application.isEditor;

        /// <summary>
        /// 当前是否运行在 Unity Editor 之外的构建 Player 中。
        /// </summary>
        public static bool IsBuildRuntime => !IsEditor && IsPlaying;

        /// <summary>
        /// Unity 当前运行的平台。
        /// </summary>
        public static RuntimePlatform CurrentPlatform => Application.platform;

        /// <summary>
        /// Unity 报告的设备当前系统语言。
        /// </summary>
        public static SystemLanguage CurrentSystemLanguage => Application.systemLanguage;
        #endregion

        #region 网络状态

        /// <summary>
        /// Unity 报告的设备当前网络可达类型。
        /// 该值不执行实际联网探测，不能保证目标互联网服务可访问。
        /// </summary>
        public static NetworkReachability InternetReachability =>
            Application.internetReachability;

        /// <summary>
        /// Unity 报告的网络可达类型是否不为 <see cref="NetworkReachability.NotReachable"/>。
        /// 该属性不代表已成功连接互联网或指定服务器。
        /// </summary>
        public static bool IsNetworkReachable =>
            InternetReachability != NetworkReachability.NotReachable;

        /// <summary>
        /// Unity 报告当前网络是否可通过 Wi-Fi 或网线到达。
        /// 该属性无法区分 Wi-Fi 与有线网络，也不代表目标互联网服务可访问。
        /// </summary>
        public static bool IsLocalAreaNetworkReachable =>
            InternetReachability == NetworkReachability.ReachableViaLocalAreaNetwork;

        /// <summary>
        /// 返回 Unity 报告的网络可达类型是否不为
        /// <see cref="NetworkReachability.NotReachable"/>。
        /// 该方法不执行实际联网探测。
        /// </summary>
        public static bool IsNetworkAvailable()
        {
            return IsNetworkReachable;
        }

        #endregion

        #region 产品信息

        /// <summary>
        /// 当前应用的产品名称。
        /// </summary>
        public static string ProductName => Application.productName;

        /// <summary>
        /// 当前应用的版本号。
        /// </summary>
        public static string GameVersion => Application.version;

        /// <summary>
        /// 当前应用的公司名称。
        /// </summary>
        public static string CompanyName => Application.companyName;

        #endregion
    }
}
