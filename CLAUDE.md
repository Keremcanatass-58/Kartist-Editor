# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Kartist is an ASP.NET Core MVC web app (AI-assisted design studio + social network for designers). Backend is C# on .NET 8 with Dapper over SQL Server; frontend is server-rendered Razor views with Fabric.js for the canvas editor. The UI and data model are written in Turkish (table/column names, controller actions, view files) — preserve that language when adding code.

## Commands

```bash
dotnet restore Kartist.sln
dotnet build Kartist.sln --configuration Release
dotnet run                                # local dev (uses appsettings.Development.json)
dotnet publish Kartist.csproj --configuration Release --output ./publish
```

There are **no test projects** in this repo — the CI workflow at `.github/workflows/dotnet.yml` only runs `dotnet test` if a `*.Tests.csproj` / `*.Test.csproj` is found. Do not claim tests passed unless you add them.

The README says ".NET 9.0 SDK" but the real target is `net8.0` (see `Kartist.csproj` and the CI `setup-dotnet` step). Use the .NET 8 SDK.

## Configuration

- `appsettings.json` ships with **placeholder** secrets (`YOUR_…`). Real secrets live in `appsettings.Development.json` (gitignored) locally, and are injected in CI from the `APPSETTINGS_PRODUCTION_JSON` GitHub secret (see `.github/workflows/dotnet.yml`). Never commit real values into `appsettings.json`.
- Key switches read in `Program.cs`:
  - `Database:AutoSchema` — **code default is `true`** (`Program.cs:81`); production must set this to `false` explicitly in `appsettings.json` if you don't want `Kartist.Data.DatabaseInitializer.Initialize` running ALTER/CREATE statements at startup. Leave it on locally so new columns/tables/badges are created without hand-written migrations.
  - `Razor:RuntimeCompilation` — code default is `true` (`Program.cs:35`), but only honored in Development; allows editing `.cshtml` without rebuild.
  - `Security:RateLimit*` — consumed (defaults `100` reqs / `60` s) but `app.UseRateLimiting(...)` is commented out at `Program.cs:101`. Uncomment that single line to switch the existing token-bucket middleware on; no other wiring needed.
  - `Ai:*` — selects AI providers (see below). `OpenAI:ApiKey` / `Groq:ApiKey` are read out-of-band by the services.

## Architecture

### Request pipeline (`Program.cs`)
1. `AutoValidateAntiforgeryTokenAttribute` is added globally — every non-GET MVC action requires a CSRF token. The `/api/deploy` minimal endpoint opts out with `.DisableAntiforgery()`.
2. `SecurityHeadersMiddleware` writes a custom CSP; it **skips** static-file paths (`/lib/`, `/css/`, `/js/`, `/uploads/`, `/img/`) and OAuth callbacks. When adding a new third-party CDN or API origin, update the `scriptSrc` / `connectSrc` / `frame-src` / `img-src` lists there or the browser will silently block it. The CSP intentionally retains `'unsafe-inline'` and `'unsafe-eval'` because Fabric.js + the Tailwind CDN require them — this is a known tradeoff (nonce-based CSP is on the Sprint 5 list); do not strip those tokens without first migrating every inline `<script>`/`<style>` to a nonce.
3. Two cookie schemes are registered: `KartistCookie` (primary auth, 30-day) and `External` (short-lived Google OAuth handoff). Google auth is only wired up when both `Authentication:Google:ClientId` and `ClientSecret` are set — guard new OAuth-dependent code accordingly.
4. SignalR hubs are mapped at `/adminHub`, `/notificationHub`, and `/notifHub` (alias). `NotificationHub` maintains a static `UserConnections` dictionary keyed by email (the `Identity.Name` claim).

### Controllers
- `Controllers/Base/BaseController.cs` exposes `CurrentUserId` (read from the `"Id"` claim) and `CurrentUserEmail`. **New controllers that need the logged-in user should inherit from this**, not re-implement the claim lookup. `SocialController` is the reference example; `HomeController` and `AccountController` pre-date it and still do their own `User.Claims.FirstOrDefault(...)` lookups — match the BaseController pattern in new code.
- Controllers are large (Home/Social/Account all ~1k lines). They mix Dapper SQL, HTML responses, and JSON endpoints in the same class. When adding social/feed logic, prefer extending `Services/Business/SocialService.cs` + `Data/Repositories/SocialRepository.cs` rather than inlining more Dapper in `SocialController`.
- `AdminController` follows a strict pattern enforced in commit `2fcb18c`: every state-changing action is `[HttpPost]` + `[ValidateAntiForgeryToken]` and gated by `AdminYetkili()` (which is **fail-closed** — DB errors return `false`). Do not add `HttpGet` admin mutations or skip the guard, even for "internal" endpoints.

### Data access
- Dapper only — no EF. Queries are inline strings using parameterized `@name` placeholders. Never concatenate user input into SQL; use Dapper parameters. `InputValidator.IsValidInput` is a blunt keyword blacklist and is **not** a substitute for parameterization.
- `Data/DatabaseInitializer.cs` is the authoritative place for schema changes: add a new `IF NOT EXISTS ... ALTER TABLE / CREATE TABLE` block there (the file is idempotent). Seed data for `Rozetler` (badges) is also inserted here.
- Turkish schema: `Kullanicilar` (users), `Sablonlar` (templates), `SosyalGonderiler` (posts), `SosyalBegeniler` (likes), `SosyalYorumlar` (comments), `Takipciler` (follows), `Hikayeler` (stories), `DirektMesajlar` (DMs), `Bildirimler` (notifications), `Rozetler` (badges), `KullaniciXP`. Keep new identifiers in the same language.

