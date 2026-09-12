using System;

namespace DshLauncher.Services
{
    /// <summary>
    /// Minimal semver RANGE matching for plugin peerDependencies: supports the
    /// patterns seen in the wild — <c>*</c>, exact versions, partial versions
    /// (<c>1.2</c>, <c>1</c>), comparators (<c>&gt;= &gt; &lt;= &lt; =</c>),
    /// caret/tilde (<c>^ ~</c>), AND lists (space/comma) and OR lists (<c>||</c>),
    /// with optional -prerelease suffixes.
    /// </summary>
    public static class SemVerRange
    {
        public static bool Satisfies(string version, string range)
        {
            var v = SemVer.Parse(version);
            if (v == null || string.IsNullOrWhiteSpace(range))
            {
                return false;
            }

            foreach (var alternative in range.Split(new[] { "||" }, StringSplitOptions.None))
            {
                if (SatisfiesAnd(v.Value, alternative))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SatisfiesAnd(in SemVer.Version v, string alternative)
        {
            foreach (var token in alternative.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SatisfiesComparator(v, token))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SatisfiesComparator(in SemVer.Version v, string comparator)
        {
            var spec = comparator.Trim();
            if (spec == "*" || spec == "x" || spec == "X")
            {
                return true;
            }

            var op = "";
            foreach (var prefix in new[] { ">=", "<=", ">", "<", "=", "^", "~" })
            {
                if (spec.StartsWith(prefix, StringComparison.Ordinal))
                {
                    op = prefix;
                    spec = spec.Substring(prefix.Length).Trim();
                    break;
                }
            }

            var target = ParseTarget(spec);
            if (target == null)
            {
                return false;
            }
            var (major, minor, patch, parts, pre) = target.Value;

            switch (op)
            {
                case ">=":
                    return Compare(v, major, minor, patch, pre) >= 0;
                case ">":
                    return Compare(v, major, minor, patch, pre) > 0;
                case "<=":
                    return Compare(v, major, minor, patch, pre) <= 0;
                case "<":
                    return Compare(v, major, minor, patch, pre) < 0;
                case "=":
                    return Compare(v, major, minor, patch, pre) == 0;

                case "^":
                {
                    if (Compare(v, major, minor, patch, pre) < 0)
                    {
                        return false;
                    }
                    int upperMajor, upperMinor, upperPatch;
                    if (major > 0)
                    {
                        upperMajor = major + 1;
                        upperMinor = 0;
                        upperPatch = 0;
                    }
                    else if (minor > 0)
                    {
                        upperMajor = 0;
                        upperMinor = minor + 1;
                        upperPatch = 0;
                    }
                    else
                    {
                        upperMajor = 0;
                        upperMinor = 0;
                        upperPatch = patch + 1;
                    }
                    return Compare(v, upperMajor, upperMinor, upperPatch, null) < 0;
                }

                case "~":
                {
                    if (Compare(v, major, minor, patch, pre) < 0)
                    {
                        return false;
                    }
                    var upperMajor = parts >= 2 ? major : major + 1;
                    var upperMinor = parts >= 2 ? minor + 1 : 0;
                    return Compare(v, upperMajor, upperMinor, 0, null) < 0;
                }

                default: // bare
                {
                    if (parts == 3)
                    {
                        return Compare(v, major, minor, patch, pre) == 0;
                    }
                    if (Compare(v, major, minor, patch, pre) < 0)
                    {
                        return false;
                    }
                    var upperMajor = parts == 1 ? major + 1 : major;
                    var upperMinor = parts == 1 ? 0 : minor + 1;
                    return Compare(v, upperMajor, upperMinor, 0, null) < 0;
                }
            }
        }

        private static (int major, int minor, int patch, int parts, string? pre)? ParseTarget(string spec)
        {
            var dash = spec.IndexOf('-');
            var core = dash >= 0 ? spec.Substring(0, dash) : spec;
            var pre = dash >= 0 ? spec.Substring(dash + 1) : null;

            var segs = core.Split('.');
            if (segs.Length == 0 || segs.Length > 3)
            {
                return null;
            }

            var nums = new int[3];
            for (var i = 0; i < segs.Length; i++)
            {
                if (!int.TryParse(segs[i], out nums[i]))
                {
                    return null;
                }
            }

            return (nums[0], nums[1], nums[2], segs.Length, pre);
        }

        private static int Compare(in SemVer.Version v, int major, int minor, int patch, string? pre)
        {
            return SemVer.Compare(v, new SemVer.Version(major, minor, patch, pre));
        }
    }
}
