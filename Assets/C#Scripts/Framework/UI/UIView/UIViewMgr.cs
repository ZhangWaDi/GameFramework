using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UIEventSystemComponent = UnityEngine.EventSystems.EventSystem;

namespace GameFramework.UI
{
    /// <summary>
    /// 构建全局 UI 运行环境，并统一管理面板的加载、分层、打开与关闭。
    /// </summary>
    public sealed class UIViewMgr : SingletonMono<UIViewMgr>
    {
        private readonly Dictionary<UILayer, RectTransform> layerRoots = new();
        private readonly Dictionary<Type, BasePanel> openedPanels = new();

        private Canvas uiCanvas;
        private UIEventSystemComponent uiEventSystem;
        private bool isInitialized;

        /// <summary>
        /// 当前是否已经完成 Canvas、UI 层级和 EventSystem 初始化。
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 框架创建并常驻的根 Canvas。
        /// </summary>
        public Canvas UICanvas
        {
            get
            {
                EnsureInitialized();
                return uiCanvas;
            }
        }

        /// <summary>
        /// 当前项目使用的唯一 UI EventSystem。
        /// </summary>
        public UIEventSystemComponent UIEventSystem
        {
            get
            {
                EnsureInitialized();
                return uiEventSystem;
            }
        }

        /// <summary>
        /// 当前已打开的面板数量。
        /// </summary>
        public int OpenPanelCount => openedPanels.Count;

        /// <summary>
        /// 幂等构建全局 UI 运行环境。
        /// </summary>
        public override void OnInit()
        {
            if (isInitialized) return;

            InitializeCanvas();
            InitializeLayers();
            InitializeEventSystem();
            isInitialized = true;
        }

        protected override void OnDestroy()
        {
            openedPanels.Clear();
            layerRoots.Clear();
            uiCanvas = null;
            uiEventSystem = null;
            isInitialized = false;
            base.OnDestroy();
        }

        #region 面板管理

        /// <summary>
        /// 打开指定类型的面板。同类型面板已打开时仅将其移动到所属层级顶部。
        /// </summary>
        public T OpenPanel<T>() where T : BasePanel
        {
            EnsureInitialized();

            Type panelType = typeof(T);
            if (openedPanels.TryGetValue(panelType, out BasePanel openedPanel))
            {
                if (openedPanel != null)
                {
                    openedPanel.transform.SetAsLastSibling();
                    return (T)openedPanel;
                }

                openedPanels.Remove(panelType);
            }

            string resourcePath = $"{UIDefines.PanelResourceRoot}/{panelType.Name}";
            GameObject panelPrefab = Resources.Load<GameObject>(resourcePath);
            if (panelPrefab == null) throw new InvalidOperationException($"打开 UI 面板失败：Resources 中不存在预制体“{resourcePath}”。");

            T prefabPanel = panelPrefab.GetComponent<T>();
            if (prefabPanel == null) throw new InvalidOperationException($"打开 UI 面板失败：预制体“{resourcePath}”的根节点未挂载组件“{panelType.FullName}”。");
            if (!layerRoots.TryGetValue(prefabPanel.Layer, out RectTransform layerRoot)) throw new InvalidOperationException($"打开 UI 面板失败：未创建层级“{prefabPanel.Layer}”。");

            GameObject panelObject = Instantiate(panelPrefab, layerRoot, false);
            panelObject.name = panelType.Name;
            SetUILayer(panelObject);
            if (!panelObject.activeSelf)
            {
                panelObject.SetActive(true);
            }

            T panel = panelObject.GetComponent<T>();
            RectTransform panelRectTransform = panelObject.GetComponent<RectTransform>();
            StretchToParent(panelRectTransform);

            try
            {
                panel.Initialize(this);
                openedPanels.Add(panelType, panel);
                panel.OpenInternal();
                return panel;
            }
            catch
            {
                openedPanels.Remove(panelType);
                Destroy(panelObject);
                throw;
            }
        }

        /// <summary>
        /// 关闭指定类型的已打开面板。
        /// </summary>
        public bool ClosePanel<T>() where T : BasePanel
        {
            if (!TryGetPanel(out T panel)) return false;
            return ClosePanel(panel);
        }

        /// <summary>
        /// 判断指定类型的面板是否处于打开状态。
        /// </summary>
        public bool IsPanelOpen<T>() where T : BasePanel
        {
            return TryGetPanel(out T _);
        }

        /// <summary>
        /// 尝试获取指定类型的已打开面板。
        /// </summary>
        public bool TryGetPanel<T>(out T panel) where T : BasePanel
        {
            EnsureInitialized();

            Type panelType = typeof(T);
            if (openedPanels.TryGetValue(panelType, out BasePanel openedPanel) && openedPanel != null)
            {
                panel = (T)openedPanel;
                return true;
            }

            openedPanels.Remove(panelType);
            panel = null;
            return false;
        }

        /// <summary>
        /// 将指定类型的已打开面板移动到所属层级顶部。
        /// </summary>
        public bool BringToFront<T>() where T : BasePanel
        {
            if (!TryGetPanel(out T panel)) return false;
            panel.transform.SetAsLastSibling();
            return true;
        }

