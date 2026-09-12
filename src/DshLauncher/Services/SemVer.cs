namespace DshLauncher.Services
{
    /// <summary>
    /// Minimal semantic-version comparison for "is there a newer dsh?" and
    /// plugin/harness compatibility checks. Supports major.minor.patch with an
    /// optional -prerelease suffix; a release always ranks above its
    /// prereleases.
    /// </summary>
    public static class SemVer
    {
        public readonly struct Version
        {
            public int Major { get; }
            public int Minor { get; }
            public int Patch { get; }
            public string? Prerelease { get; }

            public Version(int major, int minor, int patch, string? prerelease)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                Prerelease = prerelease;
            }

            public bool IsRelease => Prerelease == null;
        }

        public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

        public static int Compare(string a, string b) => Compare(Parse(a) ?? default, Parse(b) ?? default);

        public static int Compare(in Version a, in Version b)
        {
            if (a.Major != b.Major)
            {
                return a.Major.CompareTo(b.Major);
            }
            if (a.Minor != b.Minor)
            {
                return a.Minor.CompareTo(b.Minor);
            }
            if (a.Patch != b.Patch)
            {
                return a.Patch.CompareTo(b.Patch);
            }

            if (a.IsRelease && b.IsRelease)
            {
                return 0;
            }
            if (a.IsRelease)
            {
                return 1; // a is a release, b is a prerelease
            }
            if (b.IsRelease)
            {
                return -1; // b is a release
            }
            return string.CompareOrdinal(a.Prerelease, b.Prerelease);
        }

        public static Version? Parse(string version)
        {
            var v = version?.Trim() ?? string.Empty;
            if (v.Length == 0)
            {
                return null;
            }

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
            if (parts.Length == 0 || parts.Length > 3)
            {
                return null;
            }

            var numeric = new int[3];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out numeric[i]))
                {
                    return null;
                }
            }

            return new Version(numeric[0], numeric[1], numeric[2], prerelease);
        }
    }
}
