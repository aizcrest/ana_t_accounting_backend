using System.Data;
using System.Reflection;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

// Small, single-workspace development store: atomic relational snapshot writes.
sealed class PostgresLedgerStore
{
 readonly string connectionString;
 readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);
 long revision;
 public PostgresLedgerStore(string connectionString){this.connectionString=connectionString;}
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
  using var command=new NpgsqlCommand(Script("001_initial.sql"),db,transaction);command.ExecuteNonQuery();transaction.Commit();
 }
 public Database? Load(){
  using var connection=new NpgsqlConnection(connectionString);connection.Open();using var transaction=connection.BeginTransaction(IsolationLevel.RepeatableRead);
  using var version=new NpgsqlCommand("SELECT revision FROM store_state WHERE id=1",connection,transaction);revision=Convert.ToInt64(version.ExecuteScalar());
  using var command=new NpgsqlCommand(Script("load.sql"),connection,transaction);
  var result=JsonSerializer.Deserialize<Database>((string)command.ExecuteScalar()!,json)!;transaction.Commit();return result.Accounts.Count==0?null:result;
 }
 public void Save(Database database){
  using var connection=new NpgsqlConnection(connectionString);connection.Open();using var transaction=connection.BeginTransaction(IsolationLevel.Serializable);
  using var version=new NpgsqlCommand("UPDATE store_state SET revision=revision+1 WHERE id=1 AND revision=@revision",connection,transaction);version.Parameters.AddWithValue("revision",revision);
  if(version.ExecuteNonQuery()!=1)throw new LedgerConflictException();
  using var command=new NpgsqlCommand(Script("save.sql"),connection,transaction){CommandTimeout=60};command.Parameters.AddWithValue("data",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(database,json));command.ExecuteNonQuery();transaction.Commit();revision++;
 }
}
sealed class LedgerConflictException:Exception;
