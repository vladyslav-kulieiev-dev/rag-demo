# RagDemo

Chat with your own documents. Upload PDFs, Word, HTML, Markdown or text files, ask a question, and get an answer grounded in those files, with numbered links to the sources it came from.

Built with ASP.NET Core 10 Minimal APIs and Google Cloud Vertex AI Search. Vertex AI Search handles chunking, embeddings, retrieval and grounded answer generation, so this app has no vector database or embedding code of its own.

<!-- Screenshot or GIF of the page goes here: docs/demo.gif -->

## Architecture

```
Browser (wwwroot/index.html)
        │
        ▼
ASP.NET Core 10 Minimal API  (src/RagDemo.Api)
  ├─ POST /api/documents   upload → Cloud Storage → import into Vertex AI Search
  ├─ GET  /api/documents   list indexed documents
  ├─ POST /api/ask         Answer API → answer + citations
  └─ GET  /health
        │
        ├──► Cloud Storage bucket          (original files: gs://bucket/uploads/{guid}-{name})
        └──► Vertex AI Search              (data store + app, Enterprise edition, advanced LLM features)
```

How a question is answered:

1. `POST /api/ask` calls the Answer API on the app's `default_serving_config` with citations turned on.
2. Vertex AI Search finds the relevant chunks and writes an answer from them only.
3. `CitationMapper` turns the response into `{ answer, found, citations[] }`, inserting `[1]`, `[2]` markers into the text and mapping each one to a document title, URI and snippet.
4. When Vertex AI Search skips the question (for example, nothing relevant in the documents), the API returns *"No answer was found in your documents."* instead of a made-up answer.

## Project layout

```
src/RagDemo.Api/
  Program.cs                     DI, options, rate limiter, error handling, endpoints, static files
  GcpClients.cs                  Google clients (created on first use, regional endpoint from settings)
  GcpErrorHandler.cs             Google API errors → 502/503 ProblemDetails
  RateLimits.cs                  Fixed-window rate limit per client IP
  Options/                       GcpSettings (validated at startup), UploadSettings
  Documents/                     Upload + list endpoints, Cloud Storage + import, file validation
  Ask/                           Ask endpoint, Answer API call, CitationMapper, request/response records
  wwwroot/index.html             Upload form, document list, question box, answer with sources
tests/RagDemo.Tests/             xUnit: citation mapping, file validation, endpoint behaviour with fakes
Dockerfile
```

## Google Cloud setup

You need a Google Cloud project with billing turned on. Set a budget alert first: the Answer API is billed per query.

1. **Turn on the APIs**

   ```sh
   gcloud services enable aiplatform.googleapis.com storage.googleapis.com discoveryengine.googleapis.com
   ```

2. **Create a bucket** for the uploaded files:

   ```sh
   gcloud storage buckets create gs://YOUR_BUCKET --location=US --uniform-bucket-level-access
   ```

3. **Create a Vertex AI Search data store** (console → AI Applications → Data stores → Create):
   - Source: Cloud Storage, **unstructured documents** (you can point it at an empty folder of the bucket; the app imports files itself).
   - Location: `global` (or `us` / `eu`; the app picks the matching regional endpoint).
   - Document processing: turn on **chunking** (layout parser works well for PDFs).
   - Note the **data store ID**.

4. **Create a search app** (AI Applications → Apps → Create → Custom search):
   - Edition: **Enterprise**, with **Advanced LLM features** turned on (the Answer API needs both).
   - Connect it to the data store from step 3.
   - Note the **app ID** (this is the `EngineId` setting).

5. **Service account for Cloud Run** (optional, only if you deploy):

   ```sh
   gcloud iam service-accounts create rag-api
   gcloud projects add-iam-policy-binding YOUR_PROJECT \
     --member=serviceAccount:rag-api@YOUR_PROJECT.iam.gserviceaccount.com --role=roles/discoveryengine.editor
   gcloud storage buckets add-iam-policy-binding gs://YOUR_BUCKET \
     --member=serviceAccount:rag-api@YOUR_PROJECT.iam.gserviceaccount.com --role=roles/storage.objectAdmin
   ```

