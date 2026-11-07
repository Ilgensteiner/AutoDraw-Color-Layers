using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace Autodraw;

/// <summary>
/// Splits an (already resized) image into a small set of flat colour layers.
///
/// Pipeline:
///   1. Histogram pass: every visible pixel is put into a 5-bit-per-channel RGB bin (32768 bins),
///      accumulating count and colour sums. Transparent / near-white pixels are skipped.
///   2. Weighted median cut over the occupied bins produces up to maxColors initial clusters.
///   3. A few rounds of weighted k-means (Lloyd) on the bins refine the cluster centres.
///   4. Clusters are rounded to colours, kept out of the reserved white range, de-duplicated and
///      ordered by pixel count (largest first, ties broken by packed colour value).
///   5. Label pass: each pixel gets a single byte label (palette index, or NoLayer).
///
/// Only the byte label buffer (width * height bytes) is kept. Preview / mask bitmaps are allocated
/// on demand for preview and drawing; the caller owns and must dispose the bitmaps.
///
/// Output bitmaps are Bgra8888 / Premul. Pixels outside the image or mask have zero alpha.
/// The instance is immutable after Create and safe to read from multiple threads.
/// </summary>
internal sealed class ColorLayers
{
    public const int MinColors = 1;
    public const int MaxColors = 16;

    // Pixels with alpha below this are treated as transparent.
    private const byte AlphaCutoff = 128;

    // Pixels with R, G and B all at or above this are treated as (reserved) white / paper.
    private const byte WhiteCutoff = 245;

    // Label value for pixels that belong to no layer. Palette indices are always < MaxColors.
    private const byte NoLayer = 255;

    private const int BinShift = 3; // 8 bits -> 5 bits per channel
    private const int BinCount = 1 << 15;
    private const int KMeansIterations = 8;

    // Channel weights for distances / variance (cheap perceptual approximation).
    private const double WeightR = 2.0;
    private const double WeightG = 4.0;
    private const double WeightB = 3.0;

    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _labels;
    private readonly SKColor[] _palette;

    private ColorLayers(int width, int height, byte[] labels, SKColor[] palette)
    {
        _width = width;
        _height = height;
        _labels = labels;
        _palette = palette;
        // Expose a copy so external mutation of the array cannot desync preview / masks.
        Palette = (SKColor[])palette.Clone();
    }

    /// <summary>
    /// Layer colours, largest layer first. Never contains a colour with R, G and B all &gt;= 245.
    /// Empty when the image has no visible (non-transparent, non-white) pixels.
    /// </summary>
    public SKColor[] Palette { get; }

    public ColorLayers Resize(int width, int height)
    {
        if (width == _width && height == _height)
            return this;

        var labels = new byte[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = (int)((2L * y + 1) * _height / (2L * height)) * _width;
            for (var x = 0; x < width; x++)
            {
                var sourceX = (int)((2L * x + 1) * _width / (2L * width));
                labels[y * width + x] = _labels[sourceRow + sourceX];
            }
        }

        return Compact(width, height, labels);
    }

    private ColorLayers Compact(int width, int height, byte[] labels)
    {
        var used = new bool[_palette.Length];
        foreach (var label in labels)
            if (label != NoLayer)
                used[label] = true;
        var palette = new List<SKColor>(_palette.Length);
        var indices = new byte[256];
        indices[NoLayer] = NoLayer;
        for (var i = 0; i < _palette.Length; i++)
        {
            if (!used[i])
                continue;
            indices[i] = (byte)palette.Count;
            palette.Add(_palette[i]);
        }

        if (palette.Count != _palette.Length)
            for (var i = 0; i < labels.Length; i++)
                labels[i] = indices[labels[i]];
        return new ColorLayers(width, height, labels, palette.ToArray());
    }

