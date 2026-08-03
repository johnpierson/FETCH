using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fetch.Tests
{
    /// <summary>
    /// A loopback TCP server that speaks just enough HTTP to exercise the downloader. Uses a raw
    /// socket rather than HttpListener so the tests need no URL reservation or elevation.
    /// </summary>
    internal sealed class StubHttpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly Task acceptLoop;

        private StubHttpServer(Func<Stream, CancellationToken, Task> respond)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;

            acceptLoop = Task.Run(async () =>
            {
                try
                {
                    while (!shutdown.IsCancellationRequested)
                    {
                        using (TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                        using (NetworkStream stream = client.GetStream())
                        {
                            await ReadRequestHeaderAsync(stream, shutdown.Token).ConfigureAwait(false);
                            await respond(stream, shutdown.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    // The listener is torn down at the end of the test; nothing to report.
                }
            });
        }

        public int Port { get; }

        public string BaseAddress => $"http://127.0.0.1:{Port}/";

        /// <summary>Serves the given bytes with a correct Content-Length.</summary>
        internal static StubHttpServer ServingBytes(byte[] payload)
        {
            return new StubHttpServer(async (stream, token) =>
            {
                byte[] header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n");

                await stream.WriteAsync(header, 0, header.Length, token).ConfigureAwait(false);
                await stream.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Announces a body then never sends it, reproducing a server that accepts the connection
        /// and then stops responding.
        /// </summary>
        internal static StubHttpServer Stalling()
        {
            return new StubHttpServer(async (stream, token) =>
            {
                byte[] header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: 1048576\r\n\r\n");

                await stream.WriteAsync(header, 0, header.Length, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);

                try
                {
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        private static async Task ReadRequestHeaderAsync(Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[1];
            int consecutiveNewlines = 0;

            while (consecutiveNewlines < 2)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                if (buffer[0] == (byte)'\n')
                {
                    consecutiveNewlines++;
                }
                else if (buffer[0] != (byte)'\r')
                {
                    consecutiveNewlines = 0;
                }
            }
        }

        public void Dispose()
        {
            shutdown.Cancel();

            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
            }

            try
            {
                acceptLoop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }

            shutdown.Dispose();
        }
    }
}
