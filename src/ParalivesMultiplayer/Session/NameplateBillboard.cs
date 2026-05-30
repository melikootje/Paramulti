using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public class NameplateBillboard : MonoBehaviour
    {
        public string PlayerName = "Player";
        public Color Color = Color.white;

        static GUIStyle _style;
        static GUIStyle _shadowStyle;
        Camera _mainCamera;
        float _heightOffset = 0f;

        void Awake()
        {
            _mainCamera = Camera.main;
            _heightOffset = transform.localPosition.y;
        }

        void OnGUI()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            if (string.IsNullOrEmpty(PlayerName)) return;

            // World to screen
            var worldPos = transform.parent.position + Vector3.up * _heightOffset;
            var screenPos = _mainCamera.WorldToScreenPoint(worldPos);

            // Behind camera check
            if (screenPos.z < 0f) return;

            // Fade with distance
            float dist = Vector3.Distance(_mainCamera.transform.position, worldPos);
            float alpha = Mathf.Clamp01(1f - (dist - 5f) / 20f); // fade after 5m, gone at 25m
            if (alpha <= 0.05f) return;

            if (_style == null)
            {
                _style = new GUIStyle();
                _style.fontSize = 14;
                _style.fontStyle = FontStyle.Bold;
                _style.alignment = TextAnchor.MiddleCenter;
                _shadowStyle = new GUIStyle(_style);
            }

            _style.normal.textColor = new Color(Color.r, Color.g, Color.b, alpha);
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.7f);

            var content = new GUIContent(PlayerName);
            Vector2 size = _style.CalcSize(content);
            float x = screenPos.x - size.x / 2f;
            float y = Screen.height - screenPos.y - size.y / 2f;

            // Shadow
            GUI.Label(new Rect(x + 1f, y + 1f, size.x, size.y), content, _shadowStyle);
            // Text
            GUI.Label(new Rect(x, y, size.x, size.y), content, _style);
        }
    }
}
