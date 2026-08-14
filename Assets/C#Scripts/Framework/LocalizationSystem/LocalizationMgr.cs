using System;
using System.Collections.Generic;
using GameFramework.EventSystem;
using GameFramework.LocalizationSystem.Generated;
using GameFramework.LocalizationSystem.Provider;
using UnityEngine;

namespace GameFramework.LocalizationSystem
{
    /// <summary>
    /// 提供当前语言、语言切换及本地化数据查询的运行时访问入口。
    /// </summary>
    public sealed class LocalizationMgr : Singleton<LocalizationMgr>
    {
        public const string LanguageSettingKey = "GameFramework.Localization.Language";
        public const string TextSectionId = "LocalizationLanguage";
        public const string ResourcePathSectionId = "LocalizationResPath";

        private LocalizationLanguage currentLanguage;
        private bool isInitialized;
        private ILocalizationPackageProvider packageProvider;
        private LocalizationCatalogSO catalog;
        private LocalizationDataSO currentLanguagePackage;
        private Dictionary<LocalizationLanguage, LocalizationPackageReference> availablePackages = new();
        private Dictionary<string, string> currentTextLookup = new(StringComparer.Ordinal);
        private Dictionary<string, string> currentResourcePathLookup = new(StringComparer.Ordinal);

        /// <summary>
        /// 当前是否已完成语言解析、语言包加载和查询缓存构建。
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 获取当前语言类型。
        /// </summary>
        public LocalizationLanguage CurrentLanguage
        {
            get
            {
                EnsureInitialized();
                return currentLanguage;
            }
        }

        /// <summary>
        /// 使用默认 Resources Provider 初始化本地化管理器。
        /// </summary>
        public override void OnInit()
        {
            if (isInitialized)
            {
                return;
            }

            Initialize(new ResourcesLocalizationPackageProvider());
        }

        protected override void OnRelease()
        {
            ReleaseLoadedPackage();
        }

        #region 外部调用

        /// <summary>
        /// 切换当前语言，切换成功后保存 PlayerPrefs 并广播无参 OnLanguageChanged 事件。
        /// </summary>
        /// <remarks>
        /// 目标语言与当前语言相同时仍会保存用户偏好，但不会重新加载语言包或广播事件。
        /// </remarks>
        public void ChangeLanguage(LocalizationLanguage language)
        {
            EnsureInitialized();

            if (!availablePackages.TryGetValue(language, out LocalizationPackageReference packageReference))
            {
                string message = $"切换语言失败：Catalog 中不存在语言“{language}”对应的有效语言包。";
                Logger.Instance.LogError(message, catalog);
                throw new KeyNotFoundException(message);
            }

            if (currentLanguage == language)
            {
                SaveLanguagePreference(language);
                return;
            }

            LocalizationDataSO loadedPackage = LoadAndValidatePackage(packageProvider, language, packageReference);
            Dictionary<string, string> loadedTextLookup;
            Dictionary<string, string> loadedResourcePathLookup;

            try
            {
                BuildLookupCaches(loadedPackage, out loadedTextLookup, out loadedResourcePathLookup);
                SaveLanguagePreference(language);
            }
            catch
            {
                packageProvider.ReleasePackage(loadedPackage);
                throw;
            }

            LocalizationDataSO previousPackage = currentLanguagePackage;
            currentLanguagePackage = loadedPackage;
            currentTextLookup = loadedTextLookup;
            currentResourcePathLookup = loadedResourcePathLookup;
            currentLanguage = language;

            try
            {
                packageProvider.ReleasePackage(previousPackage);
            }
            catch (Exception exception)
            {
                Logger.Instance.LogWarning($"释放旧语言包“{previousPackage.name}”失败：{exception.Message}", previousPackage);
            }

            Logger.Instance.LogInfo($"当前语言已切换为“{language.ToLanguageId()}”。", loadedPackage);
            EventCenter.Instance.EventTrigger(E_GlobalEventName.OnLanguageChanged);
        }

        /// <summary>
        /// 尝试获取当前语言中的本地化文本。
        /// </summary>
        public bool TryGetLocalizedString(string key, out string value)
        {
            return TryGetLocalizedValue(currentTextLookup, key, out value);
        }

