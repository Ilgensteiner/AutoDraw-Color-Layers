using System;
using System.Collections.Generic;
using System.Threading;

namespace Autodraw;

internal sealed class StrokePlan
{
    internal readonly record struct Run(int Y, int Left, int Right);
    internal readonly record struct Move(int X, int Y, bool StartsStroke = false);

    private const byte Fresh = 1;
    private const byte Painted = 2;
    private const byte Revisited = 3;
    private const int BridgeLimit = 24;
    private const int BridgeSide = 2 * BridgeLimit + 1;
    private static readonly (int X, int Y)[] Directions =
    {
        (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)
    };

    private readonly List<Run> _runs = new();
    private readonly int[] _rowStart;
    private readonly List<int> _route;
    public int Width { get; }
    public int Height { get; }
    public bool IsEmpty => _runs.Count == 0;

    public StrokePlan(byte[] centers, int width, int height, CancellationToken stop)
    {
        Width = width;
        Height = height;
        _rowStart = new int[height + 1];
        for (var y = 0; y < height; y++)
        {
            stop.ThrowIfCancellationRequested();
            _rowStart[y] = _runs.Count;
            var x = 0;
            while (x < width)
            {
                if (centers[y * width + x] == 0)
                {
                    x++;
                    continue;
                }

                var left = x++;
                while (x < width && centers[y * width + x] != 0) x++;
                _runs.Add(new Run(y, left, x - 1));
            }
        }
        _rowStart[height] = _runs.Count;
        _route = BuildRoute(centers, stop);
    }

    public IEnumerable<Move> Trace(CancellationToken stop)
    {
        foreach (var position in _route)
        {
            stop.ThrowIfCancellationRequested();
            var start = position < 0;
            var pixel = start ? ~position : position;
            yield return new Move(pixel % Width, pixel / Width, start);
        }
    }

    private List<int> BuildRoute(byte[] centers, CancellationToken stop)
    {
        var route = new List<int>();
        if (IsEmpty) return route;
        var state = (byte[])centers.Clone();
        var parents = new int[BridgeSide * BridgeSide];
        var queue = new int[parents.Length];
        Span<int> bridge = stackalloc int[BridgeLimit];

        foreach (var run in _runs)
        {
            for (var x = run.Left; x <= run.Right; x++)
            {
                var current = run.Y * Width + x;
                if (state[current] != Fresh) continue;
                stop.ThrowIfCancellationRequested();
                route.Add(~current);
                state[current] = Painted;
                var previous = current;

                while (true)
                {
                    stop.ThrowIfCancellationRequested();
                    var next = NextFresh(state, current, previous);
                    if (next < 0)
                    {
                        next = FindBridge(state, current, parents, queue, bridge, out var steps, stop);
                        if (next < 0) break;
                        // The search returns the bridge in reverse order.
                        while (steps > 0)
                        {
                            current = bridge[--steps];
                            state[current] = Revisited;
                            route.Add(current);
                        }
                    }
                    previous = current;
                    current = next;
                    state[current] = Painted;
                    route.Add(current);
                }
            }
        }
        return route;
    }

    private int NextFresh(byte[] state, int current, int previous)
    {
        var x = current % Width;
        var y = current / Width;
        var headingX = x - previous % Width;
        var headingY = y - previous / Width;
        var best = -1;
        var bestScore = int.MaxValue;
        foreach (var (dx, dy) in Directions)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (!InBounds(nx, ny)) continue;
            var pixel = ny * Width + nx;
            if (state[pixel] != Fresh) continue;

            // Take the neighbors most likely to become stranded. Prefer a straight line on ties.
            var degree = 0;
            foreach (var (ox, oy) in Directions)
                if (InBounds(nx + ox, ny + oy) && state[(ny + oy) * Width + nx + ox] == Fresh)
                    degree++;
            var turn = Math.Abs(headingX - dx) + Math.Abs(headingY - dy);
            var score = 5 * degree + turn;
            if (score >= bestScore) continue;
            best = pixel;
            bestScore = score;
        }
        return best;
    }

    private int FindBridge(byte[] state, int current, int[] parents, int[] queue, Span<int> bridge,
        out int steps, CancellationToken stop)
    {
        steps = 0;
        Array.Fill(parents, -1);
        const int start = BridgeLimit * BridgeSide + BridgeLimit;
        var left = current % Width - BridgeLimit;
        var top = current / Width - BridgeLimit;
        var head = 0;
        var tail = 1;
        parents[start] = start;
        queue[0] = start;
        for (var distance = 1; distance <= BridgeLimit && head < tail; distance++)
        {
            stop.ThrowIfCancellationRequested();
            var end = tail;
            while (head < end)
            {
                var node = queue[head++];
                var x = left + node % BridgeSide;
                var y = top + node / BridgeSide;
                foreach (var (dx, dy) in Directions)
                {
                    if (!InBounds(x + dx, y + dy)) continue;
                    var pixel = (y + dy) * Width + x + dx;
                    if (state[pixel] == Fresh)
                    {
                        for (var p = node; p != start; p = parents[p])
                            bridge[steps++] = (top + p / BridgeSide) * Width + left + p % BridgeSide;
                        return pixel;
                    }
                    if (distance == BridgeLimit || state[pixel] != Painted) continue;
                    var next = node + dy * BridgeSide + dx;
                    if (parents[next] >= 0) continue;
                    parents[next] = node;
                    queue[tail++] = next;
                }
            }
        }
        return -1;
    }

    private bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public void Stamp(uint[] pixels, int[] brush, uint color, CancellationToken stop)
    {
        var radius = brush.Length / 2;
        var range = new int[Width + 1];
        for (var y = 0; y < Height; y++)
        {
            stop.ThrowIfCancellationRequested();
            Array.Clear(range);
            for (var dy = -radius; dy <= radius; dy++)
            {
                var row = y + dy;
                if ((uint)row >= (uint)Height) continue;
                var half = brush[dy + radius];
                for (var i = _rowStart[row]; i < _rowStart[row + 1]; i++)
                {
                    var run = _runs[i];
                    range[Math.Max(0, run.Left - half)]++;
                    range[Math.Min(Width, run.Right + half + 1)]--;
                }
            }

            var coverage = 0;
            for (var x = 0; x < Width; x++)
            {
                coverage += range[x];
                if (coverage > 0) pixels[y * Width + x] = color;
            }
        }
    }
}
