using UnityEngine;

namespace GameFramework.UI.Layout
{
    public abstract class BakedRadialLayout : BakedLayout
    {
        [SerializeField, Min(0f)] private float radius = 100f;
        [SerializeField] private Vector2 centerOffset;
        [SerializeField, Range(-360f, 360f)] private float startAngle;
        [SerializeField] private bool clockwise = true;
        [SerializeField] private bool faceCenter;
        [SerializeField, Range(-360f, 360f)] private float rotationOffset;

        protected void PlaceChild(RectTransform child, float angleOffset)
        {
            float angle = startAngle + (clockwise ? angleOffset : -angleOffset);
            float radian = angle * Mathf.Deg2Rad;
            Vector2 center = LayoutRectTransform.rect.center + centerOffset;
            Vector2 direction = new(Mathf.Sin(radian), Mathf.Cos(radian));
            Vector2 position = center + direction * radius;
            SetChildLocalPosition(child, position);
            if (faceCenter) SetChildFacingPoint(child, position, center, rotationOffset);
        }
    }
}
