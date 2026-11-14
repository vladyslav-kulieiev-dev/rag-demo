# RagDemo: document Q&A with highlighted citations

Ask a question about a set of documents and get an answer written only from them. Every claim in the answer has a numbered citation, and clicking it opens the source PDF at the cited passage, highlighted.

**Live demo: https://ragdemo-877238378331.europe-central2.run.app**

Built with ASP.NET Core 10 Minimal APIs, Google Cloud Vertex AI Search, Cloud Run and PDF.js.

## Try it

The demo holds two documents: the Polish Labour Code (*Kodeks pracy*, consolidated text) and the GDPR (Regulation (EU) 2016/679, in English). Questions can be in any language. Some to start with:

- *Ile dni urlopu wypoczynkowego przysługuje pracownikowi?* (How many days of annual leave does an employee get?)
- *Kiedy pracownik podejmujący pracę po raz pierwszy nabywa prawo do urlopu?* (When does a first-time employee earn the right to leave?)
- *How long does a company have to report a data breach?*
- *What is the maximum fine for breaking the GDPR?*

Then click a `[1]` in the answer. A question the documents can't answer, such as *What is the boiling point of mercury?*, gets "No answer was found in your documents" instead of a made-up answer.

## What it does

- **Grounded answers.** Vertex AI Search retrieves the relevant chunks and writes the answer from them only. Chunking, embeddings and retrieval are managed, so there's no vector database or embedding code here.
- **A citation for each claim.** Each `[n]` links to the chunk it came from. Hovering over it shows the sentence in that chunk that best supports this part of the answer.
- **The cited passage, highlighted in the original PDF.** The PDF opens in PDF.js at the right page, with the whole cited chunk highlighted.
- **Readable answers.** The model's Markdown (lists, bold) is rendered, and document names are shown as people would write them ("Kodeks pracy").
- **Protected costs.** Per-IP and daily limits, an upload key, and capped Cloud Run instances (see [Cost protection](#cost-protection)).

## How citations work

The Answer API returns the answer text, a list of cited chunks, and for each cited piece of the answer its start and end and the chunks it relies on. Turning that into an exact highlight in a PDF takes a few steps:

1. **Split the answer at each citation** (`CitationMapper.cs`). The positions are UTF-8 byte offsets, not character positions, so the text is sliced as bytes, never in the middle of a character such as "ą". Each piece of the answer becomes a segment with its own sources.
2. **Find the supporting sentence** (`PassageMatcher.cs`). A chunk can span several articles of a law, so "chunk 3" alone isn't precise. The chunk is split into sentences (dropping markdown headings and rejoining words hyphenated across PDF lines), and the sentence sharing the most words with that piece of the answer wins. Words are compared by their first five letters, which copes with Polish inflection: *podejmuje* and *podejmujący* both become *podej*.
3. **Line the chunk up with the PDF text** (`chunk-align.mjs`, in the browser). The chunk comes from Google's layout parser and never matches the PDF's text exactly: it has markdown, while the PDF has page headers, footnote marks and hyphenated words. Both texts are reduced to lowercase letters and digits and walked side by side. When they differ, the next 8 characters of the chunk are looked for up to 100 characters ahead in the PDF (skipping, say, a page header); if they aren't there, the chunk character is skipped instead (a heading only the chunk has). Headings repeated in a table of contents are handled by trying every place the chunk could start and penalizing jumps. On the real Labour Code (97 pages), all 1,032 letters of a cited chunk were matched in about 40 ms.
4. **Highlight it** (`source.html`). The matched pieces are cut from the PDF's own text, so PDF.js finds them exactly and highlights them in one colour. The chunk travels in the URL fragment (after `#`), so long chunks never reach the server.

## Architecture

```
Browser
  index.html        question, answer with [n] links, sources, document list
  source.html       PDF.js viewer that highlights the cited chunk (chunk-align.mjs)
        │
        ▼
ASP.NET Core 10 Minimal API  (src/RagDemo.Api, on Cloud Run)
  ├─ POST /api/ask             Answer API → answer, segments, citations with passages and chunks
  ├─ GET  /api/documents       list indexed documents
  ├─ POST /api/documents       upload (needs X-Api-Key) → Cloud Storage → import into Vertex AI Search
  ├─ GET  /api/documents/file  stream a source file from the bucket (upload folder only)
  └─ GET  /health
        │
        ├──► Cloud Storage bucket   original files: gs://bucket/uploads/{id}-{name}
        └──► Vertex AI Search       data store + app (Enterprise edition, advanced LLM features)
```

The bucket stays private: visitors get files through `/api/documents/file`, which reads them with the app's own service account.

## Project layout

```
src/RagDemo.Api/
  Program.cs                 DI, options, rate limiting, error handling, endpoints, static files
  GcpClients.cs              Google clients (created on first use, regional endpoint from settings)
  GcpErrorHandler.cs         Google API errors → 502/503 ProblemDetails
  RateLimits.cs              Per-IP limit and the daily question limit
  Options/                   GcpSettings (validated at startup), UploadSettings
  Ask/                       Ask endpoint, Answer API call, CitationMapper, PassageMatcher, contracts
  Documents/                 Upload, list and file endpoints; Cloud Storage and import; file validation; readable names
  wwwroot/
    index.html               The page
    answer-format.mjs        Markdown in answers → safe DOM data
    source.html              PDF viewer page that highlights the cited chunk
    chunk-align.mjs          Lines up a chunk with the PDF text
    pdfjs/                   PDF.js 6.5 viewer (Apache-2.0)
tests/
  RagDemo.Tests/             xUnit: citations, passage matching, names, file validation, endpoints, rate limits
  js/                        Node tests for chunk alignment and answer formatting
Dockerfile
```

## Tests

```sh
dotnet test
node --test "tests/js/*.test.mjs"   # Node 22+
```

The tests don't call Google Cloud. The .NET tests cover citation mapping and UTF-8 byte offsets, passage matching on real Labour Code text, readable names, file validation, the file endpoint's headers, startup validation, the upload key, the "no answer" path and both rate limits. The JavaScript tests cover lining up a chunk with PDF text (table of contents, page headers, hyphenation) and rendering answer Markdown.

## Cost protection

- **Per IP:** 10 requests per minute across `/api/ask` and `/api/documents` (`RateLimit:PermitLimit`, `RateLimit:WindowSeconds`).
- **Daily:** a shared question limit for all visitors (`RateLimit:DailyAskLimit`, 200 by default, 20 on the live demo, `0` turns it off). It's kept in memory, so each instance counts on its own and a restart resets it. It stops floods, but it isn't an exact daily total.
- **Uploads:** need `X-Api-Key` when `Upload:ApiKey` is set (the live demo is read-only for visitors); 10 MB at most; PDF, TXT, MD, HTML and DOCX only.
- **Cloud Run:** `--max-instances 2`.
- **Google Cloud:** budget alerts on the billing account.

## Errors

| Status | When |
| --- | --- |
| 400 | Bad input: empty or too long question, wrong file type, file too big |
| 401 | Upload without a valid `X-Api-Key` (when one is configured) |
| 404 | File not found, or outside the upload folder |
| 429 | Rate limit reached: the message says whether it's the per-minute or the daily limit |
| 502 | A Google Cloud call failed (the full error goes to the log) |
| 503 | No Google Cloud credentials found |

## Limitations

- Highlighting works for PDFs with a text layer. TXT, MD and HTML sources open without highlighting, DOCX files download, and scanned PDFs can't be highlighted.
- Passage matching compares words, so when the answer rephrases the source heavily it can pick a neighbouring sentence. The whole chunk is still highlighted.
- Each question is answered on its own; there's no conversation history yet.
- The daily limit is per instance and resets on restart (see above).

## Next steps

- An evaluation set: questions with their expected source passages, and a score for how often citations hit them
- Per-user documents: sign-in, a folder per user, and metadata filters in Vertex AI Search
- Multi-turn conversations with Answer API sessions
- Streaming answers
- Highlighting for TXT, MD and HTML sources
- Ingestion status for import operations
- GitHub Actions CI

## Third-party

The PDF viewer is [PDF.js](https://github.com/mozilla/pdf.js) 6.5 by Mozilla, bundled in `src/RagDemo.Api/wwwroot/pdfjs` under the Apache License 2.0 (see the `LICENSE` file there).
