using System;
using System.Threading;

namespace Autodraw;

internal static class BrushCoverage
{
    public static int[] Disk(int diameter)
    {
        var radius = diameter / 2.0;
        var edge = (int)radius;
        var disk = new int[2 * edge + 1];
        for (var y = -edge; y <= edge; y++)
            disk[y + edge] = (int)Math.Sqrt(radius * radius - y * y);
        return disk;
    }

    public static byte[] SelectCenters(byte[] labels, int width, int height, byte layer, byte background,
        int[] disk, bool contour, bool simplified, CancellationToken stop)
    {
        var centers = new byte[labels.Length];
        if (disk.Length == 1)
        {
            for (var i = 0; i < labels.Length; i++)
            {
                if ((i & 16383) == 0) stop.ThrowIfCancellationRequested();
                if (labels[i] == layer) centers[i] = 1;
            }
            return centers;
        }

        var stride = width + 1;
        var ink = new int[checked(stride * height)];
        var motif = new int[ink.Length];
        var area = 0;
        foreach (var half in disk) area += 2 * half + 1;

        // Allow controlled overlap between adjacent colors and a small edge tolerance.
        // Keep the center on its own color. A dark contour gets more room to follow narrow lines.
        var minimumInkPercent = contour ? 40 : simplified ? 50 : 60;
        var minimumMotifPercent = simplified ? 64 : contour ? 90 : 97;
        var minimumInk = (area * minimumInkPercent + 99) / 100;
        var minimumMotif = (area * minimumMotifPercent + 99) / 100;
        var radius = disk.Length / 2;
        for (var y = 0; y < height; y++)
        {
            stop.ThrowIfCancellationRequested();
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var label = labels[y * width + x];
                ink[row + x + 1] = ink[row + x] + (label == layer ? 1 : 0);
                motif[row + x + 1] = motif[row + x] + (label == background ? 0 : 1);
            }
        }
        for (var y = 0; y < height; y++)
        {
            stop.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var pixel = y * width + x;
                if (labels[pixel] != layer) continue;
                var layerCoverage = 0;
                var motifCoverage = 0;
                for (var dy = Math.Max(-radius, -y); dy <= Math.Min(radius, height - y - 1); dy++)
                {
                    var half = disk[dy + radius];
                    var row = (y + dy) * stride;
                    var left = row + Math.Max(0, x - half);
                    var right = row + Math.Min(width, x + half + 1);
                    layerCoverage += ink[right] - ink[left];
                    motifCoverage += motif[right] - motif[left];
                }

                if (layerCoverage >= minimumInk && motifCoverage >= minimumMotif)
                    centers[pixel] = 1;
            }
        }
        return centers;
    }
}
