using System;
using System.Collections.Generic;
using Safehouse.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.UI
{
    /// <summary>Turns a route's 0–1 coordinates into a point inside the map, inset by the margin.</summary>
    public static class RouteMapLayout
    {
        public const float Margin = 52f;
        public const float NodeSize = 64f;

        public static Vector2 Center(float x, float y, float width, float height)
        {
            var marginX = width > Margin * 2f ? Margin : Mathf.Max(0f, width * 0.5f - 1f);
            var marginY = height > Margin * 2f ? Margin : Mathf.Max(0f, height * 0.5f - 1f);
            var innerW = Mathf.Max(1f, width - marginX * 2f);
            var innerH = Mathf.Max(1f, height - marginY * 2f);
            return new Vector2(marginX + Mathf.Clamp01(x) * innerW, marginY + Mathf.Clamp01(y) * innerH);
        }
    }

    /// <summary>
    /// The direct-play route: a map plate, forward edges, and a token for each node.
    /// Only a neighbor of the current node accepts a click.
    /// </summary>
    public sealed class RouteMap : VisualElement
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "start", "ENTRY" },
            { "container", "CACHE" },
            { "loose", "LOOT" },
            { "corpse", "BODY" },
            { "noise", "NOISE" },
            { "boss_lair", "BOSS" },
            { "exit", "EXIT" },
        };

        private readonly VisualElement _lines;
        private Route _route;
        private bool _canMove;
        private Action<string> _onMove;
        private float _width;
        private float _height;

        public RouteMap()
        {
            AddToClassList("raid-map");
            pickingMode = PickingMode.Ignore;

            var plate = new VisualElement { name = "raid-plate", pickingMode = PickingMode.Ignore };
            plate.AddToClassList("raid-plate");
            Stretch(plate);
            var texture = RaidArt.Plate;
            if (texture != null)
            {
                plate.style.backgroundImage = Background.FromTexture2D(texture);
                plate.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            }

            Add(plate);

            _lines = new VisualElement { name = "raid-lines", pickingMode = PickingMode.Ignore };
            Stretch(_lines);
            _lines.generateVisualContent += PaintLines;
            Add(_lines);

            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (Mathf.Approximately(_width, evt.newRect.width) && Mathf.Approximately(_height, evt.newRect.height))
                {
                    return;
                }

                _width = evt.newRect.width;
                _height = evt.newRect.height;
                Rebuild();
            });
        }

        public void Show(Route route, bool canMove, Action<string> onMove)
        {
            _route = route;
            _canMove = canMove;
            _onMove = onMove;
            Rebuild();
        }

        private void Rebuild()
        {
            var stale = new List<VisualElement>();
            foreach (var child in Children())
            {
                if (child.ClassListContains("raid-token") || child.ClassListContains("raid-legend"))
                {
                    stale.Add(child);
                }
            }

            foreach (var child in stale)
            {
                child.RemoveFromHierarchy();
            }

            if (_route == null || _width < 10f || _height < 10f)
            {
                _lines.MarkDirtyRepaint();
                return;
            }

            var current = _route.Nodes[_route.CurrentNodeId];
            foreach (var node in _route.Nodes.Values)
            {
                var open = _canMove && IsNeighbor(current, node.NodeId);
                var here = node.NodeId == current.NodeId;
                Add(Token(node, here, open));
            }

            var legend = new Label("GOLD LINES ARE OPEN") { pickingMode = PickingMode.Ignore };
            legend.AddToClassList("raid-legend");
            legend.AddToClassList("text-label");
            Add(legend);
            _lines.MarkDirtyRepaint();
        }

        private VisualElement Token(RouteNode node, bool here, bool open)
        {
            var center = RouteMapLayout.Center((float)node.X, (float)node.Y, _width, _height);
            var token = new VisualElement { pickingMode = PickingMode.Ignore };
            token.AddToClassList("raid-token");
            token.style.left = center.x - 40f;
            token.style.top = center.y - RouteMapLayout.NodeSize / 2f;

            if (here || open)
            {
                var ring = new VisualElement { pickingMode = PickingMode.Ignore };
                ring.AddToClassList("raid-ring");
                ring.AddToClassList(here ? "raid-ring-current" : "raid-ring-open");
                token.Add(ring);
            }

            var face = new VisualElement();
            face.AddToClassList("raid-node");
            if (here)
            {
                face.AddToClassList("raid-node-current");
            }
            else if (open)
            {
                face.AddToClassList("raid-node-open");
                face.pickingMode = PickingMode.Position;
                var id = node.NodeId;
                face.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    _onMove?.Invoke(id);
                });
            }
            else
            {
                face.AddToClassList(node.Cleared ? "raid-node-cleared" : "raid-node-locked");
                face.pickingMode = PickingMode.Ignore;
            }

            var art = RaidArt.Node(node.Kind);
            if (art != null)
            {
                face.style.backgroundImage = Background.FromTexture2D(art);
                face.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }

            token.Add(face);

            string caption;
            var label = new Label(Labels.TryGetValue(node.Kind, out caption) ? caption : node.Kind)
            {
                pickingMode = PickingMode.Ignore,
            };
            label.AddToClassList("raid-node-label");
            if (here)
            {
                label.AddToClassList("raid-node-label-current");
            }

            token.Add(label);
            return token;
        }

        private void PaintLines(MeshGenerationContext context)
        {
            if (_route == null || _width < 10f || _height < 10f)
            {
                return;
            }

            var painter = context.painter2D;
            var current = _route.Nodes[_route.CurrentNodeId];
            foreach (var node in _route.Nodes.Values)
            {
                if (node.NodeId == current.NodeId)
                {
                    continue;
                }

                DrawEdges(painter, node, false);
            }

            DrawEdges(painter, current, true);
        }

        private void DrawEdges(Painter2D painter, RouteNode node, bool fromCurrent)
        {
            var from = RouteMapLayout.Center((float)node.X, (float)node.Y, _width, _height);
            foreach (var neighbor in node.Neighbors)
            {
                RouteNode next;
                if (!_route.Nodes.TryGetValue(neighbor, out next))
                {
                    continue;
                }

                var to = RouteMapLayout.Center((float)next.X, (float)next.Y, _width, _height);
                var delta = to - from;
                var length = delta.magnitude;
                if (length < 1f)
                {
                    continue;
                }

                var dir = delta / length;
                var start = from + dir * 28f;
                var end = to - dir * 36f;
                painter.strokeColor = fromCurrent
                    ? new Color(0.851f, 0.643f, 0.255f, 0.95f)
                    : new Color(0.35f, 0.40f, 0.46f, 0.55f);
                painter.lineWidth = fromCurrent ? 2.5f : 1.5f;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                painter.MoveTo(start);
                painter.LineTo(end);
                painter.Stroke();

                var side = new Vector2(-dir.y, dir.x);
                painter.fillColor = painter.strokeColor;
                painter.BeginPath();
                painter.MoveTo(end);
                painter.LineTo(end - dir * 9f + side * 4.5f);
                painter.LineTo(end - dir * 9f - side * 4.5f);
                painter.ClosePath();
                painter.Fill();
            }
        }

        private static bool IsNeighbor(RouteNode node, string nodeId)
        {
            for (var index = 0; index < node.Neighbors.Count; index++)
            {
                if (node.Neighbors[index] == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }
    }
}
