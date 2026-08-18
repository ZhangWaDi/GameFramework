using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI
{
    /// <summary>
    /// UI 框架运行环境的统一固定配置。
    /// </summary>
    public static class UIDefines
    {
        public const string PanelResourceRoot = "Prefabs/UI/Panels";
        public const string CanvasObjectName = "UICanvas";
        public const string EventSystemObjectName = "UIEventSystem";
        public const string UnityUILayerName = "UI";
        public const string LayerObjectNameSuffix = "Layer";
        public const float CanvasMatchWidthOrHeight = 0.5f;
        public const float CanvasReferencePixelsPerUnit = 100f;
        public const bool CanvasPixelPerfect = false;
        public const bool GraphicRaycasterIgnoreReversedGraphics = true;
        public const int CanvasSortingOrder = 0;
        public const int CanvasTargetDisplay = 0;
        public const RenderMode CanvasRenderMode = RenderMode.ScreenSpaceOverlay;
        public const CanvasScaler.ScaleMode CanvasScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        public const CanvasScaler.ScreenMatchMode CanvasScreenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        public static readonly Vector2 CanvasReferenceResolution = new(1920f, 1080f);
        public static IReadOnlyList<UILayer> LayerOrder { get; } = Array.AsReadOnly(new[] { UILayer.Background, UILayer.Normal, UILayer.Popup, UILayer.Overlay });

        public static string GetLayerObjectName(UILayer layer)
        {
            return $"{layer}{LayerObjectNameSuffix}";
        }
    }

    /// <summary>
    /// UI 面板的固定渲染层级，声明顺序由低到高。
    /// </summary>
    public enum UILayer
    {
        Background,
        Normal,
        Popup,
        Overlay
    }
}
