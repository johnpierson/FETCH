using System;
using System.Net.Http;
using System.Net.Sockets;

namespace Fetch.Sync
{
    /// <summary>
    /// Separates "this machine cannot reach the network right now" from "the sync is misconfigured
    /// or the source is wrong".
    ///
    /// A laptop starting Revit offline is an ordinary situation and should not raise a dialog every
    /// launch; a bad URL, a missing release asset, or a failed integrity check is something the user
    /// needs to be told about. FETCH previously decided this by probing google.com before
    /// downloading, which answered a different question than the one that mattered.
    /// </summary>
    internal static class SyncFailureClassifier
    {
        internal static bool IsLikelyOffline(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException)
                {
                    return IsUnreachable(socketException.SocketErrorCode);
                }

                // A name that does not resolve, or a connection that cannot be established, surfaces
                // as HttpRequestException with no more specific inner detail on some platforms.
                if (current is HttpRequestException && current.InnerException == null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUnreachable(SocketError error)
        {
            switch (error)
            {
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.NetworkDown:
                case SocketError.NetworkUnreachable:
                case SocketError.HostUnreachable:
                case SocketError.HostDown:
                case SocketError.ConnectionRefused:
                case SocketError.TryAgain:
                case SocketError.TimedOut:
                    return true;
                default:
                    return false;
            }
        }
    }
}
