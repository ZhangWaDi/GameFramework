using GameFramework.LocalizationSystem.Generated;

namespace GameFramework.LocalizationSystem.Provider
{
    /// <summary>
    /// 定义本地化语言目录和语言包的加载、释放契约。
    /// </summary>
    /// <remarks>
    /// 由 Provider 加载的语言包应交还同一个 Provider 释放。
    /// </remarks>
    public interface ILocalizationPackageProvider
    {
        /// <summary>
        /// 加载记录可用语言及其语言包路径的目录。
        /// </summary>
        /// <returns>加载成功时返回语言目录，否则返回 <see langword="null"/>。</returns>
        LocalizationCatalogSO LoadCatalog();

        /// <summary>
        /// 根据 Provider 可识别的路径加载指定语言包。
        /// </summary>
        /// <param name="packagePath">语言包路径。</param>
        /// <returns>加载成功时返回语言包，否则返回 <see langword="null"/>。</returns>
        LocalizationDataSO LoadPackage(string packagePath);

        /// <summary>
        /// 释放由当前 Provider 加载且不再使用的语言包。
        /// </summary>
        void ReleasePackage(LocalizationDataSO package);
    }
}
