using Microsoft.EntityFrameworkCore;
using System.Security;
var builder=WebApplication.CreateBuilder(args);
if(Provisioning.IsCommand(args)){
 builder.Configuration["Ledger:ProvisioningCommand"]="true";
 builder.Configuration["ConnectionStrings:Control"]=Provisioning.ControlOwner(builder.Configuration);
}
if(args.Contains("--report-export-self-test")){ReportExportValidation.Run();return;}
if(!Provisioning.IsCommand(args))builder.Configuration["Ledger:ProvisioningCommand"]="false";
SecuritySetup.Configure(builder);
var app=builder.Build();
if(Provisioning.IsCommand(args)){await Provisioning.Run(app,args);return;}
if(SecuritySetup.IsLegacy(app.Configuration)){
 app.Services.GetRequiredService<LegacyTestWorkspace>().Initialize(app.Configuration);
 if(args.Contains("--check-database")){Console.WriteLine("Explicit regression-mode ledger storage initialized.");return;}
 if(args.Contains("--database-self-test")){PostgresStoreSelfTest.Run(app.Configuration.GetConnectionString("Accounting")!);return;}
}else{
 if(!string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Provisioning")))throw new InvalidOperationException("Provisioning credentials must not be present in the web process.");
 using var scope=app.Services.CreateScope();if(!await scope.ServiceProvider.GetRequiredService<ControlDb>().Database.CanConnectAsync())throw new InvalidOperationException("Control database is unavailable. No default school or anonymous fallback is used.");
}
app.Use(async(h,next)=>{
 h.Response.Headers["X-Content-Type-Options"]="nosniff";h.Response.Headers["Referrer-Policy"]="no-referrer";
 h.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
 try{await next();}
 catch(LedgerConflictException){h.Response.StatusCode=409;await h.Response.WriteAsJsonAsync(new{error="The ledger changed. Reload and retry."});}
 catch(ResourceNotFoundException){h.Response.StatusCode=404;await h.Response.WriteAsJsonAsync(new{error="Resource not found."});}
 catch(SecurityException e){app.Logger.LogWarning("School context validation denied request {Trace}: {Reason}",h.TraceIdentifier,e.Message);h.Response.StatusCode=403;await h.Response.WriteAsJsonAsync(new{error="School access unavailable."});}
 catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException{SqlState:"23505"}){h.Response.StatusCode=409;await h.Response.WriteAsJsonAsync(new{error="The account or assignment already exists. Reload and check the current school assignments."});}
 catch(Npgsql.PostgresException e){app.Logger.LogWarning("Database operation rejected {Trace}: {SqlState}",h.TraceIdentifier,e.SqlState);h.Response.StatusCode=e.SqlState is "23505" or "40001"?409:503;await h.Response.WriteAsJsonAsync(new{error="Database operation failed. Reload or contact the administrator."});}
 catch(Npgsql.NpgsqlException){h.Response.StatusCode=503;await h.Response.WriteAsJsonAsync(new{error="Database unavailable. No fallback school is used."});}
 catch(ArgumentException e){h.Response.StatusCode=400;await h.Response.WriteAsJsonAsync(new{error=e.Message});}
});
app.UseDefaultFiles();app.UseStaticFiles(new StaticFileOptions{OnPrepareResponse=context=>{if(context.File.Name=="index.html")context.Context.Response.Headers.CacheControl="no-store";}});app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseMiddleware<SchoolAccessMiddleware>();
app.MapGet("/api/health",()=>SecuritySetup.IsLegacy(app.Configuration)?Results.Ok(new{status="ok",storage=string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Accounting"))?"JSON":"PostgreSQL"}):Results.Ok(new{status="ok",authentication="required",storage="school-isolated PostgreSQL",deviceAccess=SecuritySetup.RequiresDevice(app.Configuration)?"approved client certificate":"loopback development only"}));
AuthEndpoints.Map(app);AccountingEndpoints.Map(app);CanteenEndpoints.Map(app);Banking.Map(app);
app.Map("/api/{**path}",()=>Results.NotFound());app.MapFallbackToFile("index.html");
app.Run();
sealed class ResourceNotFoundException:Exception;