        /// <summary>
        /// 获取当前语言中的本地化文本；键不存在时抛出 KeyNotFoundException。
        /// </summary>
        public string GetLocalizedString(string key)
        {
            return GetLocalizedValue(currentTextLookup, key, "本地化文本");
        }

        /// <summary>
        /// 尝试获取当前语言中的本地化资源路径。
        /// </summary>
        public bool TryGetLocalizedResourcePath(string key, out string path)
        {
            return TryGetLocalizedValue(currentResourcePathLookup, key, out path);
        }

        /// <summary>
        /// 获取当前语言中的本地化资源路径；键不存在时抛出 KeyNotFoundException。
        /// </summary>
        public string GetLocalizedResourcePath(string key)
        {
            return GetLocalizedValue(currentResourcePathLookup, key, "本地化资源路径");
        }

        #endregion

        #region 内部实现

        /// <summary>
        /// 使用指定 Provider 初始化本地化管理器。
        /// </summary>
        private void Initialize(ILocalizationPackageProvider targetProvider)
        {
            if (targetProvider == null)
            {
                throw new ArgumentNullException(nameof(targetProvider));
            }

            if (isInitialized)
            {
                if (ReferenceEquals(packageProvider, targetProvider))
                {
                    return;
                }

                ReleaseLoadedPackage();
            }

            LocalizationCatalogSO loadedCatalog = targetProvider.LoadCatalog();
            if (loadedCatalog == null)
            {
                const string message = "加载本地化 Catalog 失败，请确认 Catalog 已生成且 Provider 配置正确。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }

            Dictionary<LocalizationLanguage, LocalizationPackageReference> loadedAvailablePackages = BuildAvailablePackages(loadedCatalog);
            LocalizationLanguage selectedLanguage = ResolveCurrentLanguage(loadedCatalog, loadedAvailablePackages);
            LocalizationDataSO loadedPackage = LoadAndValidatePackage(targetProvider, selectedLanguage, loadedAvailablePackages[selectedLanguage]);
            Dictionary<string, string> loadedTextLookup;
            Dictionary<string, string> loadedResourcePathLookup;

            try
            {
                BuildLookupCaches(loadedPackage, out loadedTextLookup, out loadedResourcePathLookup);
            }
            catch
            {
                targetProvider.ReleasePackage(loadedPackage);
                throw;
            }

            packageProvider = targetProvider;
            catalog = loadedCatalog;
            currentLanguagePackage = loadedPackage;
            currentLanguage = selectedLanguage;
            availablePackages = loadedAvailablePackages;
            currentTextLookup = loadedTextLookup;
            currentResourcePathLookup = loadedResourcePathLookup;
            isInitialized = true;
            Logger.Instance.LogInfo($"本地化管理器初始化完成，当前语言：{selectedLanguage.ToLanguageId()}。", loadedPackage);
        }

        /// <summary>
        /// 加载并验证指定语言的本地化语言包。
        /// </summary>
        private static LocalizationDataSO LoadAndValidatePackage(ILocalizationPackageProvider targetProvider, LocalizationLanguage language, LocalizationPackageReference packageReference)
        {
            LocalizationDataSO loadedPackage = targetProvider.LoadPackage(packageReference.ResourcesPath);
            string languageId = language.ToLanguageId();

            if (loadedPackage == null)
            {
                string message = $"加载语言“{languageId}”的本地化语言包失败，Provider 路径为“{packageReference.ResourcesPath}”。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }

            if (!string.Equals(loadedPackage.Language, languageId, StringComparison.Ordinal))
            {
                string packageName = loadedPackage.name;
                string packageLanguage = loadedPackage.Language;
                targetProvider.ReleasePackage(loadedPackage);
                string message = $"语言包“{packageName}”声明的语言为“{packageLanguage}”，与 Catalog 中的“{languageId}”不一致。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }

            return loadedPackage;
        }

        /// <summary>
        /// 从加载的 Catalog 中构建可用的语言包映射。
        /// </summary>
        private static Dictionary<LocalizationLanguage, LocalizationPackageReference> BuildAvailablePackages(LocalizationCatalogSO loadedCatalog)
        {
            Dictionary<LocalizationLanguage, LocalizationPackageReference> loadedAvailablePackages = new();

            foreach (LocalizationPackageReference packageReference in loadedCatalog.Packages)
            {
                if (packageReference == null || string.IsNullOrWhiteSpace(packageReference.ResourcesPath))
                {
                    Logger.Instance.LogWarning($"本地化 Catalog“{loadedCatalog.name}”中存在空语言包配置，已忽略。", loadedCatalog);
                    continue;
                }

                if (!LocalizationLanguageUtility.TryParseLanguage(packageReference.Language, out LocalizationLanguage language))
                {
                    Logger.Instance.LogWarning($"本地化 Catalog“{loadedCatalog.name}”中的语言“{packageReference.Language}”不在生成的语言枚举中，已忽略。", loadedCatalog);
                    continue;
                }

                if (!loadedAvailablePackages.TryAdd(language, packageReference))
                {
                    Logger.Instance.LogWarning($"本地化 Catalog“{loadedCatalog.name}”中重复配置了语言“{packageReference.Language}”，已忽略后续配置。", loadedCatalog);
                }
            }

            if (loadedAvailablePackages.Count == 0)
            {
                string message = $"本地化 Catalog“{loadedCatalog.name}”中不存在可用的语言包配置。";
                Logger.Instance.LogError(message, loadedCatalog);
                throw new InvalidOperationException(message);
            }

            return loadedAvailablePackages;
        }

        /// <summary>
        /// 从加载的语言包中构建文本和资源路径查找表。
        /// </summary>
        private static void BuildLookupCaches(LocalizationDataSO languagePackage, out Dictionary<string, string> textLookup, out Dictionary<string, string> resourcePathLookup)
        {
            textLookup = new(StringComparer.Ordinal);
            resourcePathLookup = new(StringComparer.Ordinal);
            HashSet<string> sectionIds = new(StringComparer.Ordinal);

            foreach (LocalizationTableSection section in languagePackage.Sections)
            {
                if (section == null || string.IsNullOrWhiteSpace(section.TableId))
                {
                    throw new InvalidOperationException($"语言包“{languagePackage.name}”中存在无效的本地化分区。");
                }

                if (!sectionIds.Add(section.TableId))
                {
                    throw new InvalidOperationException($"语言包“{languagePackage.name}”中重复配置了分区“{section.TableId}”。");
                }

                Dictionary<string, string> entryLookup = BuildEntryLookup(languagePackage, section);
                switch (section.TableId)
                {
                    case TextSectionId:
                        textLookup = entryLookup;
                        break;
                    case ResourcePathSectionId:
                        resourcePathLookup = entryLookup;
                        break;
                }
            }
        }

        /// <summary>
        /// 构建指定分区的键值查找表。
        /// </summary>
        private static Dictionary<string, string> BuildEntryLookup(LocalizationDataSO languagePackage, LocalizationTableSection section)
        {
            Dictionary<string, string> entryLookup = new(StringComparer.Ordinal);

            foreach (LocalizationDataEntry entry in section.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new InvalidOperationException($"语言包“{languagePackage.name}”的分区“{section.TableId}”中存在无效键。");
                }

                if (!entryLookup.TryAdd(entry.Key, entry.Value ?? string.Empty))
                {
                    throw new InvalidOperationException($"语言包“{languagePackage.name}”的分区“{section.TableId}”中重复配置了键“{entry.Key}”。");
                }
            }

            return entryLookup;
        }

        /// <summary>
        /// 解析当前语言。
        /// </summary>
        private static LocalizationLanguage ResolveCurrentLanguage(LocalizationCatalogSO loadedCatalog, IReadOnlyDictionary<LocalizationLanguage, LocalizationPackageReference> loadedAvailablePackages)
        {
            if (TryGetPlayerPrefsLanguage(loadedAvailablePackages, out LocalizationLanguage language))
            {
                return language;
            }

            string systemLanguageId = Application.systemLanguage.ToString();
            if (TryGetAvailableLanguage(systemLanguageId, loadedAvailablePackages, out language))
            {
                return language;
            }

            Logger.Instance.LogWarning($"当前系统语言“{systemLanguageId}”没有对应的本地化语言包，将使用 Catalog 默认语言。", loadedCatalog);

            if (TryGetAvailableLanguage(loadedCatalog.DefaultLanguage, loadedAvailablePackages, out language))
            {
                return language;
            }

            foreach (LocalizationLanguage availableLanguage in loadedAvailablePackages.Keys)
            {
                Logger.Instance.LogWarning($"Catalog 默认语言“{loadedCatalog.DefaultLanguage}”不可用，将使用首个有效语言“{availableLanguage.ToLanguageId()}”。", loadedCatalog);
                return availableLanguage;
            }

            throw new InvalidOperationException("本地化 Catalog 中不存在可用的语言包配置。");
        }

        /// <summary>
        /// 尝试从 PlayerPrefs 中获取当前语言。
        /// </summary>
        private static bool TryGetPlayerPrefsLanguage(IReadOnlyDictionary<LocalizationLanguage, LocalizationPackageReference> loadedAvailablePackages, out LocalizationLanguage language)
        {
            language = default;
            if (!PlayerPrefsSetting.HasSetting(LanguageSettingKey))
            {
                return false;
            }

            string savedLanguageId = PlayerPrefsSetting.GetString(LanguageSettingKey);
            if (TryGetAvailableLanguage(savedLanguageId, loadedAvailablePackages, out language))
            {
                return true;
            }

            Logger.Instance.LogWarning($"PlayerPrefs 中保存的语言“{savedLanguageId}”无效或没有对应语言包，将尝试使用系统语言。");
            return false;
        }

        /// <summary>
        /// 尝试获取可用的语言包。
        /// </summary>
        private static bool TryGetAvailableLanguage(string languageId, IReadOnlyDictionary<LocalizationLanguage, LocalizationPackageReference> loadedAvailablePackages, out LocalizationLanguage language)
        {
            return LocalizationLanguageUtility.TryParseLanguage(languageId, out language) && loadedAvailablePackages.ContainsKey(language);
        }

        /// <summary>
        /// 尝试获取本地化值。
        /// </summary>
        private bool TryGetLocalizedValue(IReadOnlyDictionary<string, string> lookup, string key, out string value)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("本地化键不能为空或仅包含空白字符。", nameof(key));
            }

            return lookup.TryGetValue(key, out value);
        }

