namespace DshLauncher.Services
{
    /// <summary>
    /// Minimal semantic-version comparison for "is there a newer dsh?"
    /// Supports major.minor.patch with an optional -prerelease suffix; a
    /// release always ranks above its prereleases.
    /// </summary>
    public static class SemVer
    {
        public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

        public static int Compare(string a, string b)
        {
            var (numA, preA) = Split(a);
            var (numB, preB) = Split(b);

            for (var i = 0; i < 3; i++)
            {
                var c = numA[i].CompareTo(numB[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            if (preA == null && preB == null)
            {
                return 0;
            }
            if (preA == null)
            {
                return 1; // a is a release, b is a prerelease
            }
            if (preB == null)
            {
                return -1; // b is a release
            }
            return string.CompareOrdinal(preA, preB);
        }

        private static (int[] numeric, string? prerelease) Split(string version)
        {
            var v = version?.Trim() ?? string.Empty;
            var plusIdx = v.IndexOf('+');
            if (plusIdx >= 0)
            {
                v = v.Substring(0, plusIdx);
            }

            string? prerelease = null;
            var dashIdx = v.IndexOf('-');
            if (dashIdx >= 0)
            {
                prerelease = v.Substring(dashIdx + 1);
                v = v.Substring(0, dashIdx);
            }

            var parts = v.Split('.');
            var numeric = new int[3];
            for (var i = 0; i < 3; i++)
            {
                numeric[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
            }

            return (numeric, prerelease);
        }
    }
}
