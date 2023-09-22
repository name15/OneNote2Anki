using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

public class WebServer {
    private readonly TcpListener _tcpListener;
    private bool _running = false;
    private string _htmlMessage;
    private string _resourcesDirectory;
    private Logger _logger;

    public WebServer(string ipAddress, int port) {
        _tcpListener = new TcpListener(IPAddress.Parse(ipAddress), port);
        _htmlMessage = "";
        _resourcesDirectory = Environment.CurrentDirectory;
        _logger = new Logger("WebServer");
    }

    public WebServer WithHtml(string html) {
        _htmlMessage = html;
        return this;
    }

    public WebServer WithResources(string directory) {
        _resourcesDirectory = directory;
        return this;
    }

    public void Start() {
        _running = true;
        new Thread(() => {
            while (_running) {
                _tcpListener.Start();
                if (_tcpListener.Pending()) HandleRequest();
                else Thread.Sleep(100);
            }
        }).Start();
        _logger.Log($"Listening for connections on http://{_tcpListener.LocalEndpoint}");
    }

    public void Pause() {
        _running = false;
        _logger.Log("[WebServer] Server stopped.");
    }

    private void HandleRequest() {
        TcpClient client = _tcpListener.AcceptTcpClient();
        var stream = client.GetStream();

        // Read incomming messages
        var buffer = new byte[1024];
        var length = stream.Read(buffer, 0, buffer.Length);
        var request = Encoding.UTF8.GetString(buffer, 0, length);
        var match = Regex.Match(request, "GET\\s(?<uri>\\S*)\\sHTTP/1.1", RegexOptions.Multiline);

        if (!match.Success) {
            Console.WriteLine("[WebServer] Could not parse the request\n{0}\n\n", request);
            return;
        }

        // The requested resource
        var uri = match.Groups["uri"].Value;

        if (uri == "/") {
            WriteResponse(stream, Encoding.UTF8.GetBytes(_htmlMessage), "text/html; charset=UTF-8");
            client.Close();
            return;
        }

        // Redirect the URI to the resources directory
        var filePath = Path.Combine(_resourcesDirectory, uri.Substring(1));
        if (File.Exists(filePath)) {
            byte[] fileContent = File.ReadAllBytes(filePath);
            var extension = Path.GetExtension(filePath);
            var mimeType = Regex.IsMatch(extension, "jp(e)?g|png|svg|gif") ? "image"
                : Regex.IsMatch(extension, "mp3|ogg|wav(e)?") ? "audio"
                : Regex.IsMatch(extension, "webm|mp4") ? "video"
                : "text"; // TODO: Add more types
            WriteResponse(stream, fileContent, mimeType + "/" + extension);
        } else {
            _logger.Log($"Could not find resource '{uri}'.");
        }

        client.Close();
    }

    private void WriteResponse(NetworkStream stream, byte[] content, string mime_type) {
        var headerText = "HTTP/1.1 200 OK\r\n"
            + $"Content-Type: {mime_type}\r\n"
            + $"Content-Length: {content.Length}\r\n\n";

        byte[] header = Encoding.UTF8.GetBytes(headerText.ToString());

        stream.Write(header, 0, header.Length);
        stream.Write(content, 0, content.Length);
    }
}