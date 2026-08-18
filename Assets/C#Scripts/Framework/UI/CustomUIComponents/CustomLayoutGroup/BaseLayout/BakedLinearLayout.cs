using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.UI.Layout
{
    public abstract class BakedLinearLayout : BakedLayout
    {
        [SerializeField] private RectOffset padding = new();
        [SerializeField] private float spacing;
        [SerializeField] private bool useCustomSpacing;
        [SerializeField] private List<float> customSpacingList = new();
        [SerializeField] private TextAnchor childAlignment = TextAnchor.UpperLeft;

        protected RectOffset Padding => padding ??= new();
        protected TextAnchor ChildAlignment => childAlignment;

        protected float GetSpacingAfter(int childIndex)
        {
            if (useCustomSpacing && childIndex >= 0 && childIndex < customSpacingList.Count) return customSpacingList[childIndex];
            return spacing;
        }

        protected float GetTotalSpacing(int childCount)
        {
            float totalSpacing = 0f;
            for (int i = 0; i < childCount - 1; i++) totalSpacing += GetSpacingAfter(i);
            return totalSpacing;
        }

        protected static float GetHorizontalAlignment(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => 0.5f,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => 1f,
                _ => 0f
            };
        }

        protected static float GetVerticalAlignment(TextAnchor alignment)
        {
            return alignment switch
            {
                TextAnchor.MiddleLeft or TextAnchor.MiddleCenter or TextAnchor.MiddleRight => 0.5f,
                TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight => 1f,
                _ => 0f
            };
        }
    }
}
