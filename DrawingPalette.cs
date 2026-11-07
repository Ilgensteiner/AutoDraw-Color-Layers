using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Autodraw;

/// <summary>
/// Restricts already quantized colour layers (see <see cref="ColorLayers"/>) to the colours the
/// user actually has available in the target program.
///
/// Mapping works in OKLab (perceptual). Every source layer goes to its nearest allowed colour, so
/// similar source layers collapse onto the same allowed colour. A small penalty keeps neutral
/// (grey / black) sources on neutral targets and chromatic sources on chromatic targets when a
/// sensible alternative exists. Output colours are always taken verbatim from the allowed list.
/// </summary>
internal static class DrawingPalette
{
    // OKLab chroma below this counts as neutral (black / grey).
    private const double NeutralChroma = 0.035;

    // Added to the squared OKLab distance when source and target differ in neutrality
    // (~0.077 OKLab units, roughly a clearly visible but not huge colour difference).
    private const double NeutralMismatchPenalty = 0.006;

    // Distances closer than this are treated as equal; the earlier allowed colour wins.
    private const double TieEpsilon = 1e-12;

    private static readonly SKColor[] StandardColors =
    {
        new(0x00, 0x00, 0x00), // Black
        new(0x50, 0x50, 0x50), // Dark gray
        new(0xA0, 0xA0, 0xA0), // Gray
        new(0x8B, 0x4A, 0x1E), // Brown
        new(0x8C, 0x14, 0x20), // Dark red
        new(0xE0, 0x20, 0x1B), // Red
        new(0xFF, 0x8C, 0x1A), // Orange
        new(0xFF, 0xD7, 0x1A), // Yellow
        new(0xF1, 0xC7, 0x9E), // Beige / skin
        new(0x82, 0xC8, 0x3C), // Light green
        new(0x1E, 0x7A, 0x32), // Dark green
        new(0x00, 0xA5, 0xA5), // Turquoise
        new(0x5A, 0xB4, 0xF0), // Light blue
        new(0x1E, 0x46, 0xC8), // Blue
        new(0x78, 0x32, 0xB4), // Purple
        new(0xFF, 0x6E, 0xB4), // Pink
    };

    /// <summary>
    /// 16 common, opaque, clearly distinct drawing colours. No white / near-white (paper is not drawn).
    /// Returns a fresh copy on every access.
    /// </summary>
    public static SKColor[] Standard => (SKColor[])StandardColors.Clone();

    /// <summary>
    /// True for colours that should be treated as paper rather than ink: pure / near white, pale
    /// tinted JPEG highlights (e.g. very light pink or cream) and very light neutral paper shading.
    /// Saturated light inks (light pink, pastel yellow, beige, light grey) are not paper.
    /// </summary>
    public static bool IsPaper(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        var spread = max - min;

        if (min >= 245) return true;                 // same reserved range as ColorLayers
        if (min >= 232 && spread <= 24) return true; // pale tinted highlight, e.g. (255,232,236)
        return min >= 222 && spread <= 10;           // light, nearly neutral paper shading
    }

    /// <summary>
    /// Maps quantized <paramref name="labels"/> (indices into <paramref name="original"/>, or
    /// <paramref name="transparentLabel"/>) onto colours from <paramref name="allowed"/>.
    ///
    /// Guarantees:
    ///  * transparent pixels (and labels without a valid original colour) stay <paramref name="transparentLabel"/>;
    ///  * every output colour is an element of <paramref name="allowed"/> (duplicates removed);
    ///  * at most <paramref name="limit"/> colours (and never an index equal to the sentinel);
    ///  * no empty layers;
    ///  * deterministic order: black/contour group, greys, colours (<see cref="PaletteColor.Group"/>),
    ///    then larger layers first, then packed colour value, then position in <paramref name="allowed"/>.
    /// If <paramref name="allowed"/> is empty or nothing is visible, all pixels become transparent
    /// and the palette is empty.
    /// </summary>
    public static (byte[] Labels, SKColor[] Palette) Map(byte[] labels, SKColor[] original, SKColor[] allowed,
        byte transparentLabel, int limit)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(allowed);
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be at least 1.");

        // Output indices 0..capacity-1 must never collide with the transparent sentinel.
        var capacity = Math.Min(limit, (int)transparentLabel);
        var result = new byte[labels.Length];

        // ---- Pixel count per source label -------------------------------------------------
        var counts = new long[256];
        foreach (var label in labels)
            counts[label]++;

        var sources = new List<int>();
        var sourceLimit = Math.Min(original.Length, 256);
        for (var i = 0; i < sourceLimit; i++)
            if (i != transparentLabel && counts[i] > 0)
                sources.Add(i);

