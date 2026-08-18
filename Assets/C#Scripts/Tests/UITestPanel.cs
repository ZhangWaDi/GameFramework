using System.Collections.Generic;
using GameFramework.UI;
using GameFramework.UI.Virtualization;
using UnityEngine;

public partial class UITestPanel : BasePanel
{
    private const int TestItemCount = 200;
    private readonly List<UITestItemData> dataList = new();

    private void Start()
    {
        LoadData();
        scrollView.RefreshDataList(dataList);
    }

    private void LoadData()
    {
        dataList.Clear();
        for (int i = 0; i < TestItemCount; i++)
        {
            Color color = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.7f, 1f);
            dataList.Add(new UITestItemData { Color = color });
        }
    }
}


