using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class RoadSnapHint : MonoBehaviour
    {
        private static GUIStyle labelStyle;

        private void OnGUI()
        {
            if (!RoadSnapFeature.IsBuildingRoad())
            {
                return;
            }

            EnsureStyle();
            bool suppressed = RoadSnapFeature.MagnetSuppressed;
            string text = suppressed
                ? "公路吸附：关    按 H 恢复接路    格子对齐仍然有效"
                : "公路吸附：开    按 H 关掉贴到下层公路";
            Rect rect = new Rect((Screen.width - 640f) * 0.5f, 72f, 640f, 48f);
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = suppressed ? new Color(1f, 0.84f, 0.38f) : Color.white;
            GUI.Label(rect, text, labelStyle);
            GUI.color = previous;
        }

        private static void EnsureStyle()
        {
            if (labelStyle != null)
            {
                return;
            }

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold
            };
            labelStyle.normal.textColor = Color.white;
        }
    }
}
