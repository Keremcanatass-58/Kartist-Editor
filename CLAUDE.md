# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> A more detailed counterpart for Codex lives in `AGENTS.md`. The notes here are deliberately compact — read `AGENTS.md` when you need the long-form rationale for a convention, especially around security/Sprint 4–5 hardening commits.

## Project overview

Kartist is an ASP.NET Core MVC web app (AI-assisted design studio + social network for designers). Backend: C# on **.NET 8** with **Dapper** over **SQL Server**. Frontend: server-rendered Razor views with **Fabric.js** for the canvas editor. UI, URLs, controller actions, table and column names are **Turkish** — preserve that language when adding identifiers.

The user's natural-language preference is Turkish — respond to the user in Turkish.

## Commands

```powershell
dotnet restore Kartist.sln
dotnet build Kartist.sln --configuration Release
dotnet run                                                       # local dev (uses appsettings.Development.json)
dotnet publish Kartist.csproj --configuration Release --output ./publish
```

- There are **no test projects** in this repo. CI runs `dotnet test` only when a `*.Tests.csproj` / `*.Test.csproj` exists, so do not claim tests passed unless you add one.
- The csproj targets `net8.0` — use the .NET 8 SDK even though parts of the README mention 9.0.
- Local logs land in `kartist-run.{out,err}.log` and `kartist-local-7271.{out,err}.log` (gitignored).

## Configuration (Program.cs)

- `appsettings.json` contains placeholder values (`YOUR_…`). Real secrets live in `appsettings.Development.json` (gitignored) locally and are injected from the `APPSETTINGS_PRODUCTION_JSON` GitHub secret in CI. **Never commit real values into `appsettings.json`.**
- `Database:AutoSchema` — code default is `true` (`Program.cs:84`). `Kartist.Data.DatabaseInitializer.Initialize` runs idempotent `IF NOT EXISTS` ALTER/CREATE blocks at startup. Production must opt out explicitly if it doesn't want this.
- `Razor:RuntimeCompilation` — code default `true`, only honored in Development.
- `Security:RateLimit*` defaults `100`/`60`, but `app.UseRateLimiting(...)` is commented out at `Program.cs:104`. The token-bucket middleware is fully wired — just uncomment that single line to enable.
- `Ai:ImageProvider` / `Ai:PromptProvider` choose between providers; API keys are read out-of-band (`OpenAI:ApiKey`, `Groq:ApiKey`, `Gemini:ApiKey`, `Pexels:ApiKey`).
- Google OAuth is only registered when both `Authentication:Google:ClientId` and `ClientSecret` are set — guard new OAuth-dependent code the same way.

## Architecture big picture

### Request pipeline (`Program.cs`)
1. **Global CSRF**: `AutoValidateAntiforgeryTokenAttribute` is registered on every controller, so every non-GET MVC action requires a token. The `/api/deploy` minimal endpoint opts out with `.DisableAntiforgery()` because it is HMAC-authenticated.
2. **`SecurityHeadersMiddleware`** writes a custom CSP, skips static paths (`/lib/`, `/css/`, `/js/`, `/uploads/`, `/img/`) and OAuth callbacks. When adding a new third-party CDN/API origin, update the `scriptSrc` / `connectSrc` / `frame-src` / `img-src` lists or the browser will silently block requests. CSP intentionally keeps `'unsafe-inline'` and `'unsafe-eval'` because Fabric.js + Tailwind CDN need them — do not strip those without migrating every inline `<script>`/`<style>` to nonces first.
3. **Cookies**: `KartistCookie` (primary auth, 30-day, `HttpOnly` + `Secure` + `SameSite=Lax`) and `External` (10-minute Google OAuth handoff, same hardening).
4. **SignalR hubs** are mapped at `/adminHub`, `/notificationHub`, and `/notifHub` (alias). `NotificationHub` keeps a static `UserConnections` dictionary keyed by the `Identity.Name` email claim and also drives WebRTC signaling for live streaming.

### Layering — where new code should go

| Concern | Right place |
|--------|-------------|
| Logged-in user lookup | Inherit `Controllers/Base/BaseController.cs` and use `CurrentUserId` / `CurrentUserEmail`. `SocialController` is the reference; `HomeController`/`AccountController` predate it and still do their own claim lookups — match the base pattern in new code. |
| Social/feed business logic | Extend `Services/Business/SocialService.cs` + `Data/Repositories/SocialRepository.cs`, **not** more inline Dapper in `SocialController`. |
| Schema changes | Add an idempotent block to `Data/DatabaseInitializer.cs` (with `IF NOT EXISTS` / `IF COL_LENGTH IS NULL`). Badge seed data lives here too. |
| Admin mutations | Every state-changing admin action must be `[HttpPost]` + `[ValidateAntiForgeryToken]` and gated by `AdminYetkili()` (which is **fail-closed** — DB error ⇒ `false`). Established in commit `2fcb18c`; do not regress. |
| AI text/image | `Services/AiPromptService.cs` and `Services/AiImageService.cs`; both have provider chains (see below). UGC text must also flow through `AiModerationService`. |

