namespace GameFramework.EventSystem
{
    /// <summary>
    /// 全局事件名称。
    /// 仅注册需要跨模块通信的事件，模块内部事件应由模块自行定义。
    /// </summary>
    public enum E_GlobalEventName
    {
        /// <summary>
        /// 当前使用的语言发生变化。
        /// </summary>
        OnLanguageChanged
    }
}
