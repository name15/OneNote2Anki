using Azure.Identity;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OneNote2Anki {
    public class Flashcard {
        public string Front;
        public string Back;
        public string[] Deck;

        public Flashcard(string front, string back, string[] deck) {
            Front = front;
            Back = back;
            Deck = deck;
        }
    }
}

class Program {
    private static readonly Logger _logger = new("Main");

    public static async Task Main() {
        Console.Write(@"Welcome to the OneNote to Anki Flashcards converter!

   ___                __      _         ____      _         _    _ 
  /___\_ __   ___  /\ \ \___ | |_ ___  |___ \    /_\  _ __ | | _(_)
 //  // '_ \ / _ \/  \/ / _ \| __/ _ \   __) |  //_\\| '_ \| |/ / |
/ \_//| | | |  __/ /\  / (_) | ||  __/  / __/  /  _  \ | | |   <| |
\___/ |_| |_|\___\_\ \/ \___/ \__\___| |_____| \_/ \_/_| |_|_|\_\_|


");

        // Read the contents of 'appsettings.json' and use them
        // to authorize and customize the application
        var settings = Settings.LoadSettings();

        if (!Confirm("Is the AnkiConnect plugin installed?")) {
            Console.Write("Please open Anki on your computer and go to the Tools->Add-ons menu item. Then click on Get Add-ons and paste in the code 2055492159. Press Enter to continue...");
            Console.ReadLine();
        }

        var host = new Uri(settings.AnkiConnectUri).Host;
        if (host != "localhost" && host != "127.0.0.1")
            Console.WriteLine("You will be asked to grant permission to use this app");


        // A wrapper around the AnkiConnect API that exposes only relevant methods
        var ankiHelper = TryAgain(() => new AnkiHelper(settings));
        if (ankiHelper == default) return;

        var mediaDirectory = await ankiHelper.GetMediaDirectoryAsync();

        Console.Write("\nYou need to sign in with your Microsoft account. Press Enter to continue...");
        Console.ReadLine();

        // Display the a message with instructions for authentication
        var deviceCodePrompt = (DeviceCodeInfo info, CancellationToken cancel) => {
            Console.WriteLine(info.Message);
            return Task.FromResult(0);
        };

        // A wrapper around the MS Graph API that exposes only relevant methods
        var oneNoteHelper = TryAgain(() => new OneNoteHelper(settings, mediaDirectory, deviceCodePrompt));
        if (oneNoteHelper == default) return;

        var stopwatch = Stopwatch.StartNew();
        var flashcards = await TryAgainAsync(() => oneNoteHelper.ExtractFlashcards());
        if (flashcards == null) return;
        stopwatch.Stop();

        Console.WriteLine("Collected a total of {0} flashcards in {1} ms.\n", flashcards.Count, stopwatch.ElapsedMilliseconds);

        if (Confirm("Do you want to review the collected flashcards?")) {
            var timestamp = DateTime.Now.ToString("dddd, dd MMMM yyyy hh: mm tt");
            var html = "<html><head><title>OneNote2Anki Flashcards</title><style>table {border-collapse: collapse;} td, th {border: 1px solid black; padding: 3px;}</style></head><body><table><tbody><tr><th>Deck</th><th>Front card</th><th>Back card</th></tr>\n";
            foreach (var flashcard in flashcards) {
                html += string.Format("<tr><td>{0}</td><td>{1}</td><td>{2}</td></tr>\n", string.Join("::", flashcard.Deck), flashcard.Front, flashcard.Back);
            }
            html += "</tbody></table></body></html>";

            var adress = "127.0.0.1";
            var port = 8080;
            var server = new WebServer(adress, port)
                .WithHtml(html)
                .WithResources(mediaDirectory);
            server.Start();

            Process.Start(new ProcessStartInfo {
                FileName = $"http://{adress}:{port}",
                UseShellExecute = true
            });

            Console.WriteLine();

            if (!Confirm("Do you want to import the flashcards into Anki?"))
                return;

            server.Pause();
        }

        // Clear all preveously generated flashcards
        var notes = await ankiHelper.FindNotesAsync("tag:OneNote2Anki");
        await ankiHelper.DeleteNotesAsync(notes);

        var ankiFlashcards = flashcards.Select(flashcard => (AnkiFlashcard)flashcard).ToList();

        // Create new decks if necessary
        var existingDeckNames = await ankiHelper.GetDeckNamesAsync();
        var newDeckNames = ankiFlashcards.Select(flashcard => flashcard.deckName).Except(existingDeckNames);

        await Task.WhenAll(newDeckNames.Select(async (deckName) => {
            await ankiHelper.CreateDeckAsync(deckName);
        }));

        // Finally, add new ids
        var noteIds = await TryAgainAsync(() => ankiHelper.AddNotesAsync(ankiFlashcards));
        if (noteIds == default) return;

        Console.WriteLine("All flashcards were transferred successfully!");
    }

    private static bool Confirm(string message) {
        bool? confirmed = null;
        while (confirmed == null) {
            Console.Write(message + " (yes/no) ");
            var choice = Console.ReadLine();
            if (choice != null) {
                if (Regex.IsMatch(choice, "y(es)?", RegexOptions.IgnoreCase))
                    confirmed = true;
                if (Regex.IsMatch(choice, "n(o)?", RegexOptions.IgnoreCase))
                    confirmed = false;
            }
        }
        return confirmed.Value;
    }

    private static T? TryAgain<T>(Func<T> getResult) {
        try {
            return getResult();
        } catch (Exception error) {
            _logger.Log(error.ToString(), MessageType.Error);
            Console.WriteLine("Something went wrong: {0}", error.Message);
            if (Confirm("\nDo you want to try again")) {
                return TryAgain(getResult);
            } else {
                Console.WriteLine("The log file is located in '{0}'.", Logger.LogFile);
                return default;
            }
        }
    }

    private static async Task<T?> TryAgainAsync<T>(Func<Task<T>> getResult) {
        try {
            return await getResult();
        } catch (Exception error) {
            _logger.Log(error.ToString(), MessageType.Error);
            Console.WriteLine("Something went wrong: {0}", error.Message);
            if (Confirm("\nDo you want to try again")) {
                return await TryAgainAsync(getResult);
            } else {
                Console.WriteLine("The log file is located in '{0}'.", Logger.LogFile);
                return default;
            }
        }
    }
}