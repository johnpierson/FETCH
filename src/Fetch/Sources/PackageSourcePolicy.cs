using System;

namespace Fetch.Sources
{
    /// <summary>
    /// Decides whether a configured package source URL may be used.
    ///
    /// A package zip becomes executable content inside Revit (Dynamo loads package assemblies), so
    /// it must not be fetched over a channel that a network attacker can rewrite. Plain HTTP is
    /// therefore rejected unless the deployment explicitly opts in, or the host is loopback.
    /// </summary>
    internal static class PackageSourcePolicy
    {
        internal static bool TryValidateUrl(string address, bool allowInsecureHttp, out Uri packageUri, out string error)
        {
            packageUri = null;
            error = null;

            if (string.IsNullOrWhiteSpace(address))
            {
                error = "No package source is configured. Set Path in the [Settings] section of FetchSettings.ini to a local folder or an https:// link.";
                return false;
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri uri))
            {
                error = "The package source is neither an existing folder nor a valid URL.";
                return false;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                packageUri = uri;
                return true;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                // Loopback cannot be intercepted by a network attacker, so local testing still works.
                if (allowInsecureHttp || uri.IsLoopback)
                {
                    packageUri = uri;
                    return true;
                }

                error = "The package source uses plain http://, which lets anyone on the network replace the packages that Revit will load. "
                        + "Use an https:// link instead, or set AllowInsecureHttp=true in the [Settings] section of FetchSettings.ini to accept that risk.";
                return false;
            }

            error = $"The package source URL uses an unsupported scheme '{uri.Scheme}'. Use an https:// link or a local folder path.";
            return false;
        }
    }
}