### Data access
- **Dapper only — no EF.** Inline SQL strings with `@param` placeholders. Never concatenate user input — `InputValidator.IsValidInput` is a blunt blacklist and is **not** a substitute for parameterization.
- Turkish schema (selection): `Kullanicilar` (users), `Yoneticiler` (admins), `Sablonlar` (templates), `SosyalGonderiler` (posts), `SosyalBegeniler` (likes), `SosyalYorumlar` (comments, self-referential `UstYorumId` for replies), `Takipciler` (follows), `Hikayeler` (24h stories), `DirektMesajlar` (DMs), `Bildirimler` (notifications), `Rozetler` + `KullaniciRozetleri` (badges), `KullaniciXP` (XP ledger), `GunlukGorevler` (daily quests), `IkiFactorKodlari` (2FA OTPs), `GirisLoglari` (login audit), `Hashtagler`, `CanliYayinlar` (live streams).

### AI provider chains
- **Image** (`IAiImageService`): OpenAI `gpt-image-1` → Pollinations (`image.pollinations.ai/prompt`) → keyword-based stock fallback. Pexels search is also supported (`Pexels:ApiKey`) for real-photo results. Keep the fallback intact so the UI never hard-fails.
- **Prompt** (`IAiPromptService`): Gemini (`gemini-2.0-flash` via OpenAI-compatible endpoint, **default**) / Groq (`llama-3.3-70b-versatile`) / OpenAI chat. Selected by `Ai:PromptProvider`. Gemini path sends the API key as a query parameter, not a Bearer header.
- **Moderation** (`AiModerationService`): gates UGC text — already called from `SocialService.CreatePostAsync` / `CreateCommentAsync` / `EditPostAsync`. New UGC entry points must call it too.
- **Health probe**: `GET /api/health/ai` (used by the CI deploy job).

### Live streaming, duels, gamification
- **Live**: `SocialController.Live/YayinBaslat/YayinBitir/GetAktifYayinlar/JoinLiveRoom` + `NotificationHub` WebRTC signaling (`StartBroadcast`, `JoinStream`, `SendOffer/Answer/IceCandidate`). Active broadcasts and viewers are tracked in static dictionaries on the hub. Frontend in `Views/Social/Live.cshtml`.
- **Duels/Competitions**: views in `Views/Social/Duels.cshtml` and `Views/Social/Competitions.cshtml` are currently UI-complete but driven by **mock ViewBag data** — no `Duellolar` table yet. Wire real tables through `DatabaseInitializer.cs` if extending.
- **XP/gamification**: `Kullanicilar.ToplamXP` + `Seviye` (cached fields); the source of truth is the `KullaniciXP` transaction log. `GunlukGorevler` stores daily quest progress. Leaderboard query in `SocialController.Feed()` sorts by `ToplamXP DESC`.

### Frontend stack
- Tailwind CSS via CDN; custom CSS variables for dark/light theming; Space Grotesk font. Icons: Lucide (`data-lucide`, init via `lucide.createIcons()`) + FontAwesome 6.5.0.
- `wwwroot/js/kartist-social.js` (v3.0) is the shared util — CSRF token helpers, `escapeHtml` XSS guard, media URL normalization, secure `fetch` wrapper, toast notifications, Turkish time-ago, avatar HTML, SignalR notification integration. **All UGC must be escaped before `innerHTML`.**
- Layouts: `_Layout.cshtml` (marketing/public), `_SocialLayout.cshtml` (legacy, only used by `Kesf`), `_SocialLayoutModern.cshtml` (current — Feed/Live/Duels/Profil/Messages/etc.). New social views go on `_SocialLayoutModern`.

### Deployment endpoint (irreversible — be careful)
`POST /api/deploy` accepts a zip, writes `update.bat`, launches it detached, and the script copies `offline_template.htm` → `app_offline.htm` (IIS shuts the app down), extracts the new build over the current directory, restores `web.config` + `appsettings.json` from `.bak` copies, then deletes `app_offline.htm` so IIS restarts.