        // ---- Distinct allowed colours (first occurrence wins, by RGB) ---------------------
        var candidates = new List<int>();
        var seen = new HashSet<uint>();
        for (var i = 0; i < allowed.Length; i++)
        {
            var c = allowed[i];
            if (seen.Add(((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue))
                candidates.Add(i);
        }

        if (sources.Count == 0 || candidates.Count == 0 || capacity == 0)
        {
            Array.Fill(result, transparentLabel);
            return (result, Array.Empty<SKColor>());
        }

        var candidateLab = new Lab[candidates.Count];
        for (var c = 0; c < candidates.Count; c++)
            candidateLab[c] = Lab.From(allowed[candidates[c]]);

        var sourceLab = new Lab[sources.Count];
        for (var s = 0; s < sources.Count; s++)
            sourceLab[s] = Lab.From(original[sources[s]]);

        // ---- Nearest allowed colour per source --------------------------------------------
        var alive = new bool[candidates.Count];
        Array.Fill(alive, true);
        var assignment = new int[sources.Count];
        for (var s = 0; s < sources.Count; s++)
            assignment[s] = Nearest(sourceLab[s], candidateLab, alive);

        // ---- Enforce the limit: drop the smallest layer, reassign its sources -------------
        var weight = new long[candidates.Count];
        while (true)
        {
            Array.Clear(weight);
            for (var s = 0; s < sources.Count; s++)
                weight[assignment[s]] += counts[sources[s]];

            var used = 0;
            for (var c = 0; c < candidates.Count; c++)
                if (weight[c] > 0) used++;
            if (used <= capacity)
                break;

            // Keep the largest black/contour layer: outlines are thin but visually essential.
            var protectedCandidate = -1;
            if (capacity >= 2)
                for (var c = 0; c < candidates.Count; c++)
                    if (weight[c] > 0 && PaletteColor.Group(allowed[candidates[c]]) == 0 &&
                        (protectedCandidate < 0 || weight[c] > weight[protectedCandidate]))
                        protectedCandidate = c;

            // Smallest used layer goes; on equal size the later allowed colour goes.
            var drop = -1;
            for (var c = 0; c < candidates.Count; c++)
            {
                if (weight[c] == 0 || c == protectedCandidate) continue;
                if (drop < 0 || weight[c] <= weight[drop])
                    drop = c;
            }

            alive[drop] = false;
            for (var s = 0; s < sources.Count; s++)
                if (assignment[s] == drop)
                    assignment[s] = Nearest(sourceLab[s], candidateLab, alive);
        }

        // ---- Stable output order ----------------------------------------------------------
        var survivors = new List<int>();
        for (var c = 0; c < candidates.Count; c++)
            if (weight[c] > 0)
                survivors.Add(c);

        survivors.Sort((x, y) =>
        {
            var cx = allowed[candidates[x]];
            var cy = allowed[candidates[y]];
            var cmp = PaletteColor.Group(cx).CompareTo(PaletteColor.Group(cy));
            if (cmp == 0) cmp = weight[y].CompareTo(weight[x]);
            if (cmp == 0) cmp = ((uint)cx).CompareTo((uint)cy);
            return cmp != 0 ? cmp : x.CompareTo(y);
        });

        var rank = new int[candidates.Count];
        var palette = new SKColor[survivors.Count];
        for (var i = 0; i < survivors.Count; i++)
        {
            rank[survivors[i]] = i;
            palette[i] = allowed[candidates[survivors[i]]];
        }

        // ---- Relabel ----------------------------------------------------------------------
        var lut = new byte[256];
        Array.Fill(lut, transparentLabel); // sentinel and invalid labels stay transparent
        for (var s = 0; s < sources.Count; s++)
            lut[sources[s]] = (byte)rank[assignment[s]];

        for (var i = 0; i < labels.Length; i++)
            result[i] = lut[labels[i]];

        return (result, palette);
    }

    // ===================================================================================
    // Helpers
    // ===================================================================================

    private static int Nearest(in Lab source, Lab[] candidates, bool[] alive)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var c = 0; c < candidates.Length; c++)
        {
            if (!alive[c]) continue;
            var d = source.DistanceSquared(candidates[c]);
            if (source.IsNeutral != candidates[c].IsNeutral)
                d += NeutralMismatchPenalty;
            if (best < 0 || d < bestDistance - TieEpsilon) // ties: earlier allowed colour wins
            {
                best = c;
                bestDistance = d;
            }
        }

        return best;
    }

    private static readonly double[] LinearLut = BuildLinearLut();

    private static double[] BuildLinearLut()
    {
        var lut = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var v = i / 255.0;
            lut[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return lut;
    }

    private readonly struct Lab
    {
        private readonly double _l;
        private readonly double _a;
        private readonly double _b;

        private Lab(double l, double a, double b)
        {
            _l = l;
            _a = a;
            _b = b;
            IsNeutral = a * a + b * b < NeutralChroma * NeutralChroma;
        }

        public bool IsNeutral { get; }

        public double DistanceSquared(in Lab other)
        {
            var dl = _l - other._l;
            var da = _a - other._a;
            var db = _b - other._b;
            return dl * dl + da * da + db * db;
        }

        public static Lab From(SKColor color)
        {
            var r = LinearLut[color.Red];
            var g = LinearLut[color.Green];
            var b = LinearLut[color.Blue];

            var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
            var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
            var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

            return new Lab(
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }
    }
}
