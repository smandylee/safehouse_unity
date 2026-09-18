using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.UI.Chrome
{
    /// <summary>
    /// A recessed "well" for a stash grid: a dark fill, an inset-look border, and faint lines every
    /// <see cref="Pitch"/> pixels. USS has no repeating-gradient background, so the lines are drawn
    /// directly with Painter2D instead of needing a tiled texture asset.
    /// </summary>
    [UxmlElement]
    public partial class GridBackground : VisualElement
    {
        [UxmlAttribute] public float Pitch { get; set; } = 48f;
        [UxmlAttribute] public Color FillColor { get; set; } = new Color(0.071f, 0.082f, 0.102f);
        [UxmlAttribute] public Color BorderColor { get; set; } = new Color(0.149f, 0.176f, 0.212f);
        [UxmlAttribute] public Color LineColor { get; set; } = new Color(1f, 1f, 1f, 0.035f);

        public GridBackground()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            // layout.width/height rather than contentRect, for the same reason as FacetedPanel:
            // contentRect is inset by padding, so the drawn well would not line up with the box.
            var w = layout.width;
            var h = layout.height;
            if (w <= 0 || h <= 0 || Pitch <= 0)
            {
                return;
            }

            var painter = mgc.painter2D;

            painter.BeginPath();
            painter.MoveTo(Vector2.zero);
            painter.LineTo(new Vector2(w, 0));
            painter.LineTo(new Vector2(w, h));
            painter.LineTo(new Vector2(0, h));
            painter.ClosePath();
            painter.fillColor = FillColor;
            painter.Fill();
            painter.strokeColor = BorderColor;
            painter.lineWidth = 1f;
            painter.Stroke();

            painter.strokeColor = LineColor;
            painter.lineWidth = 1f;
            for (var x = Pitch; x < w; x += Pitch)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0));
                painter.LineTo(new Vector2(x, h));
                painter.Stroke();
            }

            for (var y = Pitch; y < h; y += Pitch)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, y));
                painter.LineTo(new Vector2(w, y));
                painter.Stroke();
            }
        }
    }
}