## Run locally

Requirements: .NET 10 SDK and the gcloud CLI.

1. **Sign in with Application Default Credentials.** No key files are used.

   ```sh
   gcloud auth application-default login
   gcloud auth application-default set-quota-project YOUR_PROJECT
   ```

   The quota project matters: Vertex AI Search rejects user credentials that don't have one.

2. **Set the configuration** with user secrets (kept outside the repo):

   ```sh
   cd src/RagDemo.Api
   dotnet user-secrets init
   dotnet user-secrets set "Gcp:ProjectId"   "YOUR_PROJECT"
   dotnet user-secrets set "Gcp:Location"    "global"
   dotnet user-secrets set "Gcp:DataStoreId" "YOUR_DATA_STORE_ID"
   dotnet user-secrets set "Gcp:EngineId"    "YOUR_APP_ID"
   dotnet user-secrets set "Gcp:Bucket"      "YOUR_BUCKET"
   dotnet user-secrets set "Upload:ApiKey"   "pick-a-long-random-string"   # optional
   ```

   Environment variables work too (`Gcp__ProjectId`, `Gcp__Bucket`, …). If a required setting is missing, the app stops at startup with an error that names it.

3. **Run it**

   ```sh
   dotnet run --project src/RagDemo.Api
   ```

   Open the URL it prints. The OpenAPI document is at `/openapi/v1.json` in Development.

4. **Try it with curl**

   ```sh
   curl -F "file=@sample.pdf" -H "X-Api-Key: YOUR_KEY" http://localhost:5034/api/documents   # 202 + operation name
   curl http://localhost:5034/api/documents                                                  # after a few minutes
   curl -H "Content-Type: application/json" -d '{"question":"What is the refund policy?"}' http://localhost:5034/api/ask
   ```

   Indexing takes a few minutes after an upload. The document then shows up in the list and in the data store in the console.

## Tests

```sh
dotnet test
```

The tests don't call Google Cloud. They cover citation mapping (including UTF-8 byte offsets), file validation and safe names, startup validation, the API key check, the "no answer" path, and the rate limiter.

## Deploy to Cloud Run (optional)

```sh
gcloud run deploy ragdemo --source . --region us-central1 \
  --service-account rag-api@YOUR_PROJECT.iam.gserviceaccount.com \
  --allow-unauthenticated --max-instances 2 \
  --set-env-vars "Gcp__ProjectId=YOUR_PROJECT,Gcp__Location=global,Gcp__DataStoreId=YOUR_DATA_STORE_ID,Gcp__EngineId=YOUR_APP_ID,Gcp__Bucket=YOUR_BUCKET,ASPNETCORE_FORWARDEDHEADERS_ENABLED=true" \
  --set-secrets "Upload__ApiKey=ragdemo-upload-key:latest"
```

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the rate limiter see the caller's IP instead of Cloud Run's proxy. Store the upload key in Secret Manager rather than in an env var.

## Cost protection

- 10 requests per minute per IP across `/api/ask` and `/api/documents` (`RateLimit:PermitLimit`, `RateLimit:WindowSeconds`). Extra requests get `429`.
- Optional `X-Api-Key` on uploads (`Upload:ApiKey`) so strangers can't fill the index.
- Uploads limited to 10 MB and to PDF, TXT, MD, HTML and DOCX.
- Budget alerts and API quotas in the Google Cloud project; `--max-instances` on Cloud Run.

## Errors

All errors are [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) JSON:

| Status | When |
| --- | --- |
| 400 | Bad input: empty or long question, wrong file type, file too big |
| 401 | Upload without a valid `X-Api-Key` (when one is configured) |
| 429 | Rate limit reached |
| 502 | A Google Cloud call failed (the full error goes to the log) |
| 503 | No Google Cloud credentials found |

## Next steps

- Metadata filters (per collection or folder)
- Multi-turn conversations with Answer API sessions
- Streaming answers
- Sign-in (for example Google sign-in) and per-user documents
- An LLM router with tools
- Ingestion status tracking for import operations
- An evaluation set: questions → expected sources
- GitHub Actions CI
- Terraform for the Google Cloud setup
