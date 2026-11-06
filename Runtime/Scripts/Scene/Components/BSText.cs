using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using PropertyName = BS.PropertyName;

namespace BS
{
    [DefaultExecutionOrder(-1)]
    [WatchComponent]
    [RequireComponent(typeof(BSObjectId))]

    public partial class BSText : BSComponentBase
    {
        TextMeshPro tmpComponent;
        [Tooltip("The text content to display.")]
        [See(initial = "")][SerializeField] internal string text = "";

        [Tooltip("The color of the text in RGBA format.")]
        [See(initial = "1,1,1,1")][SerializeField] internal Vector4 color = new Vector4(1, 1, 1, 1);

        [Tooltip("The horizontal alignment of the text.")]
        [See(initial = "1")][SerializeField] internal HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center;

        [Tooltip("The vertical alignment of the text.")]
        [See(initial = "1")][SerializeField] internal VerticalAlignment verticalAlignment = VerticalAlignment.Center;

        [Tooltip("The font size of the text.")]
        [See(initial = "2")][SerializeField] internal float fontSize = 2;

        [Tooltip("Enable or disable rich text formatting.")]
        [See(initial = "true")][SerializeField] internal bool richText = true;

        [Tooltip("Enable or disable word wrapping.")]
        [See(initial = "true")][SerializeField] internal bool enableWordWrapping = true;

        [Tooltip("The size delta of the text RectTransform.")]
        [See(initial = "20,5")][SerializeField] internal Vector2 rectTransformSizeDelta = new Vector2(20, 5);



        internal override void UpdateStuff()
        {

        }

        internal override void StartStuff()
        {
            SetupText();
        }

        void SetupText()
        {
            if (tmpComponent == null)
            {
                tmpComponent = gameObject.GetComponent<TextMeshPro>();
                if (tmpComponent == null)
                {
                    tmpComponent = gameObject.AddComponent<TextMeshPro>();
                }
                RepairFont(tmpComponent);
            }
            tmpComponent.text = text;
            tmpComponent.color = new Color(color.x, color.y, color.z, color.w);
            tmpComponent.fontSize = fontSize;
            tmpComponent.richText = richText;
            switch (horizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    tmpComponent.alignment = TextAlignmentOptions.Left;
                    break;
                case HorizontalAlignment.Right:
                    tmpComponent.alignment = TextAlignmentOptions.Right;
                    break;
                case HorizontalAlignment.Center:
                    tmpComponent.alignment = TextAlignmentOptions.Center;
                    break;
            }
            switch (verticalAlignment)
            {
                case VerticalAlignment.Top:
                    tmpComponent.verticalAlignment = VerticalAlignmentOptions.Top;
                    break;
                case VerticalAlignment.Bottom:
                    tmpComponent.verticalAlignment = VerticalAlignmentOptions.Bottom;
                    break;
                case VerticalAlignment.Center:
                    tmpComponent.verticalAlignment = VerticalAlignmentOptions.Middle;
                    break;
            }
            tmpComponent.enableWordWrapping = enableWordWrapping;
            var rect = gameObject.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = rectTransformSizeDelta;
            }
            SetLoadedIfNot();
        }

        /// <summary>
        /// Gives text that can't draw here the app's default font. Text that can keeps the builder's font and material.
        /// </summary>
        internal static void RepairFont(TMP_Text text)
        {
            if (text == null || CanDraw(text.font, text.fontSharedMaterial))
            {
                return;
            }
            var fallback = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (fallback == null)
            {
                return;
            }
            text.font = fallback;
            text.fontSharedMaterial = fallback.material;
            text.ForceMeshUpdate();
        }

        /// <summary>
        /// Whether this font and material can show text here. The shader has to run on this device, so a bundle's copy that
        /// can't, or a material that lost its shader, fails. So does a font with no glyphs and nothing to make them from.
        /// A shader that runs but lacks a variant this device needs can't be told from here.
        /// </summary>
        internal static bool CanDraw(TMP_FontAsset font, Material material)
        {
            if (font == null || material == null || material.shader == null || !material.shader.isSupported ||
                material.shader.name == "Hidden/InternalErrorShader")
            {
                return false;
            }
            return font.atlasPopulationMode == AtlasPopulationMode.Static ? font.characterTable.Count > 0 : font.sourceFontFile != null;
        }

        internal override void DestroyStuff()
        {
            if (tmpComponent != null)
            {
                Destroy(tmpComponent);
            }
        }
        void UpdateCallback(List<PropertyName> changedProperties)
        {
            SetupText();
        }
    }
}