Auth is HMAC-SHA256 only:
- Headers: `X-Kartist-Timestamp` (Unix seconds) and `X-Kartist-Signature` (lowercase hex).
- Signature = `HMAC-SHA256(Deployment:Secret, timestamp)`, compared with `CryptographicOperations.FixedTimeEquals`.
- Timestamp must be within `max(60, Deployment:SignatureToleranceSeconds)`.
- The earlier hardcoded `secret=...` form-field fallback was removed in commit `0061ec3` — **do not reintroduce it.**

CI in `.github/workflows/dotnet.yml` computes the signature with `openssl dgst -sha256 -hmac "$DEPLOY_SECRET"`. Do not change the signing scheme without updating that workflow in the same commit, and do not invoke this endpoint manually against production unless absolutely necessary.

There is also an unauthenticated `GET /api/debug/deploy-info` (`Program.cs:145`) that exposes `Deployment:Secret`'s length, SHA-256, and a test HMAC. It is marked **`// TEMPORARY DEBUG ENDPOINT - REMOVE AFTER FIXING DEPLOY`** and should be deleted once the deploy pipeline is stable. Prefer removing it over extending it.

## Security conventions that are easy to break

- **Passwords** in both `Kullanicilar` and `Yoneticiler` are BCrypt (`BCrypt.Net-Next`). Plaintext fallback was removed in commits `e26421c` (users) and `ce92c17` (admin lazy migration → removal). Rows that never migrated can no longer authenticate — that is intentional. Never reintroduce a plaintext branch.
- **2FA codes** are 6-digit OTPs from `RandomNumberGenerator.GetInt32(100000, 1000000)` — do not switch back to `System.Random`. Stored in `IkiFactorKodlari` with a 5-minute `BitisTarihi`. Disabling 2FA requires re-entering the password (Sprint 5 commit `d23eb47`).
- **CSRF** is global-on. New forms/AJAX POSTs must include the antiforgery token (`@Html.AntiForgeryToken()` is emitted in `Views/Shared/_Layout.cshtml`; JS reads `__RequestVerificationToken`).
- **Image uploads** must go through `Helpers/FileUploadValidator.TryValidateImage` — it validates magic bytes (JPEG/PNG/GIF/WEBP) and returns a server-chosen extension. **Never derive the saved filename from `IFormFile.ContentType` or `FileName`.** The six existing upload sites (post, before/after, story, avatar legacy, avatar, cover) are the reference shape. Sprint 4 commit `da603fd`.
- **UGC HTML** must go through `Helpers/InputValidator.SanitizeHtml` before storage (strips `<script>`, `<iframe>`, `on*=` handlers, `javascript:` URLs).
- **`/uploads/`** responses are sandboxed (`Content-Security-Policy: sandbox`) and `nosniff`-tagged (commit `15244ca`). Preserve this when touching the static-file pipeline — user-uploaded HTML/SVG must not execute in the page's origin.
- **Account enumeration**: `SifremiUnuttum` returns the same generic message and timing whether the email exists or not (Sprint 5 commit `ad80405`). Do not regress to a "kayıtlı değil" branch.
- **Contact form** (`HomeController.Iletisim`) HTML-encodes all user input via `WebUtility.HtmlEncode` and strips CRLF from the subject (SMTP header-injection guard). The anonymous `GetAiDebugLog` endpoint was removed in `16a891f` — do not add a similar public debug endpoint.

## Conventions

- Turkish identifiers (`Giris`, `Kayit`, `Profil`, `KullaniciId`, `OlusturmaTarihi`). Match surrounding code rather than translating.
- Uploaded files land in `wwwroot/uploads/` (gitignored): `wwwroot/uploads/social/` with GUID filenames is the social flow.
- Controllers are large (Home/Social/Account ~1k lines each) and mix Dapper + HTML + JSON in one class. Prefer pushing new logic into services/repositories rather than growing them.

## Out-of-repo things to know

- `_archive/` and `manual-publish/` hold old publish bundles, `migration.sql`, and experiments — **read-only historical reference**, excluded via `DefaultItemExcludes` in the csproj. Don't rely on them for current behavior.
- `docs/` has sprint reports and the vize project report PDF (Turkish academic format). Sprint 4 hardening notes also live in repo-root `notlar` — read it before changing the security commits referenced above.
- The `Iyzipay 2.1.67` package is referenced in `Kartist.csproj` but **not yet wired up** — forward-looking dependency for the planned template marketplace. No `Iyzipay` usings exist in `Controllers/` or `Services/` today.
- Production: `https://kartistt.com.tr`. CI health checks hit `/` and `/api/health/ai` there.
