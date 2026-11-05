using Npgsql;
static class PostgresStoreSelfTest {
 public static void Run(string connectionString){
  var target=new NpgsqlConnectionStringBuilder(connectionString){Database="accounting_test_"+Guid.NewGuid().ToString("N"),Pooling=false};
  var admin=new NpgsqlConnectionStringBuilder(connectionString){Database="postgres",Pooling=false};
  try{
   var first=new PostgresLedgerStore(target.ConnectionString);first.Initialize();if(first.Load()!=null)throw new Exception("New database should be empty");first.Save(Seed.Create());
   var second=new PostgresLedgerStore(target.ConnectionString);var loaded=second.Load()!;
   if(loaded.Accounts.Count!=54||loaded.Documents.Count!=7)throw new Exception("Round-trip counts differ");
   first.Save(Seed.Create());try{second.Save(loaded);throw new Exception("Stale save accepted");}catch(LedgerConflictException){}
   loaded=second.Load()!;loaded.Documents.Add(loaded.Documents[0] with{Id="duplicate-number"});
   try{second.Save(loaded);throw new Exception("Duplicate number accepted");}catch(PostgresException e) when(e.SqlState=="23505"){}
   if(second.Load()!.Documents.Count!=7)throw new Exception("Transaction did not roll back");
   Console.WriteLine("PostgreSQL checks passed: migration, seed, reload, conflict and rollback.");
  }finally{using var connection=new NpgsqlConnection(admin.ConnectionString);connection.Open();using var cleanup=new NpgsqlCommand("DROP DATABASE IF EXISTS "+new NpgsqlCommandBuilder().QuoteIdentifier(target.Database!)+" WITH (FORCE)",connection);cleanup.ExecuteNonQuery();}
 }
}
