using System.Linq;
using System.Text.RegularExpressions;

namespace TileServer.Http
{
    public static class StringUtil
    {
        private static readonly Regex CamelCaseRegex = new Regex(@"(^\p{Ll}+|\p{Lu}+(?!\p{Ll})|\p{Lu}\p{Ll}+)", RegexOptions.Compiled);

        public static string UncamelCase(string s)
        {
            // Adapted from http://stackoverflow.com/a/37532157/1025421
            var words = CamelCaseRegex.Matches(s)
                .OfType<Match>()
                .Select(m => m.Value)
                .ToArray();
            return string.Join(" ", words);
        }
    }
}
