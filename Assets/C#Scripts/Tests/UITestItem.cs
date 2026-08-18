using System;
using GameFramework.UI.Virtualization;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public sealed class UITestItem : LoopItemBase
{
    private Image targetImage;

    public Color Color
    {
        get => TargetImage.color;
        set => TargetImage.color = value;
    }

    private void Awake()
    {
        targetImage = GetComponent<Image>();
    }

    protected override void OnRefreshByData(LoopItemDataBase data)
    {
        if (data is not UITestItemData itemData)
        {
            throw new ArgumentException($"{nameof(UITestItem)} 只能使用 {nameof(UITestItemData)} 类型的数据。", nameof(data));
        }
        Color = itemData.Color;
    }

    private Image TargetImage
    {
        get
        {
            if (targetImage == null)
            {
                targetImage = GetComponent<Image>();
            }
            return targetImage;
        }
    }
}

public sealed class UITestItemData : LoopItemDataBase
{
    public Color Color { get; set; }
}
