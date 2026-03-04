var builder = WebApplication.CreateBuilder(args);

// 1. SERV�SLER (S�ralama �nemli de�il ama d�zenli olsun)
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation(); // F5 ile yenileme i�in
builder.Services.AddSignalR(); // Canl� bildirim (Kredi y�klenince)
builder.Services.AddSession(); // Admin giri�i i�in

// 2. K�ML�K DO�RULAMA (Giri� Sistemi)
builder.Services.AddAuthentication("KartistCookie")
    .AddCookie("KartistCookie", options =>
    {
        options.LoginPath = "/Account/Giris"; // Giri� yapmam��sa buraya at
        options.Cookie.Name = "KartistUye";   // �erezin ad�
        options.ExpireTimeSpan = TimeSpan.FromDays(30); // 30 g�n a��k kals�n
    });

var app = builder.Build();

// 3. ARA KATMANLAR (Middleware - S�ras� �OK �NEML�)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // CSS, JS, Resimler �al��s�n diye

app.UseRouting(); // Y�nlendirme sistemi ba�las�n

app.UseSession(); // Admin paneli i�in session a��ls�n

// --- G�VENL�K DUVARI (�NCE K�ML�K, SONRA YETK�) ---
app.UseAuthentication(); // 1. Kimlik Kart�n� G�ster (Giri� yapm�� m�?)
app.UseAuthorization();  // 2. Yetkisi Var m�?
// ---------------------------------------------------

// 4. ROTALAR (Adresler)
app.MapHub<Kartist.Hubs.AdminHub>("/adminHub"); // SignalR Hatt�

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
// Bu "default" rota sayesinde /Account/Profil adresi otomatik �al���r.

app.Run();