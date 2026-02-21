using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.ExportArtifacts;

internal static partial class DocxVisualFenceMaterializer {
    private const int MaxMermaidChars = 12000;
    private const int MaxChartChars = 20000;
    private const int MaxNetworkChars = 24000;
    private const int MaxNetworkNodes = 220;
    private const int MaxNetworkEdges = 520;

    private static readonly string[] SupportedLanguages = ["mermaid", "ix-chart", "chart", "ix-network"];
    private static readonly string[] Palette = [
        "#4cc3ff",
        "#78d9a0",
        "#ffb86b",
        "#ff7ea7",
        "#a9b8ff",
        "#9be7ff",
        "#f9e27d",
        "#d1a8ff"
    ];

    public static DocxVisualFenceMaterialization Materialize(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return new DocxVisualFenceMaterialization(markdown ?? string.Empty, tempDirectory: null);
        }

        var newline = DetectLineEnding(markdown);
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var outputLines = new List<string>(lines.Length);

        string? tempDirectory = null;
        var imageIndex = 0;
        var replacedAny = false;

        for (var i = 0; i < lines.Length;) {
            var line = lines[i] ?? string.Empty;
            if (!TryReadFenceStart(line, out var marker, out var markerRunLength, out var language)) {
                outputLines.Add(line);
                i++;
                continue;
            }

            var closingIndex = FindClosingFence(lines, i + 1, marker, markerRunLength);
            if (closingIndex < 0) {
                outputLines.Add(line);
                i++;
                continue;
            }

            var supported = !string.IsNullOrWhiteSpace(language)
                && SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);
            if (!supported) {
                AppendLines(lines, outputLines, i, closingIndex);
                i = closingIndex + 1;
                continue;
            }

            var source = string.Join("\n", lines.Skip(i + 1).Take(Math.Max(0, closingIndex - i - 1)));
            tempDirectory ??= CreateTempDirectory();
            if (TryRenderVisual(language, source, tempDirectory, ++imageIndex, out var imagePath, out var altText)) {
                outputLines.Add("![" + altText + "](<" + ToMarkdownPath(imagePath) + ">)");
                replacedAny = true;
            } else {
                AppendLines(lines, outputLines, i, closingIndex);
            }