    public static unsafe SKBitmap RemoveWhiteBackground(SKBitmap source)
    {
        var foreground = new SKBitmap(new SKImageInfo(source.Width, source.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        var baseAddress = foreground.GetPixels();
        try
        {
            ForEachRow(source, (y, row) =>
            {
                var output = (uint*)((byte*)baseAddress.ToPointer() + (long)y * foreground.RowBytes);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    var b = (byte)pixel;
                    var g = (byte)(pixel >> 8);
                    var r = (byte)(pixel >> 16);
                    var a = (byte)(pixel >> 24);
                    output[x] = IsTransparent(r, g, b, a) ? 0 :
                        ((uint)a << 24) | ((uint)((r * a + 127) / 255) << 16) |
                        ((uint)((g * a + 127) / 255) << 8) | (uint)((b * a + 127) / 255);
                }
            });
            return foreground;
        }
        catch
        {
            foreground.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Quantizes <paramref name="source"/> into at most <paramref name="maxColors"/> layers.
    /// White / transparent pixels do not count towards the budget.
    /// The source is only read; it must not be modified concurrently while this runs.
    /// </summary>
    public static ColorLayers Create(SKBitmap source, int maxColors, SKColor[]? allowed = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (maxColors < MinColors || maxColors > MaxColors)
            throw new ArgumentOutOfRangeException(nameof(maxColors), maxColors,
                $"maxColors must be between {MinColors} and {MaxColors}.");

        var width = source.Width;
        var height = source.Height;
        var pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue)
            throw new ArgumentException("Source bitmap is too large.", nameof(source));

        var labels = new byte[pixelCount];
        if (pixelCount == 0)
            return new ColorLayers(width, height, labels, Array.Empty<SKColor>());

        // ---- 1. Histogram pass ------------------------------------------------------------
        var binCounts = new int[BinCount];
        var binSumR = new long[BinCount];
        var binSumG = new long[BinCount];
        var binSumB = new long[BinCount];

        ForEachRow(source, (_, row) =>
        {
            for (var x = 0; x < row.Length; x++)
            {
                var p = row[x];
                var b = (byte)p;
                var g = (byte)(p >> 8);
                var r = (byte)(p >> 16);
                var a = (byte)(p >> 24);
                if (IsTransparent(r, g, b, a)) continue;

                var bin = BinOf(r, g, b);
                binCounts[bin]++;
                binSumR[bin] += r;
                binSumG[bin] += g;
                binSumB[bin] += b;
            }
        });

        // Occupied bins in ascending bin order (deterministic starting order).
        var entryList = new List<Entry>();
        for (var bin = 0; bin < BinCount; bin++)
        {
            var count = binCounts[bin];
            if (count == 0) continue;
            entryList.Add(new Entry
            {
                Bin = bin,
                R = (double)binSumR[bin] / count,
                G = (double)binSumG[bin] / count,
                B = (double)binSumB[bin] / count,
                Weight = count
            });
        }

        if (entryList.Count == 0)
        {
            // Nothing visible: everything is transparent.
            Array.Fill(labels, NoLayer);
            return new ColorLayers(width, height, labels, Array.Empty<SKColor>());
        }

        var entries = entryList.ToArray();

        // ---- 2. Weighted median cut -------------------------------------------------------
        var boxes = MedianCut(entries, maxColors);

        // ---- 3. Weighted k-means refinement -----------------------------------------------
        var k = boxes.Count;
        var cR = new double[k];
        var cG = new double[k];
        var cB = new double[k];
        var alive = new bool[k];
        for (var c = 0; c < k; c++)
        {
            MeanOf(entries, boxes[c].Start, boxes[c].End, out cR[c], out cG[c], out cB[c]);
            alive[c] = true;
        }

        var assignment = new int[entries.Length];
        Array.Fill(assignment, -1);
        var sumW = new long[k];
        var sumR = new double[k];
        var sumG = new double[k];
        var sumB = new double[k];

        for (var iter = 0; iter < KMeansIterations; iter++)
        {
            var changed = false;
            for (var i = 0; i < entries.Length; i++)
            {
                var nearest = Nearest(entries[i], cR, cG, cB, alive);
                if (assignment[i] != nearest)
                {
                    assignment[i] = nearest;
                    changed = true;
                }
            }

            if (!changed) break;

            AccumulateClusters(entries, assignment, sumW, sumR, sumG, sumB);
            for (var c = 0; c < k; c++)
            {
                if (sumW[c] == 0)
                {
                    alive[c] = false; // empty cluster: drop it permanently
                    continue;
                }

                cR[c] = sumR[c] / sumW[c];
                cG[c] = sumG[c] / sumW[c];
                cB[c] = sumB[c] / sumW[c];
            }
        }

        // Final cluster stats strictly from the final assignment.
        AccumulateClusters(entries, assignment, sumW, sumR, sumG, sumB);

        // ---- 4. Build palette: round, avoid white, merge duplicates, order ----------------
        var clusterToMerged = new int[k];
        var mergedColors = new List<SKColor>();
        var mergedWeights = new List<long>();
        var colorIndex = new Dictionary<SKColor, int>();

        for (var c = 0; c < k; c++)
        {
            if (sumW[c] == 0)
            {
                clusterToMerged[c] = -1; // ignored empty cluster
                continue;
            }

            var color = ToPaletteColor(sumR[c] / sumW[c], sumG[c] / sumW[c], sumB[c] / sumW[c]);
            if (colorIndex.TryGetValue(color, out var existing))
            {
                mergedWeights[existing] += sumW[c];
                clusterToMerged[c] = existing;
            }
            else
            {
                var idx = mergedColors.Count;
                colorIndex.Add(color, idx);
                mergedColors.Add(color);
                mergedWeights.Add(sumW[c]);
                clusterToMerged[c] = idx;
            }
        }

        var order = new int[mergedColors.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        // Neutrals first, then the largest layers. The packed colour breaks ties.
        Array.Sort(order, (a, b) =>
        {
            var cmp = PaletteColor.Group(mergedColors[a]).CompareTo(PaletteColor.Group(mergedColors[b]));
            if (cmp == 0)
                cmp = mergedWeights[b].CompareTo(mergedWeights[a]);
            return cmp != 0 ? cmp : ((uint)mergedColors[a]).CompareTo((uint)mergedColors[b]);
        });

        var rank = new int[order.Length];
        var palette = new SKColor[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            rank[order[i]] = i;
            palette[i] = mergedColors[order[i]];
        }

        var binToLabel = new byte[BinCount];
        Array.Fill(binToLabel, NoLayer);
        for (var i = 0; i < entries.Length; i++)
            binToLabel[entries[i].Bin] = (byte)rank[clusterToMerged[assignment[i]]];

        // ---- 5. Label pass ----------------------------------------------------------------
        ForEachRow(source, (y, row) =>
        {
            var offset = y * width;
            for (var x = 0; x < row.Length; x++)
            {
                var p = row[x];
                var b = (byte)p;
                var g = (byte)(p >> 8);
                var r = (byte)(p >> 16);
                var a = (byte)(p >> 24);
                labels[offset + x] = IsTransparent(r, g, b, a) ? NoLayer : binToLabel[BinOf(r, g, b)];
            }
        });

        if (allowed is null)
            return new ColorLayers(width, height, labels, palette);

        (labels, palette) = DrawingPalette.Map(labels, palette, allowed, NoLayer, maxColors);
        var layers = new ColorLayers(width, height, labels, palette);
        return layers.RemoveEnclosedColorSpecks();
    }

    private ColorLayers RemoveEnclosedColorSpecks()
    {
        if (_palette.Length < 2) return this;
        var chromatic = new bool[256];
        for (var i = 0; i < _palette.Length; i++) chromatic[i] = PaletteColor.Group(_palette[i]) == 2;
        byte[]? revised = null;
        for (var y = 1; y < _height - 1; y++)
        {
            for (var x = 1; x < _width - 1; x++)
            {
                var p = y * _width + x;
                var original = _labels[p];
                var neighbor = _labels[p - 1];
                if (original == neighbor || !chromatic[original] || !chromatic[neighbor] ||
                    _labels[p + 1] != neighbor || _labels[p - _width] != neighbor ||
                    _labels[p + _width] != neighbor)
                    continue;
                (revised ??= (byte[])_labels.Clone())[p] = neighbor;
            }
        }
        return revised is null ? this : Compact(_width, _height, revised);
    }

    /// <summary>
    /// Full-colour preview: each visible pixel in its palette colour (opaque),
    /// everything else transparent. Same dimensions as the source.
    /// Caller owns the returned bitmap.
    /// </summary>
    public SKBitmap CreatePreview()
    {
        var lut = new uint[256];
        for (var i = 0; i < _palette.Length; i++)
        {
            var c = _palette[i];
            lut[i] = 0xFF000000u | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue;
        }

        return Render(lut);
    }

    internal sealed record Pass(SKColor Ink, int Diameter, bool IsDark, bool UsesOutlineBrush, StrokePlan Path);

    public Pass[] PreparePasses(int brush, int contourBrush, OutlineMode mode, CancellationToken stop)
    {
        if (mode == OutlineMode.Automatic)
        {
            if (contourBrush > 0 && contourBrush < brush)
                mode = OutlineMode.Last;
            else if (brush >= 16 || (brush >= 8 && brush * 14 >= Math.Min(_width, _height)))
                mode = OutlineMode.OmitNarrow;
            else
                mode = brush >= 5 ? OutlineMode.First : OutlineMode.Last;
        }

        var effective = this;
        if (mode == OutlineMode.OmitNarrow && _palette.Any(color => PaletteColor.Group(color) != 0)
            && _palette.Any(color => PaletteColor.Group(color) == 0))
        {
            var dark = new bool[256];
            for (var i = 0; i < _palette.Length; i++)
                dark[i] = IsContour(i);
            effective = Compact(_width, _height, OutlineReduction.FillNarrowOutlines(
                _labels, dark, _width, _height, brush, NoLayer, stop));
        }

        var order = Enumerable.Range(0, effective._palette.Length);
        order = mode == OutlineMode.First
            ? order.OrderBy(index => !effective.IsContour(index))
            : order.OrderBy(index => effective.IsContour(index));
        var passes = new List<Pass>();
        foreach (var index in order)
        {
            stop.ThrowIfCancellationRequested();
            var isDark = effective.IsContour(index);
            var separate = mode != OutlineMode.OmitNarrow && contourBrush > 0 && isDark;
            var diameter = separate ? contourBrush : brush;
            var path = effective.PlanPass(index, diameter, mode == OutlineMode.OmitNarrow, stop);
            if (!path.IsEmpty)
                passes.Add(new Pass(effective._palette[index], diameter, isDark, separate, path));
        }
        return passes.ToArray();
    }

    public bool IsContour(int index) => PaletteColor.Group(_palette[index]) == 0;

    private StrokePlan PlanPass(int index, int diameter, bool simplified, CancellationToken stop)
    {
        var disk = BrushCoverage.Disk(diameter);
        var centers = BrushCoverage.SelectCenters(_labels, _width, _height,
            (byte)index, NoLayer, disk, IsContour(index), simplified, stop);
        return new StrokePlan(centers, _width, _height, stop);
    }

    public unsafe SKBitmap CreateProjectedPreview(Pass[] passes, CancellationToken stop)
    {
        var pixels = new uint[_labels.Length];
        foreach (var pass in passes)
        {
            stop.ThrowIfCancellationRequested();
            pass.Path.Stamp(pixels, BrushCoverage.Disk(pass.Diameter), (uint)pass.Ink, stop);
        }
        var result = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul));
        fixed (uint* source = pixels)
        {
            var destination = (byte*)result.GetPixels().ToPointer();
            for (var y = 0; y < _height; y++)
                Buffer.MemoryCopy(source + y * _width, destination + (long)y * result.RowBytes,
                    result.RowBytes, _width * sizeof(uint));
        }
        return result;
    }

    // ===================================================================================
    // Helpers
    // ===================================================================================

    private struct Entry
    {
        public int Bin;
        public double R;
        public double G;
        public double B;
        public long Weight;
    }

    private sealed class Box
    {
        public int Start;
        public int End;
        public double Score; // weighted squared error; 0 => not worth splitting
        public int Axis;     // 0 = R, 1 = G, 2 = B (channel with largest weighted error)
    }

    private sealed class AxisComparer : IComparer<Entry>
    {
        public static readonly AxisComparer[] ByAxis = { new(0), new(1), new(2) };
        private readonly int _axis;

        private AxisComparer(int axis) => _axis = axis;

        public int Compare(Entry x, Entry y)
        {
            var cmp = _axis switch
            {
                0 => x.R.CompareTo(y.R),
                1 => x.G.CompareTo(y.G),
                _ => x.B.CompareTo(y.B)
            };
            return cmp != 0 ? cmp : x.Bin.CompareTo(y.Bin); // bins are unique => total order
        }
    }

    private static bool IsTransparent(byte r, byte g, byte b, byte a)
    {
        return a < AlphaCutoff || DrawingPalette.IsPaper(r, g, b);
    }

    private static int BinOf(byte r, byte g, byte b)
    {
        return ((r >> BinShift) << 10) | ((g >> BinShift) << 5) | (b >> BinShift);
    }

    /// <summary>
    /// Reads the source row by row as Bgra8888 / Unpremul (Skia converts other formats and
    /// premultiplied alpha), so no full-size copy of the source is needed.
    /// </summary>
    private static unsafe void ForEachRow(SKBitmap source, Action<int, uint[]> rowAction)
    {
        var width = source.Width;
        var height = source.Height;
        var rowInfo = new SKImageInfo(width, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var row = new uint[width];

        using var pixmap = source.PeekPixels()
                           ?? throw new ArgumentException("Source bitmap has no readable raster pixels.",
                               nameof(source));

        fixed (uint* rowPtr = row)
        {
            for (var y = 0; y < height; y++)
            {
                if (!pixmap.ReadPixels(rowInfo, (IntPtr)rowPtr, width * 4, 0, y))
                    throw new InvalidOperationException(
                        $"Could not read pixels from source bitmap ({source.ColorType}).");
                rowAction(y, row);
            }
        }
    }

    private static List<Box> MedianCut(Entry[] entries, int maxBoxes)
    {
        var boxes = new List<Box> { MakeBox(entries, 0, entries.Length) };

        while (boxes.Count < maxBoxes)
        {
            // Split the box with the largest weighted error; first box wins ties.
            var best = -1;
            var bestScore = 0.0;
            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.End - box.Start < 2) continue;
                if (box.Score > bestScore)
                {
                    bestScore = box.Score;
                    best = i;
                }
            }

            if (best < 0) break; // nothing left to split (fewer distinct colours than budget)

            var target = boxes[best];
            Array.Sort(entries, target.Start, target.End - target.Start, AxisComparer.ByAxis[target.Axis]);

            long total = 0;
            for (var i = target.Start; i < target.End; i++) total += entries[i].Weight;

            // Weighted median split; both halves are guaranteed non-empty.
            var half = (total + 1) / 2;
            long cumulative = 0;
            var split = target.End - 1;
            for (var i = target.Start; i < target.End; i++)
            {
                cumulative += entries[i].Weight;
                if (cumulative >= half)
                {
                    split = i + 1;
                    break;
                }
            }

            split = Math.Clamp(split, target.Start + 1, target.End - 1);

            boxes[best] = MakeBox(entries, target.Start, split);
            boxes.Add(MakeBox(entries, split, target.End));
        }

        return boxes;
    }

