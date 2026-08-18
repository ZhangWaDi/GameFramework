using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.UI.Layout
{
    public enum BakedFanAlignment
    {
        Start,
        Center,
        End
    }

    /// <summary>
    /// 将直接子节点按固定或自定义角度间隔烘焙为扇形。
    /// </summary>
    public sealed class BakedFanLayout : BakedRadialLayout
    {
        [SerializeField] private BakedFanAlignment alignment = BakedFanAlignment.Center;
        [SerializeField] private float spacingAngle = 30f;
        [SerializeField] private bool useCustomSpacingAngle;
        [SerializeField] private List<float> customSpacingAngleList = new();

        protected override void Arrange(IReadOnlyList<RectTransform> layoutChildren)
        {
            if (layoutChildren.Count == 0) return;

            float totalAngle = 0f;
            for (int i = 0; i < layoutChildren.Count - 1; i++) totalAngle += GetSpacingAngleAfter(i);
            float currentAngle = alignment switch
            {
                BakedFanAlignment.Center => -totalAngle * 0.5f,
                BakedFanAlignment.End => -totalAngle,
                _ => 0f
            };

            for (int i = 0; i < layoutChildren.Count; i++)
            {
                PlaceChild(layoutChildren[i], currentAngle);
                if (i < layoutChildren.Count - 1) currentAngle += GetSpacingAngleAfter(i);
            }
        }

        private float GetSpacingAngleAfter(int childIndex)
        {
            if (useCustomSpacingAngle && childIndex >= 0 && childIndex < customSpacingAngleList.Count) return customSpacingAngleList[childIndex];
            return spacingAngle;
        }
    }
}
