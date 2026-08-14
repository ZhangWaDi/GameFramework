using System;
using GameFramework.LocalizationSystem.Generated;
using UnityEngine;

namespace GameFramework.LocalizationSystem.Provider
{
    /// <summary>
    /// 使用 Unity Resources 系统加载本地化目录和语言包。
    /// </summary>
    public sealed class ResourcesLocalizationPackageProvider : ILocalizationPackageProvider
    {
        public const string DefaultCatalogPath = "LocalizationDataSOAssets/LocalizationCatalog";
        private readonly string catalogPath;

        /// <summary>
        /// 使用默认 Resources 路径创建 Provider。
        /// </summary>
        public ResourcesLocalizationPackageProvider() : this(DefaultCatalogPath)
        {
        }

        /// <summary>
        /// 使用指定 Resources 相对路径创建 Provider。
        /// </summary>
        /// <param name="catalogPath">不包含扩展名的语言目录 Resources 相对路径。</param>
        public ResourcesLocalizationPackageProvider(string catalogPath)
        {
            if (string.IsNullOrWhiteSpace(catalogPath))
            {
                throw new ArgumentException("语言目录 Resources 路径不能为空或仅包含空白字符。", nameof(catalogPath));
            }

            this.catalogPath = catalogPath;
        }

        public LocalizationCatalogSO LoadCatalog()
        {
            return Resources.Load<LocalizationCatalogSO>(catalogPath);
        }

        public LocalizationDataSO LoadPackage(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("语言包 Resources 路径不能为空或仅包含空白字符。", nameof(packagePath));
            }

            return Resources.Load<LocalizationDataSO>(packagePath);
        }

        public void ReleasePackage(LocalizationDataSO package)
        {
            if (package == null)
            {
                return;
            }

            Resources.UnloadAsset(package);
        }
    }
}