    private static Box MakeBox(Entry[] entries, int start, int end)
    {
        double w = 0, sR = 0, sG = 0, sB = 0, sRR = 0, sGG = 0, sBB = 0;
        for (var i = start; i < end; i++)
        {
            var e = entries[i];
            double ew = e.Weight;
            w += ew;
            sR += e.R * ew;
            sG += e.G * ew;
            sB += e.B * ew;
            sRR += e.R * e.R * ew;
            sGG += e.G * e.G * ew;
            sBB += e.B * e.B * ew;
        }

        // Weighted sum of squared deviations per channel (clamped against rounding noise).
        var errR = Math.Max(0, sRR - sR * sR / w) * WeightR;
        var errG = Math.Max(0, sGG - sG * sG / w) * WeightG;
        var errB = Math.Max(0, sBB - sB * sB / w) * WeightB;

        var axis = 0;
        var maxErr = errR;
        if (errG > maxErr) { axis = 1; maxErr = errG; }
        if (errB > maxErr) axis = 2;

        return new Box { Start = start, End = end, Score = errR + errG + errB, Axis = axis };
    }

    private static void MeanOf(Entry[] entries, int start, int end, out double r, out double g, out double b)
    {
        double w = 0, sR = 0, sG = 0, sB = 0;
        for (var i = start; i < end; i++)
        {
            var e = entries[i];
            w += e.Weight;
            sR += e.R * e.Weight;
            sG += e.G * e.Weight;
            sB += e.B * e.Weight;
        }

        r = sR / w;
        g = sG / w;
        b = sB / w;
    }

