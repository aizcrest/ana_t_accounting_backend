using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PdfSharp.Pdf.IO;

static class ReportExportValidation
{
 public static void Run(){var db=Seed.Create();var p=SchoolProfile.Default(db);db.CanteenProfile=p;var seen=new HashSet<string>();int count=0;
  foreach(var mode in new[]{("Non-IU",true,"School-managed"),("Non-IU",false,"School-managed"),("IU",true,"Cooperative"),("Non-IU",true,"Laboratory")}){
   p.UnitType=mode.Item1;p.HasBankAccount=mode.Item2;p.Operation=mode.Item3;
   foreach(var listing in CanteenReports.Catalog(db,new(2026,1,1)).Where(r=>r.Applicable)){
    if(!seen.Add(listing.Id))continue;var report=CanteenReports.Build(db,listing.Id,new(2026,1,1));
    foreach(var format in new[]{"pdf","docx","xlsx"}){
     using var data=new MemoryStream(ReportExports.Render(report,format));
     if(format=="pdf"){using var pdf=PdfReader.Open(data,PdfDocumentOpenMode.Import);if(pdf.PageCount==0||pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>().Any(page=>page.Width.Point>page.Height.Point))throw new Exception("Invalid portrait PDF: "+listing.Id);}
     else{using OpenXmlPackage package=format=="docx"?WordprocessingDocument.Open(data,false):SpreadsheetDocument.Open(data,false);var errors=new OpenXmlValidator().Validate(package).ToList();if(errors.Count>0)throw new Exception(listing.Id+" "+format+": "+errors[0].Description);}
     count++;
    }
   }
  }
  Console.WriteLine($"PASS {seen.Count} report types / {count} PDF, DOCX, XLSX exports. PDF portrait pages and Office schemas validated.");
 }
}
