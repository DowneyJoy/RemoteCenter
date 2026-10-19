using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RemoteCenter
{
    /// 运行时构建 uGUI 界面的工具方法（两端 UI 全部由代码生成，无需预制体）。
    public static class UIFactory
    {
        static Font _font;

        /// 全平台优先使用随包发布的 Noto Sans SC（Android 无系统动态字体机制，不能依赖 OS 字体）。
        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.Load<Font>("Codely/Fonts/NotoSansSC-Regular");
                    if (_font == null)
                    {
                        try { _font = null; }//new Font("Microsoft YaHei")
                        catch { }
                        if (_font == null)
                            _font = GetBuiltinFont("LegacyRuntime.ttf") ?? GetBuiltinFont("Arial.ttf");
                    }
                }
                return _font;
            }
        }

        static Font GetBuiltinFont(string name)
        {
            try { return Resources.GetBuiltinResource<Font>(name); }
            catch { return null; }
        }

        static Sprite _roundedSprite;

        /// 运行时程序化生成圆角底图，不依赖引擎内置 UI 贴图（Tuanjie 未附带 UISprite 等资源）。
        static Sprite GetRoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;
            const int size = 24;
            const int radius = 6;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = x - Mathf.Clamp(x, radius, size - 1 - radius);
                    int dy = y - Mathf.Clamp(y, radius, size - 1 - radius);
                    bool inside = dx * dx + dy * dy <= radius * radius;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            _roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _roundedSprite.name = "RoundedBox";
            return _roundedSprite;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();
            return canvas;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.text = content;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, int fontSize)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(0.23f, 0.27f, 0.35f, 1f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var label2 = CreateText(go.transform, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter);
            Stretch(label2.rectTransform);
            return button;
        }

        public static Slider CreateSlider(Transform parent, string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent, false);
            var slider = root.GetComponent<Slider>();

            var background = CreateImage(root.transform, "Background", new Color(1f, 1f, 1f, 0.22f));
            Stretch(background.rectTransform);

            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(root.transform, false);
            var fill = fillGo.GetComponent<Image>();
            fill.color = new Color(0.25f, 0.78f, 0.45f);
            RectTransform fillRt = fill.rectTransform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
            fillRt.sizeDelta = Vector2.zero;

            slider.fillRect = fillRt;
            slider.targetGraphic = fill;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        public static InputField CreateInputField(Transform parent, string name, string initialText, string placeholderText)
        {
            var root = new GameObject(name, typeof(Image), typeof(InputField));
            root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(0.13f, 0.13f, 0.15f, 0.92f);

            var input = root.GetComponent<InputField>();
            input.image = image;

            var text = CreateText(root.transform, "Text", initialText, 21, Color.white, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(12f, 1f);
            text.rectTransform.offsetMax = new Vector2(-12f, -1f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var placeholder = CreateText(root.transform, "Placeholder", placeholderText, 21, new Color(1f, 1f, 1f, 0.38f), TextAnchor.MiddleLeft);
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(12f, 1f);
            placeholder.rectTransform.offsetMax = new Vector2(-12f, -1f);
            placeholder.horizontalOverflow = HorizontalWrapMode.Overflow;

            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = initialText;
            return input;
        }
    }
}
