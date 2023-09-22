public enum MessageType {
    Info,
    Debug,
    Warning,
    Error
}

class Logger {
    private readonly string _originator;
    private static readonly string _logFile = Path.Combine(Environment.CurrentDirectory, "Anki2OneNote.log");
    private static bool _initiated = false;

    public static string LogFile { get => _logFile; }

    public Logger(string originator) {
        _originator = originator;
        if (!_initiated) {
            var block = new string('#', 32);
            var nl = Environment.NewLine;
            File.AppendAllText(_logFile, $"{nl}{block} New session {block}{nl}{nl}");
            _initiated = true;
        }
    }

    public void Log(string message, MessageType messageType = MessageType.Info) {
        var line = $"[{DateTime.Now.ToString("s")} {messageType} {_originator}] {message} {Environment.NewLine}";
        File.AppendAllText(_logFile, line);
    }
}