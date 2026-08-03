using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 提供当前应用 PlayerPrefs 偏好数据的统一读写入口。
    /// </summary>
    /// <remarks>
    /// Unity PlayerPrefs 原生支持 <see cref="int"/>、<see cref="float"/> 和 <see cref="string"/>；
    /// 布尔值由本类使用整数 0 和 1 表示。
    /// 本类不提供缓存、加密、数据版本管理或复杂存档能力。
    /// </remarks>
    public static class PlayerPrefsSetting
    {
        private const int FalseValue = 0;
        private const int TrueValue = 1;

        #region 存储管理

        /// <summary>
        /// 将所有已修改的 PlayerPrefs 偏好数据保存到持久化存储。
        /// </summary>
        public static void Save()
        {
            PlayerPrefs.Save();
            Logger.Instance.LogInfo("PlayerPrefs 偏好数据已保存。");
        }

        /// <summary>
        /// 检查指定键是否存在于当前应用的 PlayerPrefs 中。
        /// </summary>
        /// <param name="keyName">要检查的键。</param>
        /// <returns>存在指定键时返回 <see langword="true"/>。</returns>
        public static bool HasSetting(string keyName)
        {
            ValidateKey(keyName);
            return PlayerPrefs.HasKey(keyName);
        }

        /// <summary>
        /// 删除指定键及其对应值。
        /// </summary>
        /// <param name="keyName">要删除的键。</param>
        /// <returns>
        /// 成功找到并删除指定键时返回 <see langword="true"/>；
        /// 键不存在时返回 <see langword="false"/>。
        /// </returns>
        public static bool RemoveSetting(string keyName)
        {
            ValidateKey(keyName);
            if (!PlayerPrefs.HasKey(keyName))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(keyName);
            Logger.Instance.LogInfo($"已删除 PlayerPrefs 键：{keyName}。");
            return true;
        }

        /// <summary>
        /// 删除当前应用 PlayerPrefs 中的全部键和值。
        /// </summary>
        /// <remarks>
        /// 此操作没有按键恢复能力，调用方应自行确认删除范围。
        /// </remarks>
        public static void RemoveAllSettings()
        {
            PlayerPrefs.DeleteAll();
            Logger.Instance.LogWarning("已删除当前应用的全部 PlayerPrefs 偏好数据。");
        }

        #endregion

        #region 布尔值

        /// <summary>
        /// 读取使用整数存储的布尔值。
        /// </summary>
        /// <param name="keyName">要读取的键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        public static bool GetBool(string keyName, bool defaultValue = false)
        {
            ValidateKey(keyName);
            int defaultIntValue = defaultValue ? TrueValue : FalseValue;
            return PlayerPrefs.GetInt(keyName, defaultIntValue) != FalseValue;
        }

        /// <summary>
        /// 使用整数 0 或 1 存储布尔值。
        /// </summary>
        /// <param name="keyName">要写入的键。</param>
        /// <param name="value">要写入的布尔值。</param>
        public static void SetBool(string keyName, bool value)
        {
            ValidateKey(keyName);
            PlayerPrefs.SetInt(keyName, value ? TrueValue : FalseValue);
        }

        #endregion

        #region 整数

        /// <summary>
        /// 读取整数值。
        /// </summary>
        /// <param name="keyName">要读取的键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        public static int GetInt(string keyName, int defaultValue = 0)
        {
            ValidateKey(keyName);
            return PlayerPrefs.GetInt(keyName, defaultValue);
        }

        /// <summary>
        /// 写入整数值。
        /// </summary>
        /// <param name="keyName">要写入的键。</param>
        /// <param name="value">要写入的整数值。</param>
        public static void SetInt(string keyName, int value)
        {
            ValidateKey(keyName);
            PlayerPrefs.SetInt(keyName, value);
        }

        #endregion

        #region 浮点数

        /// <summary>
        /// 读取单精度浮点数值。
        /// </summary>
        /// <param name="keyName">要读取的键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        public static float GetFloat(string keyName, float defaultValue = 0f)
        {
            ValidateKey(keyName);
            return PlayerPrefs.GetFloat(keyName, defaultValue);
        }

        /// <summary>
        /// 写入单精度浮点数值。
        /// </summary>
        /// <param name="keyName">要写入的键。</param>
        /// <param name="value">要写入的单精度浮点数值。</param>
        public static void SetFloat(string keyName, float value)
        {
            ValidateKey(keyName);
            PlayerPrefs.SetFloat(keyName, value);
        }

        #endregion

        #region 字符串

        /// <summary>
        /// 读取字符串值。
        /// </summary>
        /// <param name="keyName">要读取的键。</param>
        /// <param name="defaultValue">键不存在时返回的默认值。</param>
        public static string GetString(string keyName, string defaultValue = "")
        {
            ValidateKey(keyName);
            return PlayerPrefs.GetString(keyName, defaultValue);
        }

        /// <summary>
        /// 写入字符串值。
        /// </summary>
        /// <param name="keyName">要写入的键。</param>
        /// <param name="value">要写入的非空字符串。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> 为 <see langword="null"/>。
        /// </exception>
        public static void SetString(string keyName, string value)
        {
            ValidateKey(keyName);
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            PlayerPrefs.SetString(keyName, value);
        }

        #endregion

        #region 参数检查

        /// <summary>
        /// 确保 PlayerPrefs 键不是空值、空字符串或仅包含空白字符。
        /// </summary>
        private static void ValidateKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                throw new ArgumentException(
                    "PlayerPrefs 键不能为空或仅包含空白字符。",
                    nameof(keyName));
            }
        }

        #endregion
    }
}
