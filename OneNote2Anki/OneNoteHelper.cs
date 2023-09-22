using Azure.Core;
using Azure.Identity;
using ExtensionMethods;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using OneNote2Anki;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Xml.Linq;


class OneNoteHelper {
    // Settings object
    private readonly Settings _settings;
    // User auth token credential
    private readonly DeviceCodeCredential _deviceCodeCredential;
    // Client configured with user authentication
    private readonly GraphServiceClient _graphClient;
    // Http client for calls not supported by the graph client
    private readonly HttpClient _httpClient;
    // Path to media directory (for storing images)
    private readonly string _mediaDirectory;

    // Initialize MS Graph for user authentication
    public OneNoteHelper(Settings settings, string mediaDirectory, Func<DeviceCodeInfo, CancellationToken, Task> deviceCodePrompt) {
        _settings = settings;
        _mediaDirectory = mediaDirectory;

        // Authenticate a new graph client with given scopes
        var options = new DeviceCodeCredentialOptions {
            ClientId = _settings.ClientId,
            TenantId = _settings.TenantId,
            DeviceCodeCallback = deviceCodePrompt,
        };

        _deviceCodeCredential = new DeviceCodeCredential(options);

        _graphClient = new GraphServiceClient(_deviceCodeCredential, _settings.GraphUserScopes);

        // Request access token and create http client
        var context = new TokenRequestContext(_settings.GraphUserScopes);
        var response = _deviceCodeCredential.GetToken(context);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.Token);
    }

    public async Task<List<Flashcard>> ExtractFlashcards() {
        var flashcards = new List<Flashcard>();

        var pagesResponse = await _graphClient.Me.Onenote.Pages.GetAsync((requestConfiguration) => {
            requestConfiguration.QueryParameters.Select = new string[] { "title", "id" };
        });
        _ = pagesResponse ?? throw new NullReferenceException("The pages response from MS Graph is null.");

        var pages = new List<OnenotePage>();
        var pageIterator = PageIterator<OnenotePage, OnenotePageCollectionResponse>.CreatePageIterator(_graphClient, pagesResponse, (page) => { pages.Add(page); return true; });

        await pageIterator.IterateAsync();

        await Task.WhenAll(pages.Select(async (page) => {
            if (page.Title == null) return;

            var deckPattern = @"(?<deck>[^>]+)(?<!\s+)(\s*>\s*(?<deck>[^>]+)(?<!\s+))+";
            var match = Regex.Match(page.Title, deckPattern);

            if (!match.Success) return;
            var deck = match.Groups["deck"].Captures.Select(capture => capture.Value).ToArray();

            var contentResponse = await _graphClient.Me.Onenote.Pages[page.Id].Content.GetAsync();
            _ = contentResponse ?? throw new NullReferenceException("The content response from MS Graph is null.");
            var html = XDocument.Load(contentResponse);

            var paragraphs = from el in html.Element("html")?.Element("body")?.Elements("div").Elements("p") where el.Value.Contains(':') select el;

            await Task.WhenAll(paragraphs.Select(async (paragraph) => {
                var cards = paragraph.SplitElement();
                if (cards == null) return;

                var xmlFront = await ExtractImages(cards.Value.Item1);
                var xmlBack = await ExtractImages(cards.Value.Item2);

                var siblings = new List<XElement>();
                foreach (var sibling in paragraph.ElementsAfterSelf()) {
                    if (!Regex.IsMatch(sibling.Name.LocalName, "br|img|ul|ol|table")) break;
                    siblings.Add(sibling);
                };

                await Task.WhenAll(siblings.Select(async (sibling) => {
                    xmlBack += await ExtractImages(sibling);
                }));

                var xmlCard = new Flashcard(xmlFront, xmlBack, deck);
                flashcards.Add(xmlCard);
            }));
        }));

        return flashcards;
    }

    // Creates a copy of the original
    // Swaps all image src attributes of the copy
    // Serializes the copy into XML and returns the result
    private async Task<string> ExtractImages(XElement original) {
        var copy = new XElement(original);
        var images = from el in copy.DescendantsAndSelf() where el.Name.LocalName == "img" select el;

        foreach (var image in images) {
            var uri = (image.Attribute("data-fullres-src") ?? image.Attribute("src"))?.Value;
            var type = (image.Attribute("data-fullres-src-type") ?? image.Attribute("data-src-type"))?.Value;

            if (uri == null || type == null) continue;

            var token = Regex.Match(uri, "/(?<token>[^/]*)/", RegexOptions.RightToLeft).Groups["token"].Value;
            var extension = Regex.Match(type, "[^/]+$").Value;
            var file = $"{token}.{extension}";
            var path = Path.Combine(_mediaDirectory, file);

            // Download and save the image if not foud in cache
            if (!File.Exists(path)) {
                Console.WriteLine("Downloading image {0}", file);
                var imageBytes = await _httpClient.GetByteArrayAsync(uri);
                await File.WriteAllBytesAsync(path, imageBytes);
            }

            image.Attribute("src")?.SetValue(file);
            image.Attribute("data-src-type")?.Remove();
            image.Attribute("data-fullres-src")?.Remove();
            image.Attribute("data-fullres-src-type")?.Remove();
        }

        return copy.GetReader().ReadOuterXml();
    }
}