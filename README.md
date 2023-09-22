# OneNote2Anki
A command-line tool for converting OneNote pages to Anki flashcards.

# Features
- The program extracts all OneNote pages whose title matches a given pattern.
- It detects flashcards with the format `Front card: Back card`. The back card can contain rich text, images, lists and/or tables.
- The user can review the collected cards in a web browser.
- The flashcards are automatically imported into Anki into their corresponding decks.

# Major dependencies
- `Microsoft Graph` is used for extracting informatino from OneNote.
- `AnkiConnect` is used for communicating with Anki.
- etc.

# Contents
- The entry point of the program is in the file `Program.cs`.
- Class `Program` is the entry point of the program and provides the CLI.
- Class `OneNoteHelper` makes calls to the Microsft Graph API. The `ExtractFlashcards` method is responsible for extracting flashcards from OneNote pages. Here's how it works:
- Class `AnkiHelper` makes calls to the AnkiConnect API.
- Class `WebServer` serves a static webpage for reviewing the collected flashcards.
- Class `XmlExtension` is used to split XML elements into two at the location where the colon character (':') is found.
- Class `Logger` writes messages to a log file.

# Implementation details
- The program first asks for permission to use AnkiConnect.
- Then it retrieves the Anki media directory using AnkiHelper.
- It asks for permission to use the MS Graph API (using a device code credential) and recieves a OAuth token.
- It iterates over all OneNote pages.
  - Only the pages whose titles have the format `foo > bar > baz` are considered. All flashcards found are stored in the deck foo::bar::baz.
  - All XML paragraphs that contain a colon are split front and back cards.
  - Any tables, lists or other rich text content following the paragraph is added to the back card.
  - Full resolution images are cached in the Anki media folder with the same name as the original.
- If the user chooses to review the collected flashcards, a webserver is started. The TCP server handles the following requests:
  - "GET / HTTP/1.1": The server responds with a HTML document, generated from the extracted flashcards.
  - "GET /{imageUri} HTTP/1.1": All media requests are redirected to the Anki media folder.
- If the user chooses to save the flashcards in Anki, the following steps are executed:
  - All Anki notes with the tag "OneNote2Anki" are deleted
  - All missing decks are created
  - The serialized flashcards are sent using one request with the "allowDuplicates" option on.
- That's all folks!