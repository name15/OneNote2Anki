using OneNote2Anki;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

public class AnkiFlashcardOptions {
    public bool allowDuplicate { get; set; }
    public string? duplicateScope { get; set; }
}

public class AnkiFlashcard {
    public string deckName { get; set; }
    public string modelName { get; set; }
    public Dictionary<string, string> fields { get; set; }
    public string[]? tags { get; set; }
    public AnkiFlashcardOptions? options { get; set; }

    public AnkiFlashcard(Flashcard flashcard) {
        deckName = "OneNote2Anki::" + string.Join("::", flashcard.Deck);
        modelName = "Basic";
        fields = new Dictionary<string, string>();
        fields["Front"] = ToUtf8(flashcard.Front);
        fields["Back"] = ToUtf8(flashcard.Back);
        options = new AnkiFlashcardOptions();
        options.allowDuplicate = true;
        options.duplicateScope = "OneNote2Anki";
        tags = new string[] { "OneNote2Anki" };
    }

    public string ToUtf8(string text) {
        byte[] bytes = Encoding.Default.GetBytes(text);
        return Encoding.UTF8.GetString(bytes);
    }

    public static explicit operator AnkiFlashcard(Flashcard flashcard) {
        return new AnkiFlashcard(flashcard);
    }

    public string Serialize() {
        var options = new JsonSerializerOptions {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };
        return JsonSerializer.Serialize(this, options);
    }
}

class AnkiHelper {
    private readonly Settings _settings;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public class AnkiResponse<T> {
        public T? result { get; set; }
        public string? error { get; set; } // You can define a more specific type for "error" if needed
    }
    public class AnkiPermissionResult {
        public string? permission { get; set; }
    }

    public AnkiHelper(Settings settings) {
        _settings = settings;
        _httpClient = new HttpClient();
        _logger = new Logger("AnkiHelper");

        // Test connection & request permission to use the API
        RequestPermissionAsync().Wait();
    }

    private async Task<T?> PostAsync<T>(string requestJson) {
        _logger.Log($"Request: {requestJson}");

        var requestContent = new StringContent(requestJson);
        var response = await _httpClient.PostAsync(_settings.AnkiConnectUri, requestContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        _logger.Log($"Response: {responseJson}");

        var obj = JsonSerializer.Deserialize<AnkiResponse<T>>(responseJson);
        if (obj == null) throw new Exception($"Could not deserialize AnkiConnect response: {responseJson}");
        if (obj.error != null) throw new Exception($"AnkiConnect API responded with an error: {obj.error}");
        return obj.result;
    }

    public Exception NullResponse() {
        return new NullReferenceException("AnkiConnect returned a null response, which was unexpected.");
    }

    private async Task RequestPermissionAsync() {
        var result = await PostAsync<AnkiPermissionResult>(@"{""action"": ""requestPermission"", ""version"": 6}");

        if (result?.permission == "denied")
            throw new Exception("Permission to access AnkiConnect API was denied.");
    }

    public async Task<string> GetMediaDirectoryAsync() {
        return await PostAsync<string>(@"{""action"": ""getMediaDirPath"", ""version"": 6}") ?? throw NullResponse();
    }

    public async Task<IEnumerable<ulong>> FindNotesAsync(string query) {
        var request = $"{{\"action\": \"findNotes\", \"version\": 6, \"params\": {{\"query\": \"{query}\"}}}}";
        return await PostAsync<IEnumerable<ulong>>(request) ?? throw NullResponse();
    }

    public async Task DeleteNotesAsync(IEnumerable<ulong> notes) {
        var request = $"{{\"action\": \"deleteNotes\", \"version\": 6, \"params\": {{\"notes\": [{string.Join(", ", notes)}]}}}}";
        await PostAsync<object>(request);
    }

    public async Task<List<ulong?>> AddNotesAsync(IEnumerable<AnkiFlashcard> notes) {
        var notesJson = notes.Select(note => note.Serialize());
        var request = $"{{\"action\": \"addNotes\", \"version\": 6, \"params\": {{\"notes\": [{string.Join(", ", notesJson)}]}}}}";
        var result = await PostAsync<List<ulong?>>(request);

        var duplicates = result?.Count(nid => nid == null);
        if (duplicates > 0)
            throw new Exception($"Could nod add {duplicates} notes because of duplicate front fields.");

        return result ?? throw NullResponse();
    }

    public async Task<List<string>> GetDeckNamesAsync() {
        return await PostAsync<List<string>>(@"{""action"": ""deckNames"", ""version"": 6}") ?? throw NullResponse();
    }

    public async Task CreateDeckAsync(string deckName) {
        await PostAsync<ulong>($"{{\"action\": \"createDeck\", \"version\": 6, \"params\": {{\"deck\": \"{deckName}\"}}}}");
    }
}