    private static int Nearest(in Entry e, double[] cR, double[] cG, double[] cB, bool[] alive)
    {
        var best = -1;
        var bestDist = double.MaxValue;
        for (var c = 0; c < cR.Length; c++)
        {
            if (!alive[c]) continue;
            var dr = e.R - cR[c];
            var dg = e.G - cG[c];
            var db = e.B - cB[c];
            var d = WeightR * dr * dr + WeightG * dg * dg + WeightB * db * db;
            if (d < bestDist) // strict: lowest index wins ties
            {
                bestDist = d;
                best = c;
            }
        }

        return best;
    }

    private static void AccumulateClusters(Entry[] entries, int[] assignment,
        long[] sumW, double[] sumR, double[] sumG, double[] sumB)
    {
        Array.Clear(sumW);
        Array.Clear(sumR);
        Array.Clear(sumG);
        Array.Clear(sumB);
        for (var i = 0; i < entries.Length; i++)
        {
            var c = assignment[i];
            var e = entries[i];
            sumW[c] += e.Weight;
            sumR[c] += e.R * e.Weight;
            sumG[c] += e.G * e.Weight;
            sumB[c] += e.B * e.Weight;
        }
    }

    private static SKColor ToPaletteColor(double r, double g, double b)
    {
        var rb = ToByte(r);
        var gb = ToByte(g);
        var bb = ToByte(b);

        // A cluster mean of non-white pixels can still land in the reserved white range.
        // Pull its darkest channel (R, G, B order on ties) just below the cutoff.
        if (rb >= WhiteCutoff && gb >= WhiteCutoff && bb >= WhiteCutoff)
        {
            const byte below = WhiteCutoff - 1;
            if (rb <= gb && rb <= bb) rb = below;
            else if (gb <= bb) gb = below;
            else bb = below;
        }

        return new SKColor(rb, gb, bb, 255);
    }

    private static byte ToByte(double v)
    {
        return (byte)Math.Clamp((int)Math.Round(v, MidpointRounding.AwayFromZero), 0, 255);
    }

    private unsafe SKBitmap Render(uint[] lut)
    {
        if (_width == 0 || _height == 0) return new SKBitmap();

        var bitmap = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var basePtr = (byte*)bitmap.GetPixels().ToPointer();
        var rowBytes = bitmap.RowBytes;

        fixed (byte* labelPtr = _labels)
        fixed (uint* lutPtr = lut)
        {
            for (var y = 0; y < _height; y++)
            {
                var dst = (uint*)(basePtr + (long)y * rowBytes);
                var src = labelPtr + (long)y * _width;
                for (var x = 0; x < _width; x++)
                    dst[x] = lutPtr[src[x]];
            }
        }

        return bitmap;
    }
}
