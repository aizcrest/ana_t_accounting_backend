record BankAccount(string Id,string Name,string Branch,string AccountName,string AccountNumber,string GlAccount,bool Active);
record CheckRange(string Id,string BankId,string Prefix,long StartNumber,long EndNumber,int NumberWidth,string Description,bool Active);
record RangeStatus(bool Active);
static class Banking
{
 public static readonly string[] ReconciliationKinds=["Bank statement balance","Deposit in transit","Outstanding check","Bank credit not yet booked","Bank debit not yet booked"];
 public static BankAccount? Resolve(Database db,string? bankId){
  if(!string.IsNullOrWhiteSpace(bankId))return db.Banks.SingleOrDefault(b=>b.Id==bankId)??throw new ResourceNotFoundException();
  var profile=db.CanteenProfile??SchoolProfile.Default(db);return db.Banks.SingleOrDefault(b=>b.GlAccount==profile.BankAccount);
 }
 public static bool InScope(Database db,CanteenInput input,BankAccount? bank){
  if(bank is null)return input.BankId is null;
  var legacyGl=(db.CanteenProfile??SchoolProfile.Default(db)).BankAccount;
  return input.BankId==bank.Id||(input.BankId is null&&bank.GlAccount==legacyGl);
 }
 public static void ValidateBank(Database db,BankAccount bank,string? existingId=null){
  if(string.IsNullOrWhiteSpace(bank.Name)||string.IsNullOrWhiteSpace(bank.AccountName)||bank.Name.Length>200||bank.Branch.Length>200||bank.AccountName.Length>200||bank.AccountNumber.Length>80)throw new ArgumentException("Enter a bank name and account name; keep names under 200 characters and the account label under 80.");
  if(!db.Accounts.Any(a=>a.Code==bank.GlAccount&&a.Level==8&&a.Category=="Asset"))throw new ArgumentException("Select an existing level-eight asset GL account.");
  if(db.Banks.Any(b=>b.Id!=existingId&&b.GlAccount==bank.GlAccount))throw new ArgumentException("Each bank account needs a separate GL account so balances can be reconciled independently.");
  var prior=db.Banks.SingleOrDefault(b=>b.Id==existingId);
  if(prior!=null&&prior.GlAccount!=bank.GlAccount&&(db.CanteenInputs.Any(i=>InScope(db,i,prior)&&ReconciliationKinds.Contains(i.Kind))||db.CheckRanges.Any(r=>r.BankId==prior.Id)))throw new ArgumentException("This bank has reconciliation records or check ranges. Preserve its GL link and set up a new bank account instead.");
 }
 public static void ValidateRange(Database db,CheckRange range,string? existingId=null){
  if(!db.Banks.Any(b=>b.Id==range.BankId&&b.Active))throw new ArgumentException("Select an active bank account.");
  if(range.StartNumber<1||range.EndNumber<range.StartNumber||range.EndNumber>999999999999||range.NumberWidth<1||range.NumberWidth>12||range.EndNumber.ToString().Length>range.NumberWidth||range.Prefix.Length>12||range.Description.Length>300)throw new ArgumentException("Use a positive ascending range, a width of 1–12 digits large enough for the end number, and a prefix up to 12 characters.");
  if(db.CheckRanges.Any(r=>r.Id!=existingId&&r.BankId==range.BankId&&r.Prefix==range.Prefix&&range.StartNumber<=r.EndNumber&&range.EndNumber>=r.StartNumber))throw new ArgumentException("This check-number range overlaps an existing range, including inactive ranges.");
 }
 public static string Format(CheckRange range,long number)=>range.Prefix+number.ToString("D"+range.NumberWidth);
 public static void Map(WebApplication app){
  app.MapGet("/api/banks",(SchoolWorkspace workspace)=>{lock(workspace.Gate){var db=workspace.Data;return Results.Ok(new{banks=db.Banks,checkRanges=db.CheckRanges,glAccounts=db.Accounts.Where(a=>a.Level==8&&a.Category=="Asset"),reconciliationKinds=ReconciliationKinds});}});
  app.MapPost("/api/banks",(SchoolWorkspace workspace,BankAccount bank)=>{lock(workspace.Gate){var db=workspace.Data;ValidateBank(db,bank);bank=bank with{Id=Guid.NewGuid().ToString(),Name=bank.Name.Trim(),AccountName=bank.AccountName.Trim()};db.Banks.Add(bank);workspace.Save();return Results.Ok(bank);}});
  app.MapPut("/api/banks/{id}",(SchoolWorkspace workspace,string id,BankAccount bank)=>{lock(workspace.Gate){var db=workspace.Data;var index=db.Banks.FindIndex(b=>b.Id==id);if(index<0)return Results.NotFound();ValidateBank(db,bank,id);bank=bank with{Id=id,Name=bank.Name.Trim(),AccountName=bank.AccountName.Trim()};db.Banks[index]=bank;workspace.Save();return Results.Ok(bank);}});
  app.MapPost("/api/banks/check-ranges",(SchoolWorkspace workspace,CheckRange range)=>{lock(workspace.Gate){var db=workspace.Data;ValidateRange(db,range);range=range with{Id=Guid.NewGuid().ToString()};db.CheckRanges.Add(range);workspace.Save();return Results.Ok(range);}});
  app.MapPut("/api/banks/check-ranges/{id}",(SchoolWorkspace workspace,string id,CheckRange range)=>{lock(workspace.Gate){var db=workspace.Data;var index=db.CheckRanges.FindIndex(r=>r.Id==id);if(index<0)return Results.NotFound();if(db.CheckRanges[index].BankId!=range.BankId)throw new ArgumentException("A check range cannot be moved to another bank.");ValidateRange(db,range,id);range=range with{Id=id};db.CheckRanges[index]=range;workspace.Save();return Results.Ok(range);}});
  app.MapPut("/api/banks/check-ranges/{id}/status",(SchoolWorkspace workspace,string id,RangeStatus status)=>{lock(workspace.Gate){var db=workspace.Data;var index=db.CheckRanges.FindIndex(r=>r.Id==id);if(index<0)return Results.NotFound();var range=db.CheckRanges[index] with{Active=status.Active};if(status.Active)ValidateRange(db,range,id);db.CheckRanges[index]=range;workspace.Save();return Results.Ok(range);}});
  app.MapGet("/api/banks/{id}/reconciliation",(SchoolWorkspace workspace,string id,string month)=>{lock(workspace.Gate){var db=workspace.Data;var bank=Resolve(db,id)!;if(!DateOnly.TryParseExact(month+"-01","yyyy-MM-dd",out var start))throw new ArgumentException("Select a reporting month (YYYY-MM).");var end=start.AddMonths(1).AddDays(-1);return Results.Ok(new{bank,report=CanteenReports.Build(db,"bank-reconciliation",start,bankId:id),inputs=db.CanteenInputs.Where(i=>i.Date>=start&&i.Date<=end&&ReconciliationKinds.Contains(i.Kind)&&InScope(db,i,bank))});}});
 }
}