        /// <summary>
        /// 关闭并销毁全部已打开面板。
        /// </summary>
        public void CloseAllPanels()
        {
            EnsureInitialized();

            List<BasePanel> panels = new(openedPanels.Values);
            List<Exception> exceptions = new();
            foreach (BasePanel panel in panels)
            {
                try
                {
                    ClosePanel(panel);
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }

            openedPanels.Clear();
            if (exceptions.Count > 0) throw new AggregateException("关闭全部 UI 面板时有一个或多个面板抛出异常。", exceptions);
        }

        internal bool ClosePanel(BasePanel panel)
        {
            EnsureInitialized();
            if (panel == null) return false;

            Type panelType = panel.GetType();
            if (!openedPanels.TryGetValue(panelType, out BasePanel openedPanel) || openedPanel != panel) return false;

            openedPanels.Remove(panelType);

            try
            {
                panel.CloseInternal();
            }
            finally
            {
                if (panel != null) Destroy(panel.gameObject);
            }

            return true;
        }

        #endregion

        #region UI 运行环境

        private void InitializeCanvas()
        {
            Transform existingCanvasTransform = transform.Find(UIDefines.CanvasObjectName);
            GameObject canvasObject;

            if (existingCanvasTransform != null)
            {
                canvasObject = existingCanvasTransform.gameObject;
                uiCanvas = canvasObject.GetComponent<Canvas>();
                if (uiCanvas == null) throw new InvalidOperationException($"UIViewMgr 子节点“{UIDefines.CanvasObjectName}”缺少 Canvas 组件。");
            }
            else
            {
                canvasObject = new GameObject(UIDefines.CanvasObjectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(transform, false);
                uiCanvas = canvasObject.GetComponent<Canvas>();
            }

            SetUILayer(canvasObject);
            uiCanvas.renderMode = UIDefines.CanvasRenderMode;
            uiCanvas.worldCamera = null;
            uiCanvas.pixelPerfect = UIDefines.CanvasPixelPerfect;
            uiCanvas.sortingOrder = UIDefines.CanvasSortingOrder;
            uiCanvas.targetDisplay = UIDefines.CanvasTargetDisplay;

            CanvasScaler canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            if (canvasScaler == null) canvasScaler = canvasObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = UIDefines.CanvasScaleMode;
            canvasScaler.referenceResolution = UIDefines.CanvasReferenceResolution;
            canvasScaler.screenMatchMode = UIDefines.CanvasScreenMatchMode;
            canvasScaler.matchWidthOrHeight = UIDefines.CanvasMatchWidthOrHeight;
            canvasScaler.referencePixelsPerUnit = UIDefines.CanvasReferencePixelsPerUnit;

            GraphicRaycaster graphicRaycaster = canvasObject.GetComponent<GraphicRaycaster>();
            if (graphicRaycaster == null) graphicRaycaster = canvasObject.AddComponent<GraphicRaycaster>();
            graphicRaycaster.ignoreReversedGraphics = UIDefines.GraphicRaycasterIgnoreReversedGraphics;
        }

        private void InitializeLayers()
        {
            layerRoots.Clear();
            RectTransform canvasRectTransform = uiCanvas.transform as RectTransform;
            if (canvasRectTransform == null) throw new InvalidOperationException("UICanvas 必须使用 RectTransform。");

            foreach (UILayer layer in UIDefines.LayerOrder)
            {
                string layerObjectName = UIDefines.GetLayerObjectName(layer);
                Transform existingLayerTransform = canvasRectTransform.Find(layerObjectName);
                RectTransform layerRoot;

                if (existingLayerTransform != null)
                {
                    layerRoot = existingLayerTransform as RectTransform;
                    if (layerRoot == null) throw new InvalidOperationException($"UI 层级节点“{layerObjectName}”必须使用 RectTransform。");
                }
                else
                {
                    GameObject layerObject = new(layerObjectName, typeof(RectTransform));
                    SetUILayer(layerObject);
                    layerRoot = layerObject.GetComponent<RectTransform>();
                    layerRoot.SetParent(canvasRectTransform, false);
                }

                StretchToParent(layerRoot);
                layerRoot.SetAsLastSibling();
                layerRoots.Add(layer, layerRoot);
            }
        }

        private void InitializeEventSystem()
        {
            UIEventSystemComponent[] eventSystems = Object.FindObjectsByType<UIEventSystemComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (eventSystems.Length > 1) throw new InvalidOperationException($"UIViewMgr 初始化失败：场景中存在 {eventSystems.Length} 个 EventSystem，请只保留一个。");

            if (eventSystems.Length == 1)
            {
                uiEventSystem = eventSystems[0];
                uiEventSystem.gameObject.SetActive(true);
                uiEventSystem.transform.SetParent(transform, false);
            }
            else
            {
                GameObject eventSystemObject = new(UIDefines.EventSystemObjectName, typeof(UIEventSystemComponent), typeof(StandaloneInputModule));
                eventSystemObject.transform.SetParent(transform, false);
                uiEventSystem = eventSystemObject.GetComponent<UIEventSystemComponent>();
            }

            BaseInputModule[] inputModules = uiEventSystem.GetComponents<BaseInputModule>();
            if (inputModules.Length == 0)
            {
                uiEventSystem.gameObject.AddComponent<StandaloneInputModule>();
                return;
            }

            for (int i = 0; i < inputModules.Length; i++)
            {
                if (inputModules[i].enabled) return;
            }

            throw new InvalidOperationException($"EventSystem“{uiEventSystem.name}”没有启用的 BaseInputModule。");
        }

        private static void StretchToParent(RectTransform rectTransform)
        {
            if (rectTransform == null) throw new ArgumentNullException(nameof(rectTransform));
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }

        private static void SetUILayer(GameObject target)
        {
            int uiLayer = LayerMask.NameToLayer(UIDefines.UnityUILayerName);
            if (uiLayer >= 0) target.layer = uiLayer;
        }

        private void EnsureInitialized()
        {
            if (!isInitialized) throw new InvalidOperationException("UIViewMgr 尚未初始化，请在项目启动流程中调用 UIViewMgr.Instance.OnInit()。");
        }

        #endregion
    }
}
