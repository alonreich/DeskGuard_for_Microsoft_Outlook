namespace DeskGuard.Core.Scoring;

using System;

public static class StringDistance
{
    public static int LevenshteinDistance(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2)
    {
        if (s1.SequenceEqual(s2)) return 0;
        if (s1.Length == 0) return s2.Length;
        if (s2.Length == 0) return s1.Length;

        int len1 = s1.Length;
        int len2 = s2.Length;

        // Use stackalloc if buffer fits within 512 ints, else array pool / heap
        int bufferSize = len2 + 1;
        Span<int> v0 = bufferSize <= 512 ? stackalloc int[bufferSize] : new int[bufferSize];
        Span<int> v1 = bufferSize <= 512 ? stackalloc int[bufferSize] : new int[bufferSize];

        for (int i = 0; i <= len2; i++)
        {
            v0[i] = i;
        }

        for (int i = 0; i < len1; i++)
        {
            v1[0] = i + 1;
            char c1 = s1[i];

            for (int j = 0; j < len2; j++)
            {
                int cost = c1 == s2[j] ? 0 : 1;
                int deletion = v1[j] + 1;
                int insertion = v0[j + 1] + 1;
                int substitution = v0[j] + cost;

                v1[j + 1] = Math.Min(deletion, Math.Min(insertion, substitution));
            }

            v1.CopyTo(v0);
        }

        return v1[len2];
    }
}
