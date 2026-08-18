using System;
using UnityEngine;

namespace GameFramework.UI
{
    /// <summary>
    /// 所有受 <see cref="UIViewMgr"/> 管理的 UI 面板基类。
    /// 面板只声明所属层级并接收打开、关闭生命周期，不负责全局 UI 状态管理。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public abstract class BasePanel : MonoBehaviour
    {
        [SerializeField] private UILayer layer = UILayer.Normal;

        private UIViewMgr owner;
        private bool isInitialized;
        private bool isOpen;

        /// <summary>
        /// 当前面板应挂载到的 UI 层级。
        /// </summary>
        public UILayer Layer => layer;

        /// <summary>
        /// 当前面板是否已经完成一次性初始化。
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 当前面板是否已由 UIViewMgr 登记为打开状态。
        /// </summary>
        public bool IsOpen => isOpen;

        /// <summary>
        /// 请求 UIViewMgr 关闭当前面板。
        /// </summary>
        /// <returns>本次调用是否成功关闭了当前面板。</returns>
        public bool Close()
        {
            return owner != null && owner.ClosePanel(this);
        }

        protected virtual void Awake()
        {
            OnBindUI();
        }

        internal void Initialize(UIViewMgr panelOwner)
        {
            if (panelOwner == null)
            {
                throw new ArgumentNullException(nameof(panelOwner));
            }
            if (owner != null && owner != panelOwner)
            {
                throw new InvalidOperationException($"面板“{GetType().Name}”已经属于另一个 UIViewMgr。");
            }
            owner = panelOwner;
            if (isInitialized)
            {
                return;
            }

            isInitialized = true;

            try
            {
                OnInitialize();
            }
            catch
            {
                isInitialized = false;
                throw;
            }
        }

        internal void OpenInternal()
        {
            if (owner == null)
            {
                throw new InvalidOperationException($"面板“{GetType().Name}”尚未关联 UIViewMgr。");
            }
            if (isOpen)
            {
                return;
            }

            isOpen = true;

            try
            {
                OnOpen();
            }
            catch
            {
                isOpen = false;
                throw;
            }
        }

        internal void CloseInternal()
        {
            if (!isOpen)
            {
                return;
            }
            isOpen = false;
            OnClose();
        }

        /// <summary>
        /// 面板 Awake 时调用一次，由生成的 Binding partial class 重写并完成 UI 绑定。
        /// </summary>
        protected virtual void OnBindUI()
        {
        }

        /// <summary>
        /// 面板关联 UIViewMgr 后调用一次；此时 Awake 中的 UI 绑定已经完成。
        /// </summary>
        protected virtual void OnInitialize()
        {
        }

        /// <summary>
        /// 面板首次进入打开状态时调用。
        /// 同一实例保持打开时重复调用 OpenPanel 不会再次触发。
        /// </summary>
        protected virtual void OnOpen()
        {
        }

        /// <summary>
        /// 面板从 UIViewMgr 移除、销毁之前调用。
        /// </summary>
        protected virtual void OnClose()
        {
        }
    }
}
