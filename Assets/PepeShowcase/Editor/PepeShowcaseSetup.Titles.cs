using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        static void BuildTitles(PepeAttackShowcase showcase)
        {
            var root = new GameObject("Showcase titles", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = showcase.director.output;
            canvas.planeDistance = .5f;
            canvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/Nunito/Nunito-Black.ttf");
            var presentation = showcase.presentation;
            presentation.heading = Text(root.transform, "PEPE / ATTACK SHOWCASE", font, 25,
                new Vector2(520, 50), new Vector2(50, -42), new Vector2(0, 1), TextAnchor.MiddleLeft);
            presentation.takeTitle = Text(root.transform, "KATANA", font, 44,
                new Vector2(600, 65), new Vector2(50, -88), new Vector2(0, 1), TextAnchor.MiddleLeft);
            presentation.counter = Text(root.transform, "01 / " + showcase.library.takes.Length.ToString("00"), font, 24,
                new Vector2(200, 45), new Vector2(50, 38), Vector2.zero, TextAnchor.MiddleLeft);
            var tag = Text(root.transform, "ZERO SPACE", font, 22,
                new Vector2(220, 45), new Vector2(-48, 38), new Vector2(1, 0), TextAnchor.MiddleRight);
            tag.color = new Color(.7f, .95f, 1);
            var progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(root.transform, false);
            var rect = progress.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.offsetMin = new Vector2(50, 24);
            rect.offsetMax = new Vector2(-50, 27);
            presentation.progress = progress.GetComponent<Image>();
            presentation.progress.color = new Color(.4f, .9f, 1, .8f);
            presentation.progress.type = Image.Type.Filled;
            presentation.progress.fillMethod = Image.FillMethod.Horizontal;
            presentation.progress.raycastTarget = false;
            var card = new GameObject("Intro and outro", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            card.transform.SetParent(root.transform, false);
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.one;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;
            card.GetComponent<Image>().color = new Color(.025f, .055f, .065f, .95f);
            card.GetComponent<Image>().raycastTarget = false;
            presentation.intro = card.GetComponent<CanvasGroup>();
            presentation.intro.alpha = 0;
            presentation.introTitle = Text(card.transform, "PEPE\nATTACK SHOWCASE", font, 76,
                new Vector2(1400, 230), Vector2.zero, new Vector2(.5f, .5f), TextAnchor.MiddleCenter);
        }

        static Text Text(Transform parent, string value, Font font, int size, Vector2 dimensions,
            Vector2 position, Vector2 anchor, TextAnchor alignment)
        {
            var node = new GameObject(value.Replace("\n", " "), typeof(RectTransform), typeof(Text), typeof(Shadow));
            node.transform.SetParent(parent, false);
            var rect = node.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            var text = node.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(.94f, .99f, 1);
            text.raycastTarget = false;
            node.GetComponent<Shadow>().effectColor = new Color(.03f, .1f, .13f, .7f);
            node.GetComponent<Shadow>().effectDistance = new Vector2(1, -2);
            return text;
        }
    }
}
