using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>Shared overlay scaling. Every panel renders through a <see cref="GUI.matrix"/> scaled by
    /// <see cref="Fit"/>: the user's preferred <c>UIScale</c>, automatically capped so the panel never
    /// exceeds the screen (the "off-screen on small displays" fix). Panels scale about their top-left
    /// corner, so the stored panel rect stays the anchor; mouse hits are mapped back with <see cref="ToLocal"/>.</summary>
    public static class UiScale
    {
        public const float Min = 0.6f, Max = 1.5f, Step = 0.05f;

        /// <summary>The user's preferred multiplier (1.0 = native), clamped to a sane range.</summary>
        public static float User => Plugin.UIScale != null ? Mathf.Clamp(Plugin.UIScale.Value, Min, Max) : 1f;

        /// <summary>Scale that fits a panelW×panelH panel on screen without ever exceeding the user's
        /// preference — i.e. <c>min(User, screenW/panelW, screenH/panelH)</c>, floored so it never vanishes.</summary>
        public static float Fit(float panelW, float panelH)
        {
            float s = User;
            if (panelW > 1f) s = Mathf.Min(s, Screen.width / panelW);
            if (panelH > 1f) s = Mathf.Min(s, Screen.height / panelH);
            return Mathf.Max(0.3f, s);
        }

        /// <summary>A GUI matrix that scales by <paramref name="scale"/> about the pivot (panel top-left).</summary>
        public static Matrix4x4 Matrix(float pivotX, float pivotY, float scale)
        {
            var p = new Vector3(pivotX, pivotY, 0f);
            return Matrix4x4.Translate(p) * Matrix4x4.Scale(new Vector3(scale, scale, 1f)) * Matrix4x4.Translate(-p);
        }

        /// <summary>Map a real GUI-space mouse position into the panel's unscaled local space, so existing
        /// hit-tests against unscaled rects keep working after the panel is drawn scaled.</summary>
        public static Vector2 ToLocal(Vector2 m, float pivotX, float pivotY, float scale)
        {
            if (scale == 1f) return m;
            return new Vector2((m.x - pivotX) / scale + pivotX, (m.y - pivotY) / scale + pivotY);
        }

        /// <summary>Nudge the user's UI scale by <paramref name="delta"/>, snapped to <see cref="Step"/>.</summary>
        public static void Adjust(float delta)
        {
            if (Plugin.UIScale == null) return;
            float v = Mathf.Round((Plugin.UIScale.Value + delta) / Step) * Step;
            Plugin.UIScale.Value = Mathf.Clamp(v, Min, Max);
        }
    }
}
