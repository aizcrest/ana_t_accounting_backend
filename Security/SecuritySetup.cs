using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

static class SecuritySetup
{
 public static bool IsDevelopment(IConfiguration c)=>c["Ledger:Mode"]=="Development";
 public static bool RequiresDevice(IConfiguration c)=>!IsDevelopment(c)||c.GetValue<bool>("Ledger:RequireClientCertificate");
 public static bool IsLegacy(IConfiguration c)=>c["Ledger:Mode"]=="LegacyTest";
 public static bool IsLocalMode(IConfiguration c)=>IsDevelopment(c)||IsLegacy(c);
 public static void Configure(WebApplicationBuilder builder){var c=builder.Configuration;var local=IsLocalMode(c);var provisioning=builder.Configuration["Ledger:ProvisioningCommand"]=="true";
  if(local){if(!builder.Environment.IsDevelopment())throw new InvalidOperationException("Local modes require ASPNETCORE_ENVIRONMENT=Development.");var urls=c["urls"]??"http://127.0.0.1:5080";foreach(var url in urls.Split(';')){if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||!(u.Host=="localhost"||IPAddress.TryParse(u.Host,out var ip)&&IPAddress.IsLoopback(ip)))throw new InvalidOperationException("Development and regression modes must bind only to loopback.");}}
  else if(!provisioning){if(string.IsNullOrWhiteSpace(c["Ledger:ClientCaFile"])||string.IsNullOrWhiteSpace(c["Ledger:KeyEncryptionCertificate"]))throw new InvalidOperationException("Production requires a trusted workstation client CA and Data Protection key-encryption certificate. No anonymous fallback is available.");builder.WebHost.ConfigureKestrel(k=>k.ConfigureHttpsDefaults(h=>{h.ClientCertificateMode=ClientCertificateMode.RequireCertificate;h.ClientCertificateValidation=(cert,_,_)=>ValidateCertificate(cert,c["Ledger:ClientCaFile"]!);}));}
  if(local&&!IsLegacy(c)&&RequiresDevice(c)){if(string.IsNullOrWhiteSpace(c["Ledger:ClientCaFile"]))throw new InvalidOperationException("Client CA required.");builder.WebHost.ConfigureKestrel(k=>k.ConfigureHttpsDefaults(h=>{h.ClientCertificateMode=ClientCertificateMode.RequireCertificate;h.ClientCertificateValidation=(cert,_,_)=>ValidateCertificate(cert,c["Ledger:ClientCaFile"]!);}));}
  if(!local&&string.IsNullOrWhiteSpace(c["Ledger:KeyEncryptionCertificate"]))throw new InvalidOperationException("Production key encryption certificate required, including provisioning.");
  if(local)foreach(var endpoint in c.GetSection("Kestrel:Endpoints").GetChildren()){var url=endpoint["Url"];if(url is not null&&(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!(uri.Host=="localhost"||IPAddress.TryParse(uri.Host,out var address)&&IPAddress.IsLoopback(address))))throw new InvalidOperationException("Local Kestrel endpoints must bind only to loopback.");}
  var keyPath=c["Ledger:KeyPath"]??Path.Combine(builder.Environment.ContentRootPath,".local","auth-keys");Directory.CreateDirectory(keyPath);
  var keys=builder.Services.AddDataProtection().SetApplicationName("LedgerPH.SchoolIsolation.v1").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
  if(!string.IsNullOrWhiteSpace(c["Ledger:KeyEncryptionCertificate"]))keys.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(c["Ledger:KeyEncryptionCertificate"]!,c["Ledger:KeyEncryptionCertificatePassword"]));
  var connection=c.GetConnectionString("Control");
  if(!IsLegacy(c)&&string.IsNullOrWhiteSpace(connection))throw new InvalidOperationException("ConnectionStrings__Control is required. Provision the control database before starting the API.");
  if(!IsLegacy(c)&&!local&&!provisioning&&new Npgsql.NpgsqlConnectionStringBuilder(connection!).SslMode!=Npgsql.SslMode.VerifyFull)throw new InvalidOperationException("The control database requires verified TLS in production.");
  builder.Services.AddDbContext<ControlDb>(o=>o.UseNpgsql(connection??"Host=localhost;Database=unused_legacy_test"));
  builder.Services.AddIdentityCore<LedgerUser>(o=>{o.Password.RequiredLength=12;o.Password.RequireDigit=true;o.Password.RequireLowercase=true;o.Password.RequireUppercase=true;o.Password.RequireNonAlphanumeric=true;o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(15);o.Lockout.MaxFailedAccessAttempts=5;o.User.RequireUniqueEmail=true;}).AddEntityFrameworkStores<ControlDb>().AddDefaultTokenProviders();
  builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o=>{o.Cookie.Name=local?"Ledger.Session.Dev":"__Host-Ledger.Session";o.Cookie.HttpOnly=true;o.Cookie.SecurePolicy=local?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;o.Cookie.SameSite=SameSiteMode.Strict;o.Cookie.Path="/";o.ExpireTimeSpan=TimeSpan.FromHours(8);o.SlidingExpiration=false;o.Events.OnRedirectToLogin=ctx=>{ctx.Response.StatusCode=401;return Task.CompletedTask;};o.Events.OnRedirectToAccessDenied=ctx=>{ctx.Response.StatusCode=403;return Task.CompletedTask;};});
  builder.Services.AddAntiforgery(o=>{o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name=local?"Ledger.Csrf.Dev":"__Host-Ledger.Csrf";o.Cookie.SecurePolicy=local?CookieSecurePolicy.SameAsRequest:CookieSecurePolicy.Always;o.Cookie.SameSite=SameSiteMode.Strict;});
  builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("login",http=>RateLimitPartition.GetFixedWindowLimiter(http.Request.Path+":"+(http.Connection.RemoteIpAddress?.ToString()??"unknown"),_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
  builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<SchoolWorkspace>();builder.Services.AddSingleton<LegacyTestWorkspace>();
 }
 public static bool ValidateCertificate(X509Certificate2 certificate,string caFile){try{using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;chain.ChainPolicy.CustomTrustStore.Add(X509CertificateLoader.LoadCertificateFromFile(caFile));chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.2"));chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;return chain.Build(certificate)&&!certificate.HasPrivateKey;}catch{return false;}}
}
