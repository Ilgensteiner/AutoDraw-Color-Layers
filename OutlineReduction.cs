using System;
using System.Threading;

namespace Autodraw;

internal enum OutlineMode
{
    Automatic,
    First,
    Last,
    OmitNarrow
}

internal static class OutlineReduction
{
    public static byte[] FillNarrowOutlines(byte[] labels, bool[] dark, int width, int height, int diameter,
        byte background, CancellationToken stop)
    {
        var radius = Math.Clamp(diameter / 3, 2, 12);
        var distances = new byte[labels.Length];
        var queue = new int[labels.Length];
        Span<int> adjacent = stackalloc int[4];
        var head = 0;
        var tail = 0;
        var result = (byte[])labels.Clone();
        for (var y = 0; y < height; y++)
        {
            stop.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                if (!dark[labels[p]]) continue;
                distances[p] = (byte)Math.Min(radius + 1,
                    Math.Min(x == 0 ? 1 : distances[p - 1] + 1, y == 0 ? 1 : distances[p - width] + 1));
            }
        }

        // Find wide dark cores before removing any edges. Expand those cores back into their own ink.
        var reach = new byte[labels.Length];
        for (var y = height - 1; y >= 0; y--)
        {
            stop.ThrowIfCancellationRequested();
            for (var x = width - 1; x >= 0; x--)
            {
                var p = y * width + x;
                if (!dark[labels[p]]) continue;
                var d = Math.Min(distances[p], Math.Min(x + 1 == width ? 1 : distances[p + 1] + 1,
                    y + 1 == height ? 1 : distances[p + width] + 1));
                distances[p] = (byte)d;
                if (d > radius)
                {
                    reach[p] = (byte)(radius + 1);
                    queue[tail++] = p;
                }
            }
        }

        while (head < tail)
        {
            if ((head & 8191) == 0) stop.ThrowIfCancellationRequested();
            var p = queue[head++];
            if (reach[p] <= 1) continue;
            var next = (byte)(reach[p] - 1);
            var count = Neighbors(p, adjacent);
            for (var i = 0; i < count; i++)
            {
                var q = adjacent[i];
                if (!dark[labels[q]] || reach[q] != 0) continue;
                reach[q] = next;
                queue[tail++] = q;
            }
        }

        // Replace narrow dark strips with the nearest surrounding permitted ink, never across paper.
        // An isolated dark ornament with no neighbor may disappear in this mode.
        head = tail = 0;
        var maxFill = Math.Max(3, diameter / 2 + 1);
        for (var p = 0; p < labels.Length; p++)
        {
            if ((p & 8191) == 0) stop.ThrowIfCancellationRequested();
            if (!dark[labels[p]] || reach[p] != 0) continue;
            var ink = background;
            var count = Neighbors(p, adjacent);
            for (var i = 0; i < count; i++)
            {
                var q = adjacent[i];
                if (labels[q] != background && !dark[labels[q]])
                {
                    ink = labels[q];
                    break;
                }
            }
            result[p] = ink;
            distances[p] = 0;
            if (ink == background) continue;
            distances[p] = 1;
            queue[tail++] = p;
        }

        while (head < tail)
        {
            if ((head & 8191) == 0) stop.ThrowIfCancellationRequested();
            var p = queue[head++];
            if (distances[p] >= maxFill) continue;
            var count = Neighbors(p, adjacent);
            for (var i = 0; i < count; i++)
            {
                var q = adjacent[i];
                if (!dark[labels[q]] || reach[q] != 0 || distances[q] != 0) continue;
                result[q] = result[p];
                distances[q] = (byte)(distances[p] + 1);
                queue[tail++] = q;
            }
        }
        return result;

        int Neighbors(int p, Span<int> found)
        {
            var count = 0;
            var x = p % width;
            if (x > 0) found[count++] = p - 1;
            if (x + 1 < width) found[count++] = p + 1;
            if (p >= width) found[count++] = p - width;
            if (p + width < labels.Length) found[count++] = p + width;
            return count;
        }
    }
}
