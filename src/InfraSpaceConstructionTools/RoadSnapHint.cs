using UnityEngine;

namespace InfraSpaceConstructionTools
{
    internal sealed class RoadSnapHint : MonoBehaviour
    {
        private static GUIStyle labelStyle;

        private void OnGUI()
        {
            bool buildingRoad = RoadSnapFeature.IsBuildingRoad();
            bool buildingLine = RoadSnapFeature.IsBuildingLine();
            if (!buildingLine)
            {
                return;
            }

            EnsureStyle();
            bool straight = StraightExtendFeature.Enabled;
            string straightText = straight
                ? "直线延伸：已开启    |    按 J 关闭    |    沿选中节点切线延长，鼠标只改变长度"
                : "直线延伸：已关闭    |    按 J 开启    |    沿选中节点切线延长，鼠标只改变长度";
            string text = straightText;
            if (buildingRoad)
            {
                bool suppressed = RoadSnapFeature.MagnetSuppressed;
                string magnetText = suppressed
                    ? "道路磁吸：已关闭    |    按 H 开启    |    网格对齐不受影响"
                    : "道路磁吸：已开启    |    按 H 关闭    |    网格对齐不受影响";
                text = magnetText + "\n" + straightText;
            }

            float width = Mathf.Max(920f, labelStyle.CalcSize(new GUIContent(straightText)).x + 48f);
            float height = buildingRoad ? 78f : 48f;
            Rect rect = new Rect((Screen.width - width) * 0.5f, 72f, width, height);
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.12f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
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
