using System.Security;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

sealed record SchoolContext(Guid SchoolId,string SchoolCode,string SchoolName,string UserId,string Role,string? DeviceId,Guid SessionId);
sealed class SchoolWorkspace
{
 readonly PostgresLedgerStore? store;readonly SchoolContext? context;readonly HttpContext http;readonly LegacyTestWorkspace legacy;
 public Database Data{get;private set;}
 public object Gate{get;}=new();
 public SchoolWorkspace(IHttpContextAccessor accessor,IDataProtectionProvider protection,IConfiguration config,LegacyTestWorkspace legacy){
  this.legacy=legacy;http=accessor.HttpContext??throw new SecurityException("Request context required.");
  if(SecuritySetup.IsLegacy(config)){Data=legacy.Data;Gate=legacy.Gate;return;}
  context=http.Items["SchoolContext"] as SchoolContext??throw new SecurityException("School access required.");
  var registration=http.Items["SchoolRegistration"] as SchoolRegistration??throw new SecurityException("School unavailable.");
  var connection=protection.CreateProtector("Ledger.SchoolConnection.v1").Unprotect(registration.ProtectedConnection);
  var selector=new NpgsqlConnectionStringBuilder(connection);
  if(selector.Database!="ledger_school_"+context.SchoolId.ToString("N")||selector.Username!="ledger_app_"+context.SchoolId.ToString("N"))throw new SecurityException("School database identity mismatch.");
  if(!SecuritySetup.IsDevelopment(config)&&selector.SslMode!=SslMode.VerifyFull)throw new SecurityException("Verified PostgreSQL TLS is required.");
  store=new PostgresLedgerStore(connection,context.SchoolId);Data=store.Load()??throw new SecurityException("School database has no initialized chart.");
  Data.CanteenProfile??=SchoolProfile.Default(Data);Data.CanteenProfile.Name=context.SchoolName;Data.CanteenProfile.SchoolId=context.SchoolCode;
 }
 public void Save(){
  if(store is null){legacy.Save();return;}
  var actor=context!;
  if(Data.CanteenProfile is not null&&(Data.CanteenProfile.Name!=actor.SchoolName||Data.CanteenProfile.SchoolId!=actor.SchoolCode))throw new ArgumentException("School name and school ID are registered by the administrator and cannot be changed here.");
  try{store.Save(Data,new(actor.UserId,http.Request.Method+" "+http.Request.Path,actor.DeviceId,http.TraceIdentifier));}
  catch{Data=store.Load()!;throw;}
 }
 public void AuditRead(){if(store is not null)store.Audit(new(context!.UserId,http.Request.Method+" "+http.Request.Path,context.DeviceId,http.TraceIdentifier));}
}
// Explicit loopback-only compatibility harness for the existing accounting regression suites.
// It is never enabled in production and cannot be selected by a cookie, header or request.
sealed class LegacyTestWorkspace
{
 public Database Data{get;private set;}=null!;public object Gate{get;}=new();PostgresLedgerStore? store;string path="";
 public void Initialize(IConfiguration config){path=config["ACCOUNTING_DATA_PATH"]??Path.Combine(Directory.GetCurrentDirectory(),"data","ledger.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);var conn=config.GetConnectionString("Accounting");store=string.IsNullOrWhiteSpace(conn)?null:new PostgresLedgerStore(conn);store?.Initialize();Data=store?.Load()??(File.Exists(path)?System.Text.Json.JsonSerializer.Deserialize<Database>(File.ReadAllText(path),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!:Seed.Create());Save();}
 public void Save(){if(store!=null){store.Save(Data);return;}var temp=path+".tmp";File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(Data,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));File.Move(temp,path,true);}
}
