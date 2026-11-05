using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

sealed record SchoolBackup(Guid SchoolId,string SchoolCode,int SchemaVersion,string Snapshot,string SnapshotHash,string Audit,DateTimeOffset CreatedAt);
static class SchoolBackups
{
 public static async Task Run(string[] args,IConfiguration c,ControlDb control,IDataProtectionProvider protection){
  if(!Guid.TryParse(c["Ledger:TargetSchoolId"],out var schoolId))throw new ArgumentException("Set Ledger__TargetSchoolId explicitly.");
  var school=await control.Schools.SingleAsync(s=>s.Id==schoolId&&s.ProvisioningStatus=="Ready");
  var privileged=c.GetConnectionString("Provisioning")??throw new InvalidOperationException("Provisioning credentials required for offline backup operations.");
  var admin=new NpgsqlConnectionStringBuilder(privileged){Database=school.DatabaseName};var protector=protection.CreateProtector("Ledger.SchoolBackup.v1");var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
  var path=c["Ledger:BackupPath"]??Path.Combine(Directory.GetCurrentDirectory(),".local","backups",school.Id.ToString("N"),DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".ledgerbak");
  var fullPath=Path.GetFullPath(path);if(fullPath.Split(Path.DirectorySeparatorChar).Contains("wwwroot",StringComparer.OrdinalIgnoreCase))throw new ArgumentException("Backups must be stored outside wwwroot.");
  string Hash(string data)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
  if(args.Contains("--backup-school")){
   if(school.Enabled)throw new InvalidOperationException("Suspend the school and drain writes before taking a consistent offline backup.");
   var store=new PostgresLedgerStore(admin.ConnectionString,school.Id);var data=store.Load()??throw new InvalidOperationException("Empty school.");var snapshot=JsonSerializer.Serialize(data,json);
   using var connection=new NpgsqlConnection(admin.ConnectionString);connection.Open();using var command=new NpgsqlCommand("SELECT COALESCE(jsonb_agg(to_jsonb(a) ORDER BY at,id),'[]'::jsonb)::text FROM audit_events a",connection);var audit=(string)command.ExecuteScalar()!;
   using var version=new NpgsqlCommand("SELECT max(version) FROM schema_versions",connection);var pack=new SchoolBackup(school.Id,school.Code,Convert.ToInt32(version.ExecuteScalar()),snapshot,Hash(snapshot),audit,DateTimeOffset.UtcNow);
   Provisioning.WritePrivate(fullPath,new(){["protectedBackup"]=protector.Protect(JsonSerializer.Serialize(pack,json))});control.PlatformAudit.Add(new(){Actor="provisioning-cli",SchoolId=school.Id,Action="school.backup",Outcome="Encrypted"});await control.SaveChangesAsync();Console.WriteLine("Encrypted school backup written outside the web root. Retain the protected Data Protection keyring separately.");return;
  }
  var wrapper=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(fullPath))!;var backup=JsonSerializer.Deserialize<SchoolBackup>(protector.Unprotect(wrapper["protectedBackup"]),json)!;
  if(backup.SchoolId!=school.Id||backup.SchoolCode!=school.Code||Hash(backup.Snapshot)!=backup.SnapshotHash||backup.SchemaVersion>4)throw new InvalidOperationException("Backup identity/version validation failed. No live database was changed.");
  var restoreName="ledger_restore_"+Guid.NewGuid().ToString("N");var destination=new NpgsqlConnectionStringBuilder(privileged){Database=restoreName};var target=new PostgresLedgerStore(destination.ConnectionString);target.Initialize();
  try{
   using var connection=new NpgsqlConnection(destination.ConnectionString);connection.Open();using(var marker=new NpgsqlCommand("INSERT INTO tenant_identity(singleton,school_id) VALUES(true,@school)",connection)){marker.Parameters.AddWithValue("school",school.Id);marker.ExecuteNonQuery();}
   using(var revoke=new NpgsqlCommand("REVOKE ALL ON DATABASE "+new NpgsqlCommandBuilder().QuoteIdentifier(restoreName)+" FROM PUBLIC",connection))revoke.ExecuteNonQuery();
   target.Load();var data=JsonSerializer.Deserialize<Database>(backup.Snapshot,json)!;target.Save(data);
   using(var audit=new NpgsqlCommand("INSERT INTO audit_events SELECT * FROM jsonb_populate_recordset(NULL::audit_events,@audit); INSERT INTO audit_outbox(event_id) SELECT id FROM audit_events",connection)){audit.Parameters.AddWithValue("audit",NpgsqlDbType.Jsonb,backup.Audit);audit.ExecuteNonQuery();}
   var loaded=new PostgresLedgerStore(destination.ConnectionString,school.Id).Load()!;if(Hash(JsonSerializer.Serialize(loaded,json))!=backup.SnapshotHash)throw new InvalidOperationException("Restored snapshot does not match backup.");
   control.PlatformAudit.Add(new(){Actor="provisioning-cli",SchoolId=school.Id,Action="school.restore.verify",Outcome="Quarantine verified"});await control.SaveChangesAsync();Console.WriteLine("Restore verified in private quarantine database: "+restoreName+". Live school databases and routing were not changed. Activation requires an offline maintenance procedure.");
  }catch{Console.Error.WriteLine("Restore validation failed in quarantine. Live school databases were not changed.");throw;}
 }
}