            i = closingIndex + 1;
        }

        if (!replacedAny && tempDirectory is not null) {
            TryDeleteDirectory(tempDirectory);
            tempDirectory = null;
        }

        var rewritten = string.Join("\n", outputLines);
        if (!string.Equals(newline, "\n", StringComparison.Ordinal)) {
            rewritten = rewritten.Replace("\n", newline, StringComparison.Ordinal);
        }

        return new DocxVisualFenceMaterialization(rewritten, tempDirectory);
    }

    private static bool TryRenderVisual(string language, string source, string tempDirectory, int imageIndex, out string imagePath, out string altText) {
        imagePath = string.Empty;
        altText = string.Empty;
        var normalized = (language ?? string.Empty).Trim().ToLowerInvariant();
        var content = NormalizeText(source);

        if (normalized == "mermaid") {
            if (content.Length == 0 || content.Length > MaxMermaidChars) {
                return false;
            }

            var svg = RenderMermaidPreviewSvg(content);
            imagePath = WriteSvg(tempDirectory, imageIndex, "mermaid", svg);
            altText = "Mermaid diagram";
            return true;
        }

        if (normalized is "ix-chart" or "chart") {
            if (content.Length == 0 || content.Length > MaxChartChars) {
                return false;
            }

            if (!TryRenderChartSvg(content, out var svg)) {
                return false;
            }

            imagePath = WriteSvg(tempDirectory, imageIndex, "chart", svg);
            altText = "Chart preview";
            return true;
        }

        if (normalized == "ix-network") {
            if (content.Length == 0 || content.Length > MaxNetworkChars) {
                return false;
            }

            if (!TryRenderNetworkSvg(content, out var svg)) {
                return false;
            }

            imagePath = WriteSvg(tempDirectory, imageIndex, "network", svg);
            altText = "Network preview";
            return true;
        }

        return false;
    }

    private static void AppendLines(string[] source, List<string> destination, int fromInclusive, int toInclusive) {
        for (var idx = fromInclusive; idx <= toInclusive; idx++) {
            destination.Add(source[idx] ?? string.Empty);
        }
    }

    private static string RenderMermaidPreviewSvg(string source) {
        var lines = source
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .Where(static line => line.Length > 0)
            .Take(28)
            .ToArray();
        if (lines.Length == 0) {
            lines = ["(empty diagram source)"];
        }

        const int width = 960;
        var height = 110 + lines.Length * 18;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(width.ToString(CultureInfo.InvariantCulture))
            .Append("' height='").Append(height.ToString(CultureInfo.InvariantCulture))
            .Append("' viewBox='0 0 ").Append(width.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(height.ToString(CultureInfo.InvariantCulture)).Append("'>");
        sb.Append("<rect x='0' y='0' width='100%' height='100%' fill='#0f172a'/>");
        sb.Append("<rect x='14' y='14' width='932' height='").Append((height - 28).ToString(CultureInfo.InvariantCulture)).Append("' rx='12' fill='#111f35' stroke='#3b4f6a'/>");
        sb.Append("<text x='28' y='44' fill='#9ad9ff' font-size='21' font-family='Segoe UI, Arial'>Mermaid Diagram</text>");
        sb.Append("<text x='28' y='66' fill='#9ab2cc' font-size='12' font-family='Segoe UI, Arial'>Rendered export preview from mermaid source</text>");

        var y = 94;
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.Length > 110) {
                line = line[..107] + "...";
            }

            sb.Append("<text x='28' y='").Append(y.ToString(CultureInfo.InvariantCulture))
                .Append("' fill='#e8f3ff' font-size='13' font-family='Consolas, monospace'>")
                .Append(EscapeXml(line))
                .Append("</text>");
            y += 18;
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static bool TryRenderChartSvg(string json, out string svg) {
        svg = string.Empty;
        JsonDocument doc;
        try {
            doc = JsonDocument.Parse(json);
        } catch {
            return false;
        }

        using (doc) {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            var chartType = ReadStringProperty(root, "type", maxLength: 32).ToLowerInvariant();
            if (chartType.Length == 0) {
                return false;
            }

            if (!TryGetProperty(root, "data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!TryGetProperty(dataElement, "datasets", out var datasetsElement) || datasetsElement.ValueKind != JsonValueKind.Array) {
                return false;
            }

            var datasets = datasetsElement.EnumerateArray().ToArray();
            if (datasets.Length == 0) {
                return false;
            }

            var first = datasets[0];
            if (first.ValueKind != JsonValueKind.Object || !TryGetProperty(first, "data", out var firstData) || firstData.ValueKind != JsonValueKind.Array) {
                return false;
            }

            var values = new List<double>();
            var radii = new List<double>();
            foreach (var value in firstData.EnumerateArray()) {
                if (TryReadChartPoint(value, out var y, out var r)) {
                    values.Add(y);
                    radii.Add(r);
                }
            }

            if (values.Count == 0) {
                return false;
            }

            if (values.Count > 200) {
                values = values.Take(200).ToList();
                radii = radii.Take(200).ToList();
            }

            var labels = new List<string>();
            if (TryGetProperty(dataElement, "labels", out var labelsElement) && labelsElement.ValueKind == JsonValueKind.Array) {
                foreach (var label in labelsElement.EnumerateArray()) {
                    labels.Add(TrimAndCap(label.ValueKind == JsonValueKind.String ? label.GetString() : string.Empty, 40));
                    if (labels.Count >= values.Count) {
                        break;
                    }
                }
            }

            while (labels.Count < values.Count) {
                labels.Add((labels.Count + 1).ToString(CultureInfo.InvariantCulture));
            }

            var datasetLabel = ReadStringProperty(first, "label", 80);
            var explicitColor = ReadChartColor(first);
            if (chartType is "pie" or "doughnut" or "polararea") {
                svg = RenderPieLikeChartSvg(chartType, values, labels, datasetLabel, explicitColor);
                return true;
            }

            svg = RenderCartesianChartSvg(chartType, values, radii, labels, datasetLabel, explicitColor);
            return true;
        }
    }

    private static bool TryRenderNetworkSvg(string json, out string svg) {
        svg = string.Empty;
        JsonDocument doc;
        try {
            doc = JsonDocument.Parse(json);
        } catch {
            return false;
        }

        using (doc) {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!TryGetProperty(root, "nodes", out var nodesElement) || nodesElement.ValueKind != JsonValueKind.Array) {
                return false;
            }

            var nodes = new List<(string Id, string Label)>();
            foreach (var node in nodesElement.EnumerateArray()) {
                if (node.ValueKind != JsonValueKind.Object) {
                    return false;
                }

                var id = ReadNodeId(node);
                if (id.Length == 0 || id.Length > 80) {
                    return false;
                }

                var label = ReadStringProperty(node, "label", 80);
                if (label.Length == 0) {
                    label = id;
                }

                nodes.Add((id, label));
                if (nodes.Count > MaxNetworkNodes) {
                    return false;
                }
            }

            if (nodes.Count == 0) {
                return false;
            }

            var ids = new HashSet<string>(nodes.Select(static n => n.Id), StringComparer.Ordinal);
            if (ids.Count != nodes.Count) {
                return false;
            }

            var edges = new List<(string From, string To, string Label)>();
            if (TryGetProperty(root, "edges", out var edgesElement) && edgesElement.ValueKind == JsonValueKind.Array) {
                foreach (var edge in edgesElement.EnumerateArray()) {
                    if (edge.ValueKind != JsonValueKind.Object) {
                        return false;
                    }

                    var from = ReadNodeIdProperty(edge, "from");
                    var to = ReadNodeIdProperty(edge, "to");
                    if (from.Length == 0 || to.Length == 0 || !ids.Contains(from) || !ids.Contains(to)) {
                        return false;
                    }

                    var label = ReadStringProperty(edge, "label", 80);
                    edges.Add((from, to, label));
                    if (edges.Count > MaxNetworkEdges) {
                        return false;
                    }
                }
            }

            svg = RenderNetworkSvg(nodes, edges);
            return true;
        }
    }

    private static bool TryReadChartPoint(JsonElement element, out double value, out double radius) {
        value = 0;
        radius = 6;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) && double.IsFinite(number)) {
            value = number;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (TryGetProperty(element, "y", out var yElement) && yElement.ValueKind == JsonValueKind.Number && yElement.TryGetDouble(out var y) && double.IsFinite(y)) {
            value = y;
        } else if (TryGetProperty(element, "value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetDouble(out var v) && double.IsFinite(v)) {
            value = v;
        } else {
            return false;
        }

        if (TryGetProperty(element, "r", out var rElement) && rElement.ValueKind == JsonValueKind.Number && rElement.TryGetDouble(out var r) && double.IsFinite(r)) {
            radius = Math.Clamp(r, 3, 24);
        }

        return true;
    }

    private static string RenderCartesianChartSvg(string chartType, IReadOnlyList<double> values, IReadOnlyList<double> radii, IReadOnlyList<string> labels, string datasetLabel, string explicitColor) {
        const int width = 960;
        const int height = 480;
        const int left = 80;
        const int right = 44;
        const int top = 64;
        const int bottom = 86;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;

        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 0.0001) {
            max = min + 1;
        }
        if (min > 0) {
            min = 0;
        }
        if (max < 0) {
            max = 0;
        }

        var zeroY = top + plotHeight - (0 - min) / (max - min) * plotHeight;
        var stepX = values.Count == 1 ? plotWidth : plotWidth / (values.Count - 1.0);
        var fill = explicitColor.Length > 0 ? explicitColor : Palette[0];

        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(width).Append("' height='").Append(height).Append("' viewBox='0 0 ").Append(width).Append(' ').Append(height).Append("'>");
        sb.Append("<rect x='0' y='0' width='100%' height='100%' fill='#ffffff'/>");
        sb.Append("<rect x='").Append(left).Append("' y='").Append(top).Append("' width='").Append(plotWidth).Append("' height='").Append(plotHeight).Append("' fill='#f8fbff' stroke='#d2deec'/>");
        sb.Append("<line x1='").Append(left).Append("' y1='").Append(zeroY.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("' x2='").Append(left + plotWidth).Append("' y2='").Append(zeroY.ToString("0.##", CultureInfo.InvariantCulture))
            .Append("' stroke='#7e92a8' stroke-width='1'/>");
        sb.Append("<text x='").Append(left).Append("' y='36' fill='#1b2b3a' font-size='21' font-family='Segoe UI, Arial'>Chart Preview (")
            .Append(EscapeXml(chartType)).Append(")</text>");
        if (!string.IsNullOrWhiteSpace(datasetLabel)) {
            sb.Append("<text x='").Append(left).Append("' y='54' fill='#4e6278' font-size='12' font-family='Segoe UI, Arial'>")
                .Append(EscapeXml(datasetLabel)).Append("</text>");
        }

        if (chartType == "bar") {
            var barWidth = Math.Max(6, (plotWidth / Math.Max(values.Count, 1)) * 0.7);
            for (var i = 0; i < values.Count; i++) {
                var x = left + i * (plotWidth / Math.Max(values.Count, 1.0)) + ((plotWidth / Math.Max(values.Count, 1.0)) - barWidth) / 2.0;
                var y = top + (max - Math.Max(values[i], 0)) / (max - min) * plotHeight;
                var y2 = top + (max - Math.Min(values[i], 0)) / (max - min) * plotHeight;
                var rectY = Math.Min(y, y2);
                var rectHeight = Math.Max(1, Math.Abs(y2 - y));
                sb.Append("<rect x='").Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append("' y='")
                    .Append(rectY.ToString("0.##", CultureInfo.InvariantCulture)).Append("' width='")
                    .Append(barWidth.ToString("0.##", CultureInfo.InvariantCulture)).Append("' height='")
                    .Append(rectHeight.ToString("0.##", CultureInfo.InvariantCulture)).Append("' fill='").Append(fill).Append("' opacity='0.85'/>");
            }
        } else {
            var points = new StringBuilder();
            for (var i = 0; i < values.Count; i++) {
                var x = left + i * stepX;
                var y = top + (max - values[i]) / (max - min) * plotHeight;
                points.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
            }

            if (chartType is "line" or "radar") {
                sb.Append("<polyline fill='none' stroke='").Append(fill).Append("' stroke-width='2.4' points='")
                    .Append(points.ToString().Trim()).Append("'/>");
            }

            for (var i = 0; i < values.Count; i++) {
                var x = left + i * stepX;
                var y = top + (max - values[i]) / (max - min) * plotHeight;
                var r = chartType == "bubble"
                    ? Math.Clamp(radii[Math.Min(i, radii.Count - 1)], 3, 24)
                    : (chartType == "scatter" ? 4 : 3.2);
                sb.Append("<circle cx='").Append(x.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("' cy='").Append(y.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("' r='").Append(r.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("' fill='").Append(fill).Append("' opacity='0.88'/>");
            }
        }

        var maxLabelCount = Math.Min(labels.Count, 20);
        for (var i = 0; i < maxLabelCount; i++) {
            var x = left + i * (plotWidth / Math.Max(maxLabelCount - 1, 1.0));
            var label = labels[i];
            if (label.Length > 14) {
                label = label[..11] + "...";
            }

            sb.Append("<text x='").Append(x.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' y='").Append(height - 40).Append("' text-anchor='middle' fill='#556a80' font-size='11' font-family='Segoe UI, Arial'>")
                .Append(EscapeXml(label)).Append("</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RenderPieLikeChartSvg(string chartType, IReadOnlyList<double> values, IReadOnlyList<string> labels, string datasetLabel, string explicitColor) {
        const int width = 960;
        const int height = 500;
        const double cx = 280;
        const double cy = 275;
        const double radius = 160;
        const double innerRadius = 75;

        var totals = values.Select(static v => Math.Abs(v)).ToArray();
        var total = totals.Sum();
        if (total <= 0.0001) {
            totals = Enumerable.Repeat(1.0, values.Count).ToArray();
            total = totals.Sum();
        }

        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(width).Append("' height='").Append(height).Append("' viewBox='0 0 ").Append(width).Append(' ').Append(height).Append("'>");
        sb.Append("<rect x='0' y='0' width='100%' height='100%' fill='#ffffff'/>");
        sb.Append("<text x='56' y='42' fill='#1b2b3a' font-size='21' font-family='Segoe UI, Arial'>Chart Preview (")
            .Append(EscapeXml(chartType)).Append(")</text>");
        if (!string.IsNullOrWhiteSpace(datasetLabel)) {
            sb.Append("<text x='56' y='61' fill='#4e6278' font-size='12' font-family='Segoe UI, Arial'>")
                .Append(EscapeXml(datasetLabel)).Append("</text>");
        }

        var angle = -Math.PI / 2.0;
        for (var i = 0; i < totals.Length; i++) {
            var sweep = (totals[i] / total) * Math.PI * 2.0;
            var color = explicitColor.Length > 0 ? explicitColor : Palette[i % Palette.Length];
            sb.Append("<path d='").Append(BuildPieSlicePath(cx, cy, radius, angle, angle + sweep)).Append("' fill='").Append(color).Append("'/>");
            angle += sweep;
        }

        if (chartType == "doughnut") {
            sb.Append("<circle cx='").Append(cx.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' cy='").Append(cy.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' r='").Append(innerRadius.ToString("0.##", CultureInfo.InvariantCulture)).Append("' fill='#ffffff'/>");
        }

        var legendStartY = 112;
        for (var i = 0; i < totals.Length && i < 16; i++) {
            var y = legendStartY + i * 22;
            var color = explicitColor.Length > 0 ? explicitColor : Palette[i % Palette.Length];
            var percentage = totals[i] / total * 100.0;
            var label = labels[Math.Min(i, labels.Count - 1)];
            if (label.Length > 26) {
                label = label[..23] + "...";
            }

            sb.Append("<rect x='538' y='").Append((y - 11).ToString(CultureInfo.InvariantCulture)).Append("' width='12' height='12' fill='").Append(color).Append("'/>");
            sb.Append("<text x='560' y='").Append(y.ToString(CultureInfo.InvariantCulture)).Append("' fill='#334a62' font-size='12' font-family='Segoe UI, Arial'>")
                .Append(EscapeXml(label))
                .Append(" (")
                .Append(percentage.ToString("0.#", CultureInfo.InvariantCulture))
                .Append("%)")
                .Append("</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RenderNetworkSvg(IReadOnlyList<(string Id, string Label)> nodes, IReadOnlyList<(string From, string To, string Label)> edges) {
        const int width = 980;
        const int height = 580;
        const double centerX = width / 2.0;
        const double centerY = height / 2.0 + 16;
        var radius = Math.Min(width, height) * 0.33;

        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        if (nodes.Count == 1) {
            positions[nodes[0].Id] = (centerX, centerY);
        } else {
            for (var i = 0; i < nodes.Count; i++) {
                var angle = (Math.PI * 2.0 * i) / nodes.Count - Math.PI / 2.0;
                var x = centerX + Math.Cos(angle) * radius;
                var y = centerY + Math.Sin(angle) * radius;
                positions[nodes[i].Id] = (x, y);
            }
        }

        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(width).Append("' height='").Append(height).Append("' viewBox='0 0 ").Append(width).Append(' ').Append(height).Append("'>");
        sb.Append("<defs><marker id='arrow' markerWidth='10' markerHeight='7' refX='9' refY='3.5' orient='auto'><polygon points='0 0, 10 3.5, 0 7' fill='#6f88a1'/></marker></defs>");
        sb.Append("<rect x='0' y='0' width='100%' height='100%' fill='#f8fbff'/>");
        sb.Append("<text x='36' y='42' fill='#1b2b3a' font-size='21' font-family='Segoe UI, Arial'>Network Preview</text>");
        sb.Append("<text x='36' y='62' fill='#4e6278' font-size='12' font-family='Segoe UI, Arial'>Deterministic circular layout for ix-network export</text>");

        for (var i = 0; i < edges.Count; i++) {
            var edge = edges[i];
            var from = positions[edge.From];
            var to = positions[edge.To];
            sb.Append("<line x1='").Append(from.X.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' y1='").Append(from.Y.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' x2='").Append(to.X.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' y2='").Append(to.Y.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' stroke='#7f94ab' stroke-width='1.6' marker-end='url(#arrow)'/>");

            if (!string.IsNullOrWhiteSpace(edge.Label)) {
                var midX = (from.X + to.X) / 2.0;
                var midY = (from.Y + to.Y) / 2.0;
                var label = edge.Label.Length > 26 ? edge.Label[..23] + "..." : edge.Label;
                sb.Append("<text x='").Append(midX.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("' y='").Append((midY - 6).ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("' fill='#5a7088' font-size='11' text-anchor='middle' font-family='Segoe UI, Arial'>")
                    .Append(EscapeXml(label))
                    .Append("</text>");
            }
        }

        for (var i = 0; i < nodes.Count; i++) {
            var node = nodes[i];
            var point = positions[node.Id];
            var color = Palette[i % Palette.Length];
            sb.Append("<circle cx='").Append(point.X.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' cy='").Append(point.Y.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' r='24' fill='").Append(color).Append("' stroke='#2f4256' stroke-width='1.2'/>");
            sb.Append("<text x='").Append(point.X.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' y='").Append((point.Y + 41).ToString("0.##", CultureInfo.InvariantCulture))
                .Append("' fill='#293f57' font-size='12' text-anchor='middle' font-family='Segoe UI, Arial'>")
                .Append(EscapeXml(node.Label.Length > 22 ? node.Label[..19] + "..." : node.Label))
                .Append("</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string BuildPieSlicePath(double cx, double cy, double radius, double startAngle, double endAngle) {
        var startX = cx + radius * Math.Cos(startAngle);
        var startY = cy + radius * Math.Sin(startAngle);
        var endX = cx + radius * Math.Cos(endAngle);
        var endY = cy + radius * Math.Sin(endAngle);
        var largeArc = (endAngle - startAngle) > Math.PI ? "1" : "0";
        return "M " + cx.ToString("0.##", CultureInfo.InvariantCulture) + " " + cy.ToString("0.##", CultureInfo.InvariantCulture)
            + " L " + startX.ToString("0.##", CultureInfo.InvariantCulture) + " " + startY.ToString("0.##", CultureInfo.InvariantCulture)
            + " A " + radius.ToString("0.##", CultureInfo.InvariantCulture) + " " + radius.ToString("0.##", CultureInfo.InvariantCulture)
            + " 0 " + largeArc + " 1 " + endX.ToString("0.##", CultureInfo.InvariantCulture) + " " + endY.ToString("0.##", CultureInfo.InvariantCulture)
            + " Z";
    }

    private static string ReadChartColor(JsonElement dataset) {
        if (TryGetProperty(dataset, "backgroundColor", out var background)) {
            if (background.ValueKind == JsonValueKind.String) {
                return TrimAndCap(background.GetString(), 64);
            }

            if (background.ValueKind == JsonValueKind.Array) {
                foreach (var item in background.EnumerateArray()) {
                    if (item.ValueKind == JsonValueKind.String) {
                        var color = TrimAndCap(item.GetString(), 64);
                        if (color.Length > 0) {
                            return color;
                        }
                    }
                }
            }
        }

        if (TryGetProperty(dataset, "borderColor", out var border) && border.ValueKind == JsonValueKind.String) {
            return TrimAndCap(border.GetString(), 64);
        }

        return string.Empty;
    }

}

internal sealed class DocxVisualFenceMaterialization : IDisposable {
    private readonly string? _tempDirectory;

    internal DocxVisualFenceMaterialization(string markdown, string? tempDirectory) {
        Markdown = markdown ?? string.Empty;
        _tempDirectory = string.IsNullOrWhiteSpace(tempDirectory) ? null : tempDirectory;
    }

    public string Markdown { get; }
    public bool HasLocalImages => !string.IsNullOrWhiteSpace(_tempDirectory);
    public IReadOnlyList<string> AllowedImageDirectories => HasLocalImages ? [_tempDirectory!] : Array.Empty<string>();

    public void Dispose() {
        if (!HasLocalImages) {
            return;
        }

        try {
            if (Directory.Exists(_tempDirectory)) {
                Directory.Delete(_tempDirectory!, recursive: true);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }
}
