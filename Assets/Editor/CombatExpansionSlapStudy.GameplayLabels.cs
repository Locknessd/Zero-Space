using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static void CandidateLabel(Texture2D texture, int x, int y, string value)
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ.=|";
            string[] glyphs =
            {
                "111101101101111", "010110010010111", "111001111100111", "111001111001111",
                "101101111001001", "111100111001111", "111100111101111", "111001001001001",
                "111101111101111", "111101111001111", "010101111101101", "110101110101110",
                "111100100100111", "110101101101110", "111100110100111", "111100110100100",
                "111100101101111", "101101111101101", "111010010010111", "001001001101111",
                "101101110101101", "100100100100111", "101111111101101", "101111111111101",
                "111101101101111", "111101111100100", "111101101111001", "110101110101101",
                "111100111001111", "111010010010010", "101101101101111", "101101101101010",
                "101101111111101", "101101010101101", "101101010010010", "111001010100111",
                "000000000000010", "000111000111000", "010010010010010"
            };
            foreach (char character in value)
            {
                int glyph = alphabet.IndexOf(character);
                if (glyph >= 0)
                    for (int row = 0; row < 5; row++)
                    for (int column = 0; column < 3; column++)
                        if (glyphs[glyph][row * 3 + column] == '1')
                            for (int dy = 0; dy < 3; dy++)
                            for (int dx = 0; dx < 3; dx++)
                                texture.SetPixel(x + column * 3 + dx, y + (4 - row) * 3 + dy, Color.white);
                x += 12;
            }
        }
    }
}
