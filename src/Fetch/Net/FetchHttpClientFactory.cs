using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Fetch.Net
{
    /// <summary>
    /// Single place where HTTP clients are configured. FETCH previously created them in three
    /// places with different user agents, redirect handling, and timeouts.
    /// </summary>
    internal static class FetchHttpClientFactory
    {
        internal const string UserAgent = "FETCH-Revit";

        internal static HttpMessageHandler CreateDownloadHandler()
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect = true,
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
        }

        /// <summary>
        /// Client for package downloads. The timeout is disabled deliberately: with
        /// ResponseHeadersRead it would not bound a stalled body read anyway, and it would cut off
        /// a legitimately slow one. The caller applies progress-based cancellation instead.
        /// </summary>
        internal static HttpClient CreateDownloadClient(HttpMessageHandler handler)
        {
            HttpClient client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        /// <summary>Client for resolving a short-link redirect without following it.</summary>
        internal static HttpClient CreateRedirectProbeClient(TimeSpan timeout, out HttpMessageHandler handler)
        {
            handler = new HttpClientHandler { AllowAutoRedirect = false };

            HttpClient client = new HttpClient(handler)
            {
                Timeout = timeout
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }
    }
}
