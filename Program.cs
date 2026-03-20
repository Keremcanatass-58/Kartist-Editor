using Kartist.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "text/css", "application/javascript", "text/html", "image/svg+xml" });
});

var mvc = builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Razor:RuntimeCompilation", true))
{
    mvc.AddRazorRuntimeCompilation();
}
builder.Services.AddSignalR();
builder.Services.AddSession();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "KartistCookie";
    options.DefaultSignInScheme = "KartistCookie";
    options.DefaultChallengeScheme = "KartistCookie";
})
    .AddCookie("KartistCookie", options =>
    {
        options.LoginPath = "/Account/Giris";
        options.Cookie.Name = "KartistUye";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
    })
    .AddCookie("External", options =>
    {
        options.Cookie.Name = "KartistExternal";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle("Google", options =>
    {
        options.SignInScheme = "External";
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });
}

var app = builder.Build();

var autoSchema = builder.Configuration.GetValue<bool>("Database:AutoSchema", true);
if (autoSchema)
{
    EnsureDatabaseSchema(builder.Configuration.GetConnectionString("DefaultConnection"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseMiddleware<SecurityHeadersMiddleware>();

var secConfig = builder.Configuration.GetSection("Security");
int maxReq = secConfig.GetValue<int>("RateLimitMaxRequests", 100);
int timeWin = secConfig.GetValue<int>("RateLimitTimeWindowSeconds", 60);
// Comment out rate limiting if it causes build errors without the proper AddRateLimiter setup, but keep the middleware.
// app.UseRateLimiting(maxReq, timeWin);
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=604800");
    }
});

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<Kartist.Hubs.AdminHub>("/adminHub");

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapPost("/api/deploy", async (Microsoft.AspNetCore.Http.HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    if (form["secret"] != "kartist-deploy-secret-2026") return Microsoft.AspNetCore.Http.Results.Unauthorized();
    if (form.Files.Count == 0) return Microsoft.AspNetCore.Http.Results.BadRequest("No file");

    var file = form.Files[0];
    var zipPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "release.zip");
    using (var stream = new System.IO.FileStream(zipPath, System.IO.FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    var batPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "update.bat");
    var batContent = @"@echo off
timeout /t 2 /nobreak > nul
echo ^<html^>^<body^>^<h2^>Kartist Guncelleniyor...^</h2^>^</body^>^</html^> > app_offline.htm
timeout /t 3 /nobreak > nul
tar -xf release.zip
del app_offline.htm
del release.zip
del update.bat";
                
    await System.IO.File.WriteAllTextAsync(batPath, batContent);
    var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {batPath}",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        }
    };
    process.Start();
    return Microsoft.AspNetCore.Http.Results.Ok("Deployment initiated successfully.");
}).DisableAntiforgery();

app.Run();

static void EnsureDatabaseSchema(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return;
    }

    const string sql = @"
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'BasarisizGirisSayisi')
    ALTER TABLE Kullanicilar ADD BasarisizGirisSayisi INT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'HesapKilitliMi')
    ALTER TABLE Kullanicilar ADD HesapKilitliMi BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'KilitBitisTarihi')
    ALTER TABLE Kullanicilar ADD KilitBitisTarihi DATETIME NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'IkiFactorAktif')
    ALTER TABLE Kullanicilar ADD IkiFactorAktif BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'Biyografi')
    ALTER TABLE Kullanicilar ADD Biyografi NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'SosyalMedya')
    ALTER TABLE Kullanicilar ADD SosyalMedya NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'ProfilResmi')
    ALTER TABLE Kullanicilar ADD ProfilResmi NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Kullanicilar') AND name = 'UyelikBitisTarihi')
    ALTER TABLE Kullanicilar ADD UyelikBitisTarihi DATETIME NULL;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('IkiFactorKodlari') AND type = 'U')
BEGIN
    CREATE TABLE IkiFactorKodlari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciEmail NVARCHAR(256) NOT NULL,
        Kod NVARCHAR(6) NOT NULL,
        OlusturmaTarihi DATETIME NOT NULL DEFAULT GETUTCDATE(),
        BitisTarihi DATETIME NOT NULL,
        Kullanildi BIT NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_IkiFactorKodlari_Email ON IkiFactorKodlari(KullaniciEmail);
END

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('GirisLoglari') AND type = 'U')
BEGIN
    CREATE TABLE GirisLoglari (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        KullaniciEmail NVARCHAR(256) NULL,
        IpAdresi NVARCHAR(50) NULL,
        BasariliMi BIT NULL,
        Tarih DATETIME NOT NULL DEFAULT GETUTCDATE(),
        UserAgent NVARCHAR(500) NULL
    );
    CREATE INDEX IX_GirisLoglari_Email ON GirisLoglari(KullaniciEmail);
    CREATE INDEX IX_GirisLoglari_Tarih ON GirisLoglari(Tarih);
END
";

    try
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB schema check failed: {ex.Message}");
    }
}





