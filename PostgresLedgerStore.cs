using System.Data;
using System.Reflection;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

// Small, single-workspace development store: atomic relational snapshot writes.
sealed class PostgresLedgerStore
{
 readonly string connectionString;
 readonly Guid? expectedSchoolId;
 readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);
 long revision;
 public PostgresLedgerStore(string connectionString,Guid? expectedSchoolId=null){this.connectionString=connectionString;this.expectedSchoolId=expectedSchoolId;}
 NpgsqlConnection Open(){var connection=new NpgsqlConnection(connectionString);connection.Open();if(expectedSchoolId.HasValue){try{using var marker=new NpgsqlCommand("SELECT school_id FROM tenant_identity WHERE singleton=true",connection);if(marker.ExecuteScalar() is not Guid actual||actual!=expectedSchoolId)throw new System.Security.SecurityException("School identity mismatch.");}catch{connection.Dispose();throw;}}return connection;}
 static void AppendAudit(NpgsqlConnection connection,NpgsqlTransaction? transaction,AuditWrite audit){using var command=new NpgsqlCommand("SELECT ledger_append_audit(@id,@actor,@action,@device,@correlation)",connection,transaction);command.Parameters.AddWithValue("id",Guid.NewGuid());command.Parameters.AddWithValue("actor",audit.Actor);command.Parameters.AddWithValue("action",audit.Action);command.Parameters.AddWithValue("device",NpgsqlDbType.Text,(object?)audit.DeviceId??DBNull.Value);command.Parameters.AddWithValue("correlation",audit.CorrelationId);command.ExecuteNonQuery();}
 public void Audit(AuditWrite audit){using var connection=Open();AppendAudit(connection,null,audit);}
 static string Script(string name){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Accounting.Api.database."+name)??throw new InvalidOperationException("Missing migration: "+name);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
 public void Initialize(){
  var target=new NpgsqlConnectionStringBuilder(connectionString);
  if(string.IsNullOrWhiteSpace(target.Database))throw new InvalidOperationException("PostgreSQL connection requires Database.");
  var admin=new NpgsqlConnectionStringBuilder(connectionString){Database="postgres"};
  using(var connection=new NpgsqlConnection(admin.ConnectionString)){
   connection.Open();using var exists=new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_database WHERE datname=@name)",connection);exists.Parameters.AddWithValue("name",target.Database);
   if(!(bool)exists.ExecuteScalar()!){using var create=new NpgsqlCommand("CREATE DATABASE "+new NpgsqlCommandBuilder().QuoteIdentifier(target.Database),connection);create.ExecuteNonQuery();}
  }
  using var db=new NpgsqlConnection(connectionString);db.Open();using var transaction=db.BeginTransaction();
  using(var guard=new NpgsqlCommand("SELECT pg_advisory_xact_lock(81729461)",db,transaction))guard.ExecuteNonQuery();
  using var command=new NpgsqlCommand(Script("001_initial.sql"),db,transaction);command.ExecuteNonQuery();
  using var supplement=new NpgsqlCommand(Script("002_canteen_reports.sql"),db,transaction);supplement.ExecuteNonQuery();using var banks=new NpgsqlCommand(Script("003_banks.sql"),db,transaction);banks.ExecuteNonQuery();using var audit=new NpgsqlCommand(Script("004_audit.sql"),db,transaction);audit.ExecuteNonQuery();transaction.Commit();
 }
 public Database? Load(){
  using var connection=Open();using var transaction=connection.BeginTransaction(IsolationLevel.RepeatableRead);
  using var version=new NpgsqlCommand("SELECT revision FROM store_state WHERE id=1",connection,transaction);revision=Convert.ToInt64(version.ExecuteScalar());
  using var command=new NpgsqlCommand(Script("load.sql"),connection,transaction);
  var result=JsonSerializer.Deserialize<Database>((string)command.ExecuteScalar()!,json)!;transaction.Commit();return result.Accounts.Count==0?null:result;
 }
 public void Save(Database database,AuditWrite? audit=null){
  using var connection=Open();using var transaction=connection.BeginTransaction(IsolationLevel.Serializable);
  using var version=new NpgsqlCommand("UPDATE store_state SET revision=revision+1 WHERE id=1 AND revision=@revision",connection,transaction);version.Parameters.AddWithValue("revision",revision);
  if(version.ExecuteNonQuery()!=1)throw new LedgerConflictException();
  using var command=new NpgsqlCommand(Script("save.sql"),connection,transaction){CommandTimeout=60};command.Parameters.AddWithValue("data",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(database,json));command.ExecuteNonQuery();if(audit is not null)AppendAudit(connection,transaction,audit);transaction.Commit();revision++;
 }
}
sealed class LedgerConflictException:Exception;

sealed record AuditWrite(string Actor,string Action,string? DeviceId,string CorrelationId);
