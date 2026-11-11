using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aelid.Rendering
{
    /// <summary>
    /// Builds the glyph atlas used by the ASCII pass from hand-drawn 8x8
    /// bitmaps, so no font file or imported texture is required.
    ///
    /// Layout: 16 columns x 16 rows of 8x8 glyphs (128x128, R8).
    ///   Row    = glyph vocabulary, indexed by AsciiSurfaceClass (row 15 = sky).
    ///   Col 0-9  = density ramp, sorted by ink coverage (dark -> bright).
    ///   Col 10-13 = line glyphs | - / \ used on detected edges.
    ///   Col 14 = sparkle (flowers by day, fireflies by night).
    ///   Col 15 = solid block.
    /// </summary>
    public static class AsciiGlyphAtlas
    {
        public const int GlyphSize = 8;
        public const int Columns = 16;
        public const int Rows = 16;
        public const int Levels = 10;
        public const int Size = GlyphSize * Columns; // 128

        public const int ColEdgeVertical = 10;
        public const int ColEdgeHorizontal = 11;
        public const int ColEdgeSlash = 12;
        public const int ColEdgeBackslash = 13;
        public const int ColSparkle = 14;
        public const int ColSolid = 15;

        // Thin grass blades have no keyboard character; they live in the
        // private-use range so vocabulary strings can still list them.
        const char Sprout = '';
        const char Blade = '';
        const char TallBlade = '';
        const char TwinBlades = '';
        const char TallTwin = '';
        const char Tuft = '';
        const char ThickTuft = '';
        const char Sward = '';

        // Water: horizontal strokes only, so lakes read as rippling lines.
        const char Dash = '';
        const char WaterHatch = '';
        const char WaterLines = '';

        static readonly string k_GrassBlades = new(new[] { ' ', '.', Sprout, Blade, TallBlade, TwinBlades, TallTwin, Tuft, ThickTuft, Sward });
        static readonly string k_WaterLines = new(new[] { ' ', Dash, '-', '~', '=', '≡', '≈', WaterHatch, '≋', WaterLines });

        // Glyph vocabularies (any order; sorted by coverage at build time).
        // Dense surfaces use letters, as in classic ASCII art: walls read as text.
        static readonly Dictionary<int, string> k_Vocabularies = new()
        {
            { (int)AsciiSurfaceClass.Default,  " .:-=+*#%@" },
            { (int)AsciiSurfaceClass.Exempt,   " .:-=+*#%@" },
            { (int)AsciiSurfaceClass.Foliage,  " .,;o*&8♣♠" },
            { (int)AsciiSurfaceClass.Water,    k_WaterLines },
            { (int)AsciiSurfaceClass.Stone,    " .:oO8SQMB" },
            { (int)AsciiSurfaceClass.Emissive, " .'^*+A#%@" },
            { (int)AsciiSurfaceClass.Grass,    k_GrassBlades },
            { (int)AsciiSurfaceClass.Wood,     " .-=+xNH#@" },
            { AsciiSurfaceLayers.SkyClass,     " .`'-~+*=%" },
        };

        // 8x8 bitmaps, top row first. 'X' = ink.
        static readonly Dictionary<char, string[]> k_Glyphs = new()
        {
            { ' ', new[] { "........", "........", "........", "........", "........", "........", "........", "........" } },
            { '.', new[] { "........", "........", "........", "........", "........", "...XX...", "...XX...", "........" } },
            { ',', new[] { "........", "........", "........", "........", "........", "...XX...", "...XX...", "..XX...." } },
            { '\'', new[] { "...XX...", "...XX...", "..XX....", "........", "........", "........", "........", "........" } },
            { '`', new[] { "..XX....", "...XX...", "........", "........", "........", "........", "........", "........" } },
            { '"', new[] { ".XX.XX..", ".XX.XX..", ".X..X...", "........", "........", "........", "........", "........" } },
            { ':', new[] { "........", "...XX...", "...XX...", "........", "........", "...XX...", "...XX...", "........" } },
            { ';', new[] { "........", "...XX...", "...XX...", "........", "........", "...XX...", "...XX...", "..XX...." } },
            { '-', new[] { "........", "........", "........", ".XXXXXX.", "........", "........", "........", "........" } },
            { '=', new[] { "........", "........", ".XXXXXX.", "........", "........", ".XXXXXX.", "........", "........" } },
            { '+', new[] { "........", "...XX...", "...XX...", ".XXXXXX.", "...XX...", "...XX...", "........", "........" } },
            { '*', new[] { "........", ".XX..XX.", "..XXXX..", "XXXXXXXX", "..XXXX..", ".XX..XX.", "........", "........" } },
            { 'x', new[] { "........", "........", ".XX..XX.", "..XXXX..", "...XX...", "..XXXX..", ".XX..XX.", "........" } },
            { '#', new[] { ".XX.XX..", ".XX.XX..", "XXXXXXX.", ".XX.XX..", "XXXXXXX.", ".XX.XX..", ".XX.XX..", "........" } },
            { '%', new[] { "XX...XX.", "XX..XX..", "...XX...", "..XX....", ".XX.....", "XX..XX..", "X...XX..", "........" } },
            { '@', new[] { ".XXXXX..", "XX...XX.", "XX.XXXX.", "XX.XXXX.", "XX.XXX..", "XX......", ".XXXX...", "........" } },
            { '&', new[] { "..XXX...", ".XX.XX..", "..XXX...", ".XXX.XX.", "XX.XXX..", "XX..XX..", ".XXX.XX.", "........" } },
            { '♣', new[] { "...XX...", "..XXXX..", "..XXXX..", "XX.XX.XX", "XXXXXXXX", "XX.XX.XX", "...XX...", "..XXXX.." } },
            { '♠', new[] { "...XX...", "..XXXX..", ".XXXXXX.", "XXXXXXXX", "XXXXXXXX", ".XX..XX.", "...XX...", "..XXXX.." } },
            { '~', new[] { "........", "........", "........", ".XXX.XX.", "XX.XXX..", "........", "........", "........" } },
            { '≈', new[] { "........", ".XXX.XX.", "XX.XXX..", "........", ".XXX.XX.", "XX.XXX..", "........", "........" } },
            { '≋', new[] { ".XXX.XX.", "XX.XXX..", "........", ".XXX.XX.", "XX.XXX..", "........", ".XXX.XX.", "XX.XXX.." } },
            { '░', new[] { "X...X...", "..X...X.", "X...X...", "..X...X.", "X...X...", "..X...X.", "X...X...", "..X...X." } },
            { '▒', new[] { "X.X.X.X.", ".X.X.X.X", "X.X.X.X.", ".X.X.X.X", "X.X.X.X.", ".X.X.X.X", "X.X.X.X.", ".X.X.X.X" } },
            { '▓', new[] { ".XXX.XXX", "XX.XXX.X", ".XXX.XXX", "XX.XXX.X", ".XXX.XXX", "XX.XXX.X", ".XXX.XXX", "XX.XXX.X" } },
            { 'H', new[] { "XX..XX..", "XX..XX..", "XX..XX..", "XXXXXX..", "XX..XX..", "XX..XX..", "XX..XX..", "........" } },
            { '^', new[] { "...XX...", "..XXXX..", ".XX..XX.", "XX....XX", "........", "........", "........", "........" } },
            { 'A', new[] { "..XX....", ".XXXX...", "XX..XX..", "XX..XX..", "XXXXXX..", "XX..XX..", "XX..XX..", "........" } },
            { 'v', new[] { "........", "........", "XX..XX..", "XX..XX..", "XX..XX..", ".XXXX...", "..XX....", "........" } },
            { 'w', new[] { "........", "........", "XX...XX.", "XX.X.XX.", "XXXXXXX.", "XXX.XXX.", "XX...XX.", "........" } },
            { 'W', new[] { "XX...XX.", "XX...XX.", "XX...XX.", "XX.X.XX.", "XXXXXXX.", "XXX.XXX.", "XX...XX.", "........" } },

            // Thin-stroke letters for dense surfaces (stone, timber, canopies).
            { 'o', new[] { "........", "........", "..XXX...", ".X...X..", ".X...X..", ".X...X..", "..XXX...", "........" } },
            { 'O', new[] { "..XXX...", ".X...X..", "X.....X.", "X.....X.", "X.....X.", ".X...X..", "..XXX...", "........" } },
            { 'Q', new[] { "..XXX...", ".X...X..", "X.....X.", "X.....X.", "X...X.X.", ".X...X..", "..XXX.X.", "........" } },
            { '8', new[] { "..XXX...", ".X...X..", ".X...X..", "..XXX...", ".X...X..", ".X...X..", "..XXX...", "........" } },
            { 'B', new[] { "XXXXX...", "X....X..", "X....X..", "XXXXX...", "X....X..", "X....X..", "XXXXX...", "........" } },
            { 'S', new[] { "..XXXX..", ".X......", ".X......", "..XXX...", ".....X..", ".....X..", "XXXXX...", "........" } },
            { 'M', new[] { "X.....X.", "XX...XX.", "X.X.X.X.", "X..X..X.", "X.....X.", "X.....X.", "X.....X.", "........" } },
            { 'N', new[] { "X.....X.", "XX....X.", "X.X...X.", "X..X..X.", "X...X.X.", "X....XX.", "X.....X.", "........" } },

            // Grass blades, 1 px strokes rooted at the cell bottom, sparse -> dense.
            { Sprout,     new[] { "........", "........", "........", "........", "........", "....X...", "....X...", "........" } },
            { Blade,      new[] { "........", "........", "........", "........", "........", ".....X..", "....X...", "....X..." } },
            { TallBlade,  new[] { "........", "........", "........", "...X....", "...X....", "....X...", "....X...", "....X..." } },
            { TwinBlades, new[] { "........", "........", "........", "........", "..X...X.", "..X..X..", "...X.X..", "...X.X.." } },
            { TallTwin,   new[] { "........", "........", ".X......", ".X....X.", "..X..X..", "..X..X..", "...X.X..", "...X.X.." } },
            { Tuft,       new[] { "........", "....X...", ".X..X..X", ".X..X.X.", "..X.X.X.", "..X.X.X.", "...XXX..", "...XX..." } },
            { ThickTuft,  new[] { "...X....", "X..X..X.", ".X.X.X..", ".X.X.X..", ".XX.X..X", "..X.X.X.", "..XXX.X.", "...XXX.." } },
            { Sward,      new[] { "X..X.X..", "X.X..X.X", ".XX.X.X.", "X.X.XX.X", ".XX.X.X.", "X.XXX.X.", ".XX.XXX.", "..XXXX.." } },

            // Water lines.
            { Dash,       new[] { "........", "........", "........", "........", "...XX...", "........", "........", "........" } },
            { '≡',        new[] { "........", ".XXXXXX.", "........", ".XXXXXX.", "........", ".XXXXXX.", "........", "........" } },
            { WaterHatch, new[] { "........", "XXX.XXXX", "........", ".XXXX.XX", "........", "XX.XXXX.", "........", "XXXX.XXX" } },
            { WaterLines, new[] { "XXXXXXXX", "........", "XXXXXXXX", "........", "XXXXXXXX", "........", "XXXXXXXX", "........" } },
        };

        // Line glyphs. Diagonals run corner to corner so they connect across cells.
        static readonly string[] k_EdgeVertical = { "...XX...", "...XX...", "...XX...", "...XX...", "...XX...", "...XX...", "...XX...", "...XX..." };
        static readonly string[] k_EdgeHorizontal = { "........", "........", "........", "XXXXXXXX", "XXXXXXXX", "........", "........", "........" };
        static readonly string[] k_EdgeSlash = { "......XX", ".....XX.", "....XX..", "...XX...", "..XX....", ".XX.....", "XX......", "X......." };
        static readonly string[] k_EdgeBackslash = { "XX......", ".XX.....", "..XX....", "...XX...", "....XX..", ".....XX.", "......XX", ".......X" };
        static readonly string[] k_Sparkle = { "........", "........", "...X....", "..XXX...", "...X....", "........", "........", "........" };
        static readonly string[] k_Solid = { "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX", "XXXXXXXX" };

        static Texture2D s_Default;

        /// <summary>Shared runtime atlas (built on first use, never saved).</summary>
        public static Texture2D Default
        {
            get
            {
                if (s_Default == null)
                {
                    s_Default = Build(readable: false);
                    s_Default.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Default;
            }
        }

        /// <summary>Returns the ramp of a vocabulary after coverage sorting (for docs/debug).</summary>
        public static string GetSortedRamp(int row) => new string(SortedRamp(row));

        public static Texture2D Build(bool readable)
        {
            var pixels = new byte[Size * Size];
            for (int row = 0; row < Rows; row++)
            {
                char[] ramp = SortedRamp(row);
                for (int col = 0; col < Levels; col++)
                    Blit(pixels, row, col, k_Glyphs[ramp[col]]);

                Blit(pixels, row, ColEdgeVertical, k_EdgeVertical);
                Blit(pixels, row, ColEdgeHorizontal, k_EdgeHorizontal);
                Blit(pixels, row, ColEdgeSlash, k_EdgeSlash);
                Blit(pixels, row, ColEdgeBackslash, k_EdgeBackslash);
                Blit(pixels, row, ColSparkle, k_Sparkle);
                Blit(pixels, row, ColSolid, k_Solid);
            }

            var texture = new Texture2D(Size, Size, TextureFormat.R8, false, true)
            {
                name = "AsciiGlyphAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, !readable);
            return texture;
        }

        static char[] SortedRamp(int row)
        {
            if (!k_Vocabularies.TryGetValue(row, out string vocabulary))
                vocabulary = k_Vocabularies[(int)AsciiSurfaceClass.Default];

            char[] chars = vocabulary.ToCharArray();
            if (chars.Length != Levels)
                throw new InvalidOperationException($"Glyph vocabulary for row {row} must have {Levels} glyphs.");

            // Stable sort by ink coverage so each ramp runs dark -> bright.
            var indexed = new List<(char c, int ink, int order)>();
            for (int i = 0; i < chars.Length; i++)
                indexed.Add((chars[i], Ink(k_Glyphs[chars[i]]), i));
            indexed.Sort((a, b) => a.ink != b.ink ? a.ink.CompareTo(b.ink) : a.order.CompareTo(b.order));
            for (int i = 0; i < chars.Length; i++)
                chars[i] = indexed[i].c;
            return chars;
        }

        static int Ink(string[] bitmap)
        {
            int count = 0;
            foreach (string line in bitmap)
                foreach (char c in line)
                    if (c == 'X') count++;
            return count;
        }

        static void Blit(byte[] pixels, int row, int col, string[] bitmap)
        {
            for (int gy = 0; gy < GlyphSize; gy++)
            {
                string line = bitmap[gy];
                // Texture row 0 is the bottom of the image, glyph row 0 is the top.
                int ty = Size - 1 - (row * GlyphSize + gy);
                for (int gx = 0; gx < GlyphSize; gx++)
                {
                    int tx = col * GlyphSize + gx;
                    pixels[ty * Size + tx] = line[gx] == 'X' ? (byte)255 : (byte)0;
                }
            }
        }
    }
}
