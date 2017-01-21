using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace TileServer.Http
{
    public static class HttpUtil
    {
        public static string GetVersionString(HttpVersion httpVersion)
        {
            switch (httpVersion)
            {
                case HttpVersion.Http10:
                    return "HTTP/1.0";
                case HttpVersion.Http11:
                    return "HTTP/1.1";
                default:
                    throw new ArgumentOutOfRangeException(nameof(httpVersion), httpVersion, null);
            }
        }

        public static string GetStatusString(HttpStatusCode statusCode)
        {
            return ((int) statusCode).ToString(CultureInfo.InvariantCulture) + " " +
                   StringUtil.UncamelCase(statusCode.ToString());
        }

        public static bool IsConnectionPersistent(HttpVersion httpVersion, IReadOnlyDictionary<string, string> headers)
        {
            string temp;
            if (httpVersion == HttpVersion.Http10)
            {
                return headers.TryGetValue("Connection", out temp) &&
                       !"Close".Equals(temp, StringComparison.OrdinalIgnoreCase);
            }

            if (httpVersion == HttpVersion.Http11)
            {
                return !headers.TryGetValue("Connection", out temp) ||
                       !"Close".Equals(temp, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}