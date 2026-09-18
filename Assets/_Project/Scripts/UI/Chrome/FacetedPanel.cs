using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.UI.Chrome
{
    /// <summary>
    /// A panel with its top-left and bottom-right corners cut off, a one-pixel top highlight and
    /// bottom shadow standing in for a bevel, and small accent brackets on the two corners left
    /// square. USS has no clip-path and no gradients, so this draws its own background with
    /// Painter2D instead of relying on background-color/border-width - those stay unset on this
    /// element's USS so nothing double-draws underneath.
    /// </summary>
    [UxmlElement]
    public partial class FacetedPanel : VisualElement
    {
        [UxmlAttribute] public float CutSize { get; set; } = 14f;
        [UxmlAttribute] public float BracketSize { get; set; } = 18f;
        [UxmlAttribute] public Color FillColor { get; set; } = new Color(0.094f, 0.106f, 0.125f);
        [UxmlAttribute] public Color BorderColor { get; set; } = new Color(0.149f, 0.176f, 0.212f);
        [UxmlAttribute] public Color TopHighlight { get; set; } = new Color(1f, 1f, 1f, 0.05f);
        [UxmlAttribute] public Color BottomShadow { get; set; } = new Color(0f, 0f, 0f, 0.5f);
        [UxmlAttribute] public Color AccentColor { get; set; } = new Color(0.851f, 0.643f, 0.255f);
        [UxmlAttribute] public bool ShowBrackets { get; set; } = true;

        public FacetedPanel()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>
        /// The box the frame is drawn around, in this element's own coordinates: its whole size,
        /// padding included - NOT <see cref="VisualElement.contentRect"/>, which is what is left
        /// over after padding. Drawing to contentRect put the frame at the element's top-left but
        /// sized it two paddings too small, so children - which sit inside the padding - hung out
        /// past its right and bottom edges while the left and top still looked correctly inset.
        /// Exposed so a test can pin that down; see FacetedPanelTests.
        /// </summary>
        public Rect FrameRect => new Rect(0, 0, layout.width, layout.height);

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var w = FrameRect.width;
            var h = FrameRect.height;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            // Clamp so a panel narrower/shorter than twice the cut never produces a bowtie shape.
            var cut = Mathf.Min(CutSize, w / 2f, h / 2f);

            var painter = mgc.painter2D;

            // The cut-corner hexagon: square at top-right and bottom-left, cut at top-left and
            // bottom-right - matches the artboard's clip-path polygon exactly.
            painter.BeginPath();
            painter.MoveTo(new Vector2(cut, 0));
            painter.LineTo(new Vector2(w, 0));
            painter.LineTo(new Vector2(w, h - cut));
            painter.LineTo(new Vector2(w - cut, h));
            painter.LineTo(new Vector2(0, h));
            painter.LineTo(new Vector2(0, cut));
            painter.ClosePath();
            painter.fillColor = FillColor;
            painter.Fill();
            painter.strokeColor = BorderColor;
            painter.lineWidth = 1f;
            painter.Stroke();

            // Bevel: a light line along the top edge, a dark one along the bottom edge, inset by
            // half a pixel so they sit inside the border stroke rather than on top of it.
            painter.BeginPath();
            painter.MoveTo(new Vector2(cut, 0.5f));
            painter.LineTo(new Vector2(w, 0.5f));
            painter.strokeColor = TopHighlight;
            painter.lineWidth = 1f;
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(new Vector2(w - cut, h - 0.5f));
            painter.LineTo(new Vector2(0, h - 0.5f));
            painter.strokeColor = BottomShadow;
            painter.lineWidth = 1f;
            painter.Stroke();

            if (!ShowBrackets)
            {
                return;
            }

            var bracket = Mathf.Min(BracketSize, w, h);
            painter.strokeColor = AccentColor;
            painter.lineWidth = 2f;

            // Top-right corner: one arm along the top edge, one along the right edge.
            painter.BeginPath();
            painter.MoveTo(new Vector2(w - bracket, 1f));
            painter.LineTo(new Vector2(w - 1f, 1f));
            painter.LineTo(new Vector2(w - 1f, bracket));
            painter.Stroke();

            // Bottom-left corner: the same shape, rotated to the opposite square corner.
            painter.BeginPath();
            painter.MoveTo(new Vector2(bracket, h - 1f));
            painter.LineTo(new Vector2(1f, h - 1f));
            painter.LineTo(new Vector2(1f, h - bracket));
            painter.Stroke();
        }
    }
}
