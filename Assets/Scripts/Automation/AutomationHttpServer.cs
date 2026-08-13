using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace VelkhanaSlice.Automation
{
    /// <summary>
    /// Tiny loopback-only HTTP/1.1 server. TcpListener avoids Windows HttpListener URL ACL setup,
    /// so a normal standalone player can be automated without administrator privileges.
    /// </summary>
    internal sealed class AutomationHttpServer : IDisposable
    {
        internal sealed class Request
        {
            public string method;
            public string path;
            public Dictionary<string, string> query;
            public string body;
        }

        internal sealed class Response
        {
            public int statusCode;
            public string contentType;
            public string body;

            public static Response Json(int statusCode, string body)
            {
                return new Response
                {
                    statusCode = statusCode,
                    contentType = "application/json; charset=utf-8",
                    body = body ?? string.Empty,
                };
            }
        }

        const int MaximumBodyBytes = 1024 * 1024;

        readonly int _port;
        readonly Func<Request, Response> _handler;
        TcpListener _listener;
        Thread _acceptThread;
        volatile bool _running;

        public AutomationHttpServer(int port, Func<Request, Response> handler)
        {
            _port = port;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Start()
        {
            if (_running) return;

            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start(16);
            _running = true;
            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "VelkhanaAutomationHttp",
            };
            _acceptThread.Start();
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (SocketException)
                {
                    if (_running) Thread.Sleep(10);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        void HandleClient(object state)
        {
            using (var client = (TcpClient)state)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;

                try
                {
                    using (NetworkStream stream = client.GetStream())
                    {
                        Request request = ReadRequest(stream);
                        Response response = request != null
                            ? _handler(request)
                            : Error(400, "Malformed HTTP request");
                        WriteResponse(stream, response ?? Error(500, "Empty server response"));
                    }
                }
                catch (Exception exception)
                {
                    try
                    {
                        using (NetworkStream stream = client.GetStream())
                            WriteResponse(stream, Error(500, exception.Message));
                    }
                    catch
                    {
                        // The caller may have disconnected while a long step command was running.
                    }
                }
            }
        }

        static Request ReadRequest(Stream stream)
        {
            // Read headers as bytes so Content-Length remains a byte count for UTF-8 JSON bodies.
            // One-byte header reads are acceptable for this small loopback control protocol and
            // avoid consuming body bytes into a separate StreamReader buffer.
            var headerBytes = new MemoryStream();
            int matchedTerminatorBytes = 0;
            while (headerBytes.Length < 32768)
            {
                int value = stream.ReadByte();
                if (value < 0) return null;
                headerBytes.WriteByte((byte)value);

                switch (matchedTerminatorBytes)
                {
                    case 0: matchedTerminatorBytes = value == '\r' ? 1 : 0; break;
                    case 1: matchedTerminatorBytes = value == '\n' ? 2 : value == '\r' ? 1 : 0; break;
                    case 2: matchedTerminatorBytes = value == '\r' ? 3 : 0; break;
                    case 3: matchedTerminatorBytes = value == '\n' ? 4 : 0; break;
                }
                if (matchedTerminatorBytes == 4) break;
            }

            if (matchedTerminatorBytes != 4)
                throw new InvalidDataException("HTTP headers are too large or incomplete");

            string headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) return null;

            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf(':');
                if (separator <= 0) continue;
                string name = lines[i].Substring(0, separator).Trim();
                string value = lines[i].Substring(separator + 1).Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(value, out contentLength);
            }

            if (contentLength < 0 || contentLength > MaximumBodyBytes)
                throw new InvalidDataException("Request body is too large");

            byte[] bodyBytes = new byte[contentLength];
            int read = 0;
            while (read < contentLength)
            {
                int count = stream.Read(bodyBytes, read, contentLength - read);
                if (count <= 0) throw new EndOfStreamException("HTTP request body is incomplete");
                read += count;
            }

            ParseTarget(first[1], out string path, out Dictionary<string, string> query);
            return new Request
            {
                method = first[0].ToUpperInvariant(),
                path = path,
                query = query,
                body = Encoding.UTF8.GetString(bodyBytes),
            };
        }

        static void ParseTarget(
            string target,
            out string path,
            out Dictionary<string, string> query)
        {
            query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int queryStart = target.IndexOf('?');
            path = Uri.UnescapeDataString(queryStart >= 0 ? target.Substring(0, queryStart) : target);
            if (queryStart < 0 || queryStart == target.Length - 1) return;

            string[] pairs = target.Substring(queryStart + 1).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                if (string.IsNullOrEmpty(pairs[i])) continue;
                int separator = pairs[i].IndexOf('=');
                string key = separator >= 0 ? pairs[i].Substring(0, separator) : pairs[i];
                string value = separator >= 0 ? pairs[i].Substring(separator + 1) : string.Empty;
                query[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                    Uri.UnescapeDataString(value.Replace('+', ' '));
            }
        }

        static void WriteResponse(Stream stream, Response response)
        {
            byte[] body = Encoding.UTF8.GetBytes(response.body ?? string.Empty);
            string reason = ReasonPhrase(response.statusCode);
            string headers =
                $"HTTP/1.1 {response.statusCode} {reason}\r\n" +
                $"Content-Type: {response.contentType ?? "application/json; charset=utf-8"}\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Headers: Content-Type\r\n" +
                "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        internal static Response Error(int statusCode, string message)
        {
            return Response.Json(statusCode, JsonStringError(message));
        }

        static string JsonStringError(string message)
        {
            if (message == null) message = "Unknown error";
            return "{\"ok\":false,\"error\":\"" + EscapeJson(message) + "\"}";
        }

        static string EscapeJson(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 32) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        static string ReasonPhrase(int statusCode)
        {
            switch (statusCode)
            {
                case 200: return "OK";
                case 202: return "Accepted";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 404: return "Not Found";
                case 409: return "Conflict";
                case 413: return "Payload Too Large";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                case 504: return "Gateway Timeout";
                default: return "Response";
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); }
            catch { }
            _listener = null;

            if (_acceptThread != null && _acceptThread.IsAlive)
                _acceptThread.Join(250);
            _acceptThread = null;
        }
    }
}
