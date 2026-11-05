using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

static class Provisioning
{
 public static bool IsCommand(string[] args)=>args.Any(a=>new[]{"--control-init","--provision-pending","--demo-school","--import-legacy","--approve-device","--flush-audit","--reset-password","--reset-mfa","--backup-school","--verify-restore"}.Contains(a));
 public static string ControlOwner(IConfiguration c){var raw=c.GetConnectionString("Provisioning")??throw new InvalidOperationException("Set ConnectionStrings__Provisioning securely for provisioning commands only.");return new NpgsqlConnectionStringBuilder(raw){Database=c["Ledger:ControlDatabaseName"]??"ledger_control"}.ConnectionString;}
 static string Q(string name)=>new NpgsqlCommandBuilder().QuoteIdentifier(name);
 static string Literal(string text)=>"'"+text.Replace("'","''")+"'";
 static string RandomSecret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 static void Sql(NpgsqlConnection c,string sql){using var cmd=new NpgsqlCommand(sql,c);cmd.ExecuteNonQuery();}
 static void CreateDatabase(string connection){var target=new NpgsqlConnectionStringBuilder(connection);using var c=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection){Database="postgres"}.ConnectionString);c.Open();using var exists=new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_database WHERE datname=@name)",c);exists.Parameters.AddWithValue("name",target.Database!);if(!(bool)exists.ExecuteScalar()!)Sql(c,"CREATE DATABASE "+Q(target.Database!));}
 public static void WritePrivate(string path,Dictionary<string,string> values){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);var temp=path+".tmp";if(File.Exists(temp))File.Delete(temp);var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;using(var file=new FileStream(temp,options))JsonSerializer.Serialize(file,values);File.Move(temp,path,true);}
 public static async Task Run(WebApplication app,string[] args){using var scope=app.Services.CreateScope();var c=app.Configuration;var db=scope.ServiceProvider.GetRequiredService<ControlDb>();var users=scope.ServiceProvider.GetRequiredService<UserManager<LedgerUser>>();var protector=scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Ledger.SchoolConnection.v1");var owner=ControlOwner(c);
  void Audit(string action,Guid? school=null){db.PlatformAudit.Add(new(){Actor="provisioning-cli",SchoolId=school,Action=action,Outcome="Success"});}
  if(args.Contains("--backup-school")||args.Contains("--verify-restore")){await SchoolBackups.Run(args,c,db,scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());return;}
  if(args.Contains("--control-init")){
   CreateDatabase(owner);await db.Database.EnsureCreatedAsync();
   if(!await db.Users.AnyAsync(u=>u.PlatformAdmin)){
    var email=c["Ledger:BootstrapEmail"]??throw new InvalidOperationException("Set Ledger__BootstrapEmail.");var password=c["Ledger:BootstrapPassword"]??throw new InvalidOperationException("Set Ledger__BootstrapPassword securely; no default password is created.");var admin=new LedgerUser{UserName=email,Email=email,PlatformAdmin=true};var result=await users.CreateAsync(admin,password);if(!result.Succeeded)throw new ArgumentException(string.Join(" ",result.Errors.Select(e=>e.Description)));Audit("admin.bootstrap");await db.SaveChangesAsync();
   }
   var path=c["Ledger:RuntimeConfigPath"]??Path.Combine(app.Environment.ContentRootPath,".local","secure-runtime.json");
   if(!File.Exists(path)){
    var name="ledger_control_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new NpgsqlConnectionStringBuilder(owner).Database!))).Substring(0,16).ToLowerInvariant();var password=RandomSecret();using var connection=new NpgsqlConnection(owner);connection.Open();using var exists=new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_roles WHERE rolname=@name)",connection);exists.Parameters.AddWithValue("name",name);if((bool)exists.ExecuteScalar()!)throw new InvalidOperationException("Runtime role already exists but its local configuration is missing; restore that configuration rather than rotating silently.");
    Sql(connection,$"CREATE ROLE {Q(name)} LOGIN PASSWORD {Literal(password)} NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS; REVOKE ALL ON DATABASE {Q(new NpgsqlConnectionStringBuilder(owner).Database!)} FROM PUBLIC; GRANT CONNECT ON DATABASE {Q(new NpgsqlConnectionStringBuilder(owner).Database!)} TO {Q(name)}; REVOKE CREATE ON SCHEMA public FROM PUBLIC; GRANT USAGE ON SCHEMA public TO {Q(name)}; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO {Q(name)}; REVOKE UPDATE,DELETE ON TABLE \"PlatformAudit\" FROM {Q(name)};");
    var runtime=new NpgsqlConnectionStringBuilder(owner){Username=name,Password=password,MaxPoolSize=20,MinPoolSize=0};WritePrivate(path,new(){["ConnectionStrings__Control"]=runtime.ConnectionString,["Ledger__KeyPath"]=c["Ledger:KeyPath"]??Path.Combine(app.Environment.ContentRootPath,".local","auth-keys"),["Ledger__Mode"]=SecuritySetup.IsDevelopment(c)?"Development":"Production",["ASPNETCORE_ENVIRONMENT"]=SecuritySetup.IsDevelopment(c)?"Development":"Production"});
   }
   Console.WriteLine("Control database and restricted runtime configuration initialized. Administrator must enroll MFA at first login.");return;
  }
  async Task ProvisionSchool(SchoolRegistration school,bool demo){
   if(school.ProvisioningStatus=="Ready")return;school.DatabaseName="ledger_school_"+school.Id.ToString("N");var role="ledger_app_"+school.Id.ToString("N");var password=RandomSecret();var admin=new NpgsqlConnectionStringBuilder(c.GetConnectionString("Provisioning")!){Database=school.DatabaseName};
   using(var cluster=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(admin.ConnectionString){Database="postgres"}.ConnectionString)){cluster.Open();Sql(cluster,$"DO $$ BEGIN IF NOT EXISTS(SELECT FROM pg_roles WHERE rolname={Literal(role)}) THEN CREATE ROLE {Q(role)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS; END IF; END $$; ALTER ROLE {Q(role)} PASSWORD {Literal(password)};");}
   var store=new PostgresLedgerStore(admin.ConnectionString);store.Initialize();using(var connection=new NpgsqlConnection(admin.ConnectionString)){connection.Open();using var marker=new NpgsqlCommand("INSERT INTO tenant_identity(singleton,school_id) VALUES(true,@id) ON CONFLICT(singleton) DO NOTHING; SELECT school_id FROM tenant_identity WHERE singleton=true",connection);marker.Parameters.AddWithValue("id",school.Id);if((Guid)marker.ExecuteScalar()! != school.Id)throw new InvalidOperationException("Existing database belongs to another school.");
    var tables="accounts,cards,items,signatory_assignments,module_access,atc_codes,documents,journal_lines,document_signatories,store_state,canteen_settings,canteen_inputs,canteen_submissions,banks,check_ranges";
    Sql(connection,$"REVOKE ALL ON DATABASE {Q(school.DatabaseName)} FROM PUBLIC; GRANT CONNECT ON DATABASE {Q(school.DatabaseName)} TO {Q(role)}; REVOKE CREATE ON SCHEMA public FROM PUBLIC; GRANT USAGE ON SCHEMA public TO {Q(role)}; GRANT SELECT ON ALL TABLES IN SCHEMA public TO {Q(role)}; GRANT INSERT,UPDATE,DELETE ON {tables} TO {Q(role)}; GRANT EXECUTE ON FUNCTION ledger_append_audit(uuid,text,text,text,text) TO {Q(role)};");
   }
   var data=store.Load();if(data is null){data=Seed.Create();if(!demo){data.Documents.Clear();data.Cards.Clear();data.Items.Clear();data.Signatories=data.Signatories.Select(s=>s with{Name=""}).ToList();}data.CanteenProfile=SchoolProfile.Default(data);data.CanteenProfile.Name=school.Name;data.CanteenProfile.SchoolId=school.Code;store.Save(data);}
   school.ProtectedConnection=protector.Protect(new NpgsqlConnectionStringBuilder(admin.ConnectionString){Username=role,Password=password,MaxPoolSize=5,MinPoolSize=0,ConnectionIdleLifetime=60}.ConnectionString);school.ProvisioningStatus="Ready";Audit(demo?"school.provision.demo":"school.provision",school.Id);await db.SaveChangesAsync();
  }
  if(args.Contains("--demo-school")){
   var code=c["Ledger:SchoolCode"]??"DEMO-A";var school=await db.Schools.SingleOrDefaultAsync(s=>s.Code==code);if(school is null){school=new(){Code=code,Name=c["Ledger:SchoolName"]??"Demo School A"};db.Schools.Add(school);await db.SaveChangesAsync();}await ProvisionSchool(school,true);Console.WriteLine("Explicit demo school initialized: "+school.Code);return;
  }
  if(args.Contains("--provision-pending")){
   foreach(var school in await db.Schools.Where(s=>s.ProvisioningStatus=="Pending"||s.ProvisioningStatus=="Failed").ToListAsync()){try{await ProvisionSchool(school,false);Console.WriteLine("Provisioned school: "+school.Code);}catch{school.ProvisioningStatus="Failed";Audit("school.provision.failed",school.Id);await db.SaveChangesAsync();throw;}}return;
  }
  if(args.Contains("--import-legacy")){
   if(!Guid.TryParse(c["Ledger:TargetSchoolId"],out var id))throw new ArgumentException("Set Ledger__TargetSchoolId explicitly.");var school=await db.Schools.SingleAsync(s=>s.Id==id&&s.ProvisioningStatus=="Ready");var admin=new NpgsqlConnectionStringBuilder(c.GetConnectionString("Provisioning")!){Database=school.DatabaseName};var destination=new PostgresLedgerStore(admin.ConnectionString,school.Id);var current=destination.Load()!;if(current.Documents.Count>0||current.Cards.Count>0||current.Banks.Count>0)throw new InvalidOperationException("Import requires an empty destination school; no existing books are overwritten.");var source=c.GetConnectionString("LegacyImport")??throw new InvalidOperationException("Set ConnectionStrings__LegacyImport for an explicit read-only legacy import.");var sourceStore=new PostgresLedgerStore(source);var data=sourceStore.Load()??throw new InvalidOperationException("Legacy source is empty.");data.CanteenProfile??=SchoolProfile.Default(data);data.CanteenProfile.Name=school.Name;data.CanteenProfile.SchoolId=school.Code;destination.Save(data,new("provisioning-cli","legacy.import",null,Guid.NewGuid().ToString()));Audit("legacy.import",school.Id);await db.SaveChangesAsync();Console.WriteLine("Legacy workspace copied to the explicitly selected school. The source database was left unchanged.");return;
  }
  if(args.Contains("--approve-device")){
   var fingerprint=(c["Ledger:DeviceFingerprint"]??"").Replace(":","").ToUpperInvariant();if(fingerprint.Length!=64||fingerprint.Any(x=>!Uri.IsHexDigit(x)))throw new ArgumentException("Set a valid SHA-256 client certificate fingerprint.");Guid? school=Guid.TryParse(c["Ledger:DeviceSchoolId"],out var id)?id:null;if(school.HasValue&&!await db.Schools.AnyAsync(s=>s.Id==school))throw new ArgumentException("Unknown device school.");var device=await db.Devices.SingleOrDefaultAsync(d=>d.Id==fingerprint);if(device is null){device=new(){Id=fingerprint};db.Devices.Add(device);}device.Name=c["Ledger:DeviceName"]??"Administrator workstation";device.SchoolId=school;device.Enabled=true;Audit("device.approve:"+fingerprint,school);await db.SaveChangesAsync();Console.WriteLine("Workstation certificate approved.");return;
  }
  if(args.Contains("--reset-password")||args.Contains("--reset-mfa")){
   var email=c["Ledger:ResetUserEmail"]??throw new ArgumentException("Set Ledger__ResetUserEmail explicitly.");var user=await users.FindByEmailAsync(email)??throw new ArgumentException("Account not found.");
   if(args.Contains("--reset-password")){var password=c["Ledger:ResetPassword"]??throw new ArgumentException("Supply Ledger__ResetPassword securely.");var result=await users.ResetPasswordAsync(user,await users.GeneratePasswordResetTokenAsync(user),password);if(!result.Succeeded)throw new ArgumentException(string.Join(" ",result.Errors.Select(e=>e.Description)));}
   if(args.Contains("--reset-mfa")){await users.ResetAuthenticatorKeyAsync(user);await users.SetTwoFactorEnabledAsync(user,false);await users.GenerateNewTwoFactorRecoveryCodesAsync(user,0);await users.UpdateSecurityStampAsync(user);}
   await users.ResetAccessFailedCountAsync(user);await users.SetLockoutEndDateAsync(user,null);
   foreach(var session in await db.Sessions.Where(s=>s.UserId==user.Id&&!s.Revoked).ToListAsync())session.Revoked=true;
   Audit((args.Contains("--reset-mfa")?"owner.mfa.reset:":"owner.password.reset:")+user.Id);await db.SaveChangesAsync();Console.WriteLine("Account recovery completed; existing sessions were revoked. MFA enrollment/verification is required at sign-in.");return;
  }
  if(args.Contains("--flush-audit")){
   foreach(var school in await db.Schools.Where(s=>s.ProvisioningStatus=="Ready").ToListAsync()){
    using var connection=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(c.GetConnectionString("Provisioning")!){Database=school.DatabaseName}.ConnectionString);connection.Open();var events=new List<PlatformAudit>();using(var cmd=new NpgsqlCommand("SELECT a.id,a.actor,a.action,a.at FROM audit_events a JOIN audit_outbox o ON o.event_id=a.id WHERE o.delivered_at IS NULL ORDER BY a.at LIMIT 500",connection))using(var reader=cmd.ExecuteReader())while(reader.Read())events.Add(new(){Id=reader.GetGuid(0),Actor=reader.GetString(1),Action=reader.GetString(2),At=reader.GetFieldValue<DateTimeOffset>(3),SchoolId=school.Id,Outcome=reader.GetString(2).StartsWith("GET ")?"ReadAttempt":"Committed"});
    foreach(var audit in events){if(!await db.PlatformAudit.AnyAsync(a=>a.Id==audit.Id)){db.PlatformAudit.Add(audit);await db.SaveChangesAsync();}using var ack=new NpgsqlCommand("UPDATE audit_outbox SET delivered_at=now() WHERE event_id=@id",connection);ack.Parameters.AddWithValue("id",audit.Id);ack.ExecuteNonQuery();}
   }Console.WriteLine("School audit outboxes forwarded idempotently.");return;
  }
 }
}
