using System;
using System.Web;

namespace 图书馆理模块.Services
{
    /// <summary>
    /// Temporary local-only protection for the unauthenticated course app.
    /// It does not replace staff accounts, CSRF tokens or audit logging.
    /// </summary>
    public static class LocalAccess
    {
        public static bool CanReadPrivateData()
        {
            var context = HttpContext.Current;
            return context != null && context.Request.IsLocal;
        }

        public static bool CanWrite()
        {
            if (!CanReadPrivateData()) return false;

            var request = HttpContext.Current.Request;
            return IsSameOriginWrite(request.Url, request.Headers["Origin"], request.UrlReferrer);
        }

        /// <summary>
        /// Fail closed when neither browser origin nor referrer is available.
        /// Both headers must match when both are present. These headers alone
        /// are not authentication; do not expose this app to untrusted clients.
        /// </summary>
        public static bool IsSameOriginWrite(Uri requestUrl, string originHeader, Uri referrer)
        {
            if (requestUrl == null || !requestUrl.IsAbsoluteUri) return false;

            bool hasOrigin = !string.IsNullOrWhiteSpace(originHeader);
            bool hasReferrer = referrer != null;
            if (!hasOrigin && !hasReferrer) return false;

            if (hasOrigin)
            {
                Uri origin;
                if (!Uri.TryCreate(originHeader, UriKind.Absolute, out origin)
                    || origin.AbsolutePath != "/"
                    || !string.IsNullOrEmpty(origin.Query)
                    || !string.IsNullOrEmpty(origin.Fragment)
                    || !SameOrigin(requestUrl, origin))
                    return false;
            }

            return !hasReferrer || SameOrigin(requestUrl, referrer);
        }

        private static bool SameOrigin(Uri expected, Uri candidate)
        {
            return candidate.IsAbsoluteUri
                && string.Equals(expected.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(expected.IdnHost, candidate.IdnHost, StringComparison.OrdinalIgnoreCase)
                && expected.Port == candidate.Port;
        }
    }
}
