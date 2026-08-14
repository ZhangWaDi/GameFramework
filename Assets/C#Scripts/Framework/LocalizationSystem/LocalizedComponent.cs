using System;
using System.Collections;
using GameFramework.EventSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.LocalizationSystem
{
    /// <summary>
    /// 可由本地化系统驱动的组件类型。
    /// </summary>
    public enum LocalizedTargetType
    {
        Text,
        TextMeshPro,
        Image
    }

    /// <summary>
    /// 根据当前语言自动刷新同一 GameObject 上的文本或图片组件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedComponent : MonoBehaviour
    {
        [SerializeField] private LocalizedTargetType targetType;
        [SerializeField] private string localizationKey = string.Empty;
        [SerializeField] private Text legacyText;
        [SerializeField] private TMP_Text textMeshPro;
        [SerializeField] private Image targetImage;

        private Coroutine imageLoadCoroutine;
        private int imageRequestVersion;
        private string lastWarningId;
        private bool hasStarted;

        public LocalizedTargetType TargetType => targetType;
        public string LocalizationKey => localizationKey;

        #region 组件生命周期
        private void Reset()
        {
            DetectTargetType();
            TryBindTarget();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                TryBindTarget();
            }
        }

        private void OnEnable()
        {
            TryBindTarget();
            EventCenter.Instance.AddEventListener(E_GlobalEventName.OnLanguageChanged, OnLanguageChanged);
            if (hasStarted)
            {
                RefreshLocalization();
            }
        }

        private void Start()
        {
            hasStarted = true;
            RefreshLocalization();
        }

        private void OnDisable()
        {
            if (EventCenter.TryGetInstance(out EventCenter eventCenter))
            {
                eventCenter.RemoveEventListener(E_GlobalEventName.OnLanguageChanged, OnLanguageChanged);
            }

            CancelImageLoad();
        }
        #endregion

        /// <summary>
        /// 修改当前组件使用的本地化键并立即刷新。
        /// </summary>
        public void SetKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("本地化键不能为空或仅包含空白字符。", nameof(key));
            }

            localizationKey = key;
            lastWarningId = null;

            if (isActiveAndEnabled)
            {
                RefreshLocalization();
            }
        }

        /// <summary>
        /// 使用当前语言重新刷新目标组件。
        /// </summary>
        public void RefreshLocalization()
        {
            if (!TryBindTarget())
            {
                LogWarningOnce("MissingTarget", $"对象“{name}”上不存在与 {targetType} 对应的组件，无法刷新本地化内容。");
                return;
            }

            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                LogWarningOnce("MissingKey", $"对象“{name}”上的 LocalizedComponent 尚未配置本地化键。");
                return;
            }

            if (!LocalizationMgr.TryGetInstance(out LocalizationMgr localizationMgr) || !localizationMgr.IsInitialized)
            {
                LogWarningOnce("ManagerNotInitialized", $"对象“{name}”刷新本地化内容失败：LocalizationMgr 尚未初始化。");
                return;
            }

            switch (targetType)
            {
                case LocalizedTargetType.Text:
                    RefreshLegacyText(localizationMgr);
                    break;
                case LocalizedTargetType.TextMeshPro:
                    RefreshTextMeshPro(localizationMgr);
                    break;
                case LocalizedTargetType.Image:
                    RefreshImage(localizationMgr);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "未知的本地化目标类型。");
            }
        }

        private void OnLanguageChanged()
        {
            RefreshLocalization();
        }

        private void RefreshLegacyText(LocalizationMgr localizationMgr)
        {
            CancelImageLoad();
            if (localizationMgr.TryGetLocalizedString(localizationKey, out string localizedText))
            {
                legacyText.text = localizedText;
                lastWarningId = null;
                return;
            }

            legacyText.text = GetMissingValuePlaceholder();
            LogWarningOnce($"MissingText:{localizationKey}:{localizationMgr.CurrentLanguage}", $"语言“{localizationMgr.CurrentLanguage}”中不存在文本键“{localizationKey}”。");
        }

        private void RefreshTextMeshPro(LocalizationMgr localizationMgr)
        {
            CancelImageLoad();
            if (localizationMgr.TryGetLocalizedString(localizationKey, out string localizedText))
            {
                textMeshPro.text = localizedText;
                lastWarningId = null;
                return;
            }

            textMeshPro.text = GetMissingValuePlaceholder();
            LogWarningOnce($"MissingText:{localizationKey}:{localizationMgr.CurrentLanguage}", $"语言“{localizationMgr.CurrentLanguage}”中不存在文本键“{localizationKey}”。");
        }

        private void RefreshImage(LocalizationMgr localizationMgr)
        {
            CancelImageLoad();
            if (!localizationMgr.TryGetLocalizedResourcePath(localizationKey, out string resourcePath) || string.IsNullOrWhiteSpace(resourcePath))
            {
                targetImage.sprite = null;
                LogWarningOnce($"MissingResourcePath:{localizationKey}:{localizationMgr.CurrentLanguage}", $"语言“{localizationMgr.CurrentLanguage}”中不存在有效的资源路径键“{localizationKey}”。");
                return;
            }

            int requestVersion = imageRequestVersion;
            imageLoadCoroutine = StartCoroutine(LoadLocalizedSprite(resourcePath, requestVersion));
        }

        private IEnumerator LoadLocalizedSprite(string resourcePath, int requestVersion)
        {
            ResourceRequest request = Resources.LoadAsync<Sprite>(resourcePath);
            yield return request;

            if (requestVersion != imageRequestVersion || !isActiveAndEnabled || targetImage == null)
            {
                yield break;
            }

            imageLoadCoroutine = null;
            if (request.asset is Sprite sprite)
            {
                targetImage.sprite = sprite;
                lastWarningId = null;
                yield break;
            }

            targetImage.sprite = null;
            LogWarningOnce($"MissingSprite:{resourcePath}", $"无法从 Resources 路径“{resourcePath}”加载 Sprite，本地化图片已清空。");
        }

        private bool TryBindTarget()
        {
            switch (targetType)
            {
                case LocalizedTargetType.Text:
                    if (legacyText == null || legacyText.gameObject != gameObject)
                    {
                        legacyText = GetComponent<Text>();
                    }
                    return legacyText != null;
                case LocalizedTargetType.TextMeshPro:
                    if (textMeshPro == null || textMeshPro.gameObject != gameObject)
                    {
                        textMeshPro = GetComponent<TMP_Text>();
                    }
                    return textMeshPro != null;
                case LocalizedTargetType.Image:
                    if (targetImage == null || targetImage.gameObject != gameObject)
                    {
                        targetImage = GetComponent<Image>();
                    }
                    return targetImage != null;
                default:
                    return false;
            }
        }

        private void DetectTargetType()
        {
            if (TryGetComponent(out TMP_Text detectedTextMeshPro))
            {
                targetType = LocalizedTargetType.TextMeshPro;
                textMeshPro = detectedTextMeshPro;
                return;
            }

            if (TryGetComponent(out Text detectedLegacyText))
            {
                targetType = LocalizedTargetType.Text;
                legacyText = detectedLegacyText;
                return;
            }

            if (TryGetComponent(out Image detectedImage))
            {
                targetType = LocalizedTargetType.Image;
                targetImage = detectedImage;
            }
        }

        private void CancelImageLoad()
        {
            imageRequestVersion++;
            if (imageLoadCoroutine == null)
            {
                return;
            }

            StopCoroutine(imageLoadCoroutine);
            imageLoadCoroutine = null;
        }

        private string GetMissingValuePlaceholder()
        {
            return $"[Missing:{localizationKey}]";
        }

        private void LogWarningOnce(string warningId, string message)
        {
            if (string.Equals(lastWarningId, warningId, StringComparison.Ordinal))
            {
                return;
            }

            lastWarningId = warningId;
            Logger.Instance.LogWarning(message, this);
        }
    }
}
