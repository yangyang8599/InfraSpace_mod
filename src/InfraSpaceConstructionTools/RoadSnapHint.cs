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
                ? "道路磁吸：已关闭    |    按 H 开启    |    网格对齐不受影响"
                : "道路磁吸：已开启    |    按 H 关闭    |    网格对齐不受影响";
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