### AI services
Three providers with a fallback chain configured via `Ai:ImageProvider` / `Ai:PromptProvider`:
- `IAiImageService` → OpenAI `gpt-image-1` → Pollinations (`image.pollinations.ai/prompt`) → keyword-based stock fallback. Adding a new provider means extending `AiImageService` and keeping the fallback order intact so the UI never gets a hard failure.
- `IAiPromptService` → Groq (`llama-3.3-70b-versatile`) or OpenAI chat.
- `AiModerationService` gates user-generated text before posting/commenting — called from `SocialService.CreatePostAsync` / `CreateCommentAsync` / `EditPostAsync`. New UGC entry points should call it too.
- Health probe: `GET /api/health/ai` (used by the CI deploy job).

### Deployment endpoint (important, irreversible)
`POST /api/deploy` in `Program.cs` accepts a zip upload, writes `update.bat`, launches it in a detached `cmd.exe`, and that script takes the site offline (copies `offline_template.htm` → `app_offline.htm`), extracts the new build over the current directory, and restores `web.config` + `appsettings.json` from `.bak` copies. Auth is HMAC-SHA256 only: the request must carry `X-Kartist-Timestamp` and `X-Kartist-Signature` headers, where the signature is HMAC(`Deployment:Secret`, timestamp) compared in constant time, with the timestamp falling inside `Deployment:SignatureToleranceSeconds` (floor 60s). The earlier hardcoded `secret=...` form-field fallback was removed in commit `0061ec3` — do not reintroduce it.

The CI workflow in `.github/workflows/dotnet.yml` computes the signature with `openssl dgst -sha256 -hmac "$DEPLOY_SECRET"`. Do not change the signature scheme without updating that workflow in the same commit, and do not invoke this endpoint manually against production without a very good reason.

There is also an unauthenticated `GET /api/debug/deploy-info` that returns the SHA-256 hash and length of `Deployment:Secret` plus a test HMAC, used for diagnosing CI signature mismatches. **It is marked `// TEMPORARY DEBUG ENDPOINT - REMOVE AFTER FIXING DEPLOY` in `Program.cs:141` and should be deleted once the deploy pipeline is stable.** It leaks enough material to brute-force a short secret — keep `Deployment:Secret` long/high-entropy, do not add new fields that expose the raw value, and prefer removing the endpoint over extending it.

## Conventions

- Turkish identifiers (actions like `Giris`, `Kayit`, `Profil`; properties like `KullaniciId`, `OlusturmaTarihi`). Match surrounding code rather than translating.
- Passwords in **both** `Kullanicilar` and `Yoneticiler` go through `BCrypt.Net` — never store plaintext, never re-introduce the pre-migration plaintext fields. The plaintext fallback was fully removed in commits `e26421c` (users) and `ce92c17`/follow-ups (admins); rows that never migrated can no longer authenticate.
- User-generated HTML must be run through `Kartist.Helpers.InputValidator.SanitizeHtml` before storage (strips `<script>`, `<iframe>`, `on*=` handlers, `javascript:` URLs, and all remaining tags).
- **Image uploads must go through `Kartist.Helpers.FileUploadValidator.TryValidateImage`** (added Sprint 4, commit `da603fd`). It checks magic bytes against a JPEG/PNG/GIF/WEBP table and returns a server-chosen extension — never derive the saved filename from `IFormFile.ContentType` or `FileName`. The six existing upload sites (post, before/after, story, avatar legacy, avatar, cover) are the reference; new endpoints must follow the same shape.
- CSRF is global-on; any new form/AJAX POST must include the antiforgery token (`@Html.AntiForgeryToken()` is emitted in `Views/Shared/_Layout.cshtml`).
- Uploaded files land under `wwwroot/uploads/` (gitignored). The social flow writes to `wwwroot/uploads/social/` with GUID filenames. Static responses for `/uploads/` are sandboxed and `nosniff`-tagged (commit `15244ca`) — preserve that when touching the static-file pipeline.

## Out-of-repo things to know

- `_archive/` holds old publish bundles, migration scripts (`migration.sql`), and experiment artifacts — treat as read-only historical reference, not buildable code. It's excluded from the project via `DefaultItemExcludes` in the csproj. `manual-publish/` is the same idea for hand-built deploys.
- `docs/` contains sprint reports and the vize project report (Turkish academic submission). `DEVAM_NOTLARI.md` at repo root holds Sprint 4 security-audit notes (jury-prep internal); read it before changing any of the Sprint 4 hardening commits referenced above.
- The `Iyzipay 2.1.67` package is referenced in `Kartist.csproj` but **not yet wired up** — it ships as a forward-looking dependency for the planned template marketplace (see README "Sprint 5+"). Do not assume payment flows exist; there are no `Iyzipay` `using` statements in `Controllers/` or `Services/` today.
- Production site: `https://kartistt.com.tr`. Health checks in CI hit `/` and `/api/health/ai` there.