        /// <summary>
        /// 获取本地化值。
        /// </summary>
        private string GetLocalizedValue(IReadOnlyDictionary<string, string> lookup, string key, string valueTypeName)
        {
            if (TryGetLocalizedValue(lookup, key, out string value))
            {
                return value;
            }

            string message = $"语言“{currentLanguage.ToLanguageId()}”中不存在键为“{key}”的{valueTypeName}。";
            Logger.Instance.LogError(message, currentLanguagePackage);
            throw new KeyNotFoundException(message);
        }

        /// <summary>
        /// 保存当前偏好设置的语言。
        /// </summary>
        private static void SaveLanguagePreference(LocalizationLanguage language)
        {
            PlayerPrefsSetting.SetString(LanguageSettingKey, language.ToLanguageId());
            PlayerPrefsSetting.Save();
        }

        /// <summary>
        /// 释放当前加载的语言包。
        /// </summary>
        private void ReleaseLoadedPackage()
        {
            if (packageProvider != null && currentLanguagePackage != null)
            {
                packageProvider.ReleasePackage(currentLanguagePackage);
            }

            packageProvider = null;
            catalog = null;
            currentLanguagePackage = null;
            currentLanguage = default;
            availablePackages.Clear();
            currentTextLookup.Clear();
            currentResourcePathLookup.Clear();
            isInitialized = false;
        }

        /// <summary>
        /// 确保本地化管理器已初始化。
        /// </summary>
        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                const string message = "LocalizationMgr 尚未初始化，请在项目启动流程中调用 LocalizationMgr.Instance.OnInit()。";
                Logger.Instance.LogError(message);
                throw new InvalidOperationException(message);
            }
        }

        #endregion
    }
}
