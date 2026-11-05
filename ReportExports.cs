using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W=DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

static class ReportExports
{
 static ReportExports(){if(GlobalFontSettings.FontResolver==null)GlobalFontSettings.FontResolver=new ReportFontResolver();}
 static readonly CultureInfo Culture=CultureInfo.GetCultureInfo("en-PH");
 static string Text(object? v)=>v switch{decimal n=>n.ToString("N2",Culture),double n=>n.ToString("N2",Culture),_=>Convert.ToString(v,Culture)??""};
 static List<string> Heading(CanteenReport r)=>["Republic of the Philippines","Department of Education","REGION IV-A CALABARZON · SCHOOLS DIVISION OF BATANGAS",r.School,r.Title,$"{r.PeriodStart:dd MMM yyyy} – {r.PeriodEnd:dd MMM yyyy} | {(r.Category=="Management"?"Management report — no separate memo deadline":$"Due {r.DueDate:dd MMM yyyy}")}",$"School ID: {r.SchoolId} | Sub-office: {r.SubOffice}",$"{r.Profile} | {r.Status.ToUpperInvariant()} | Amounts in PHP"];
 public static byte[] Render(CanteenReport r,string format)=>format switch{"pdf"=>Pdf(r),"docx"=>Docx(r),"xlsx"=>Xlsx(r),_=>throw new ArgumentException("Unsupported export format")};
 static byte[] Xlsx(CanteenReport r){using var book=new XLWorkbook();var sheet=book.AddWorksheet("Report");var width=Math.Max(2,r.Sections.Max(s=>s.Columns.Count));int row=1;
  void Banner(string text,bool bold=false,string? fill=null){var range=sheet.Range(row,1,row,width);range.Merge();range.FirstCell().SetValue(text);range.Style.Alignment.WrapText=true;range.Style.Font.Bold=bold;if(fill!=null)range.Style.Fill.BackgroundColor=XLColor.FromHtml(fill);sheet.Row(row).Height=text.Length>100?42:24;row++;}
  foreach(var text in Heading(r))Banner(text,true);
  foreach(var warning in r.Warnings)Banner("DRAFT / REVIEW: "+warning,false,"#FFF4DA");
  foreach(var narrative in r.Narrative){foreach(var line in narrative.Split('\n'))Banner(line);row++;}
  foreach(var section in r.Sections){row++;Banner(section.Title,true,"#EAF3ED");for(int c=0;c<section.Columns.Count;c++){sheet.Cell(row,c+1).SetValue(section.Columns[c]);sheet.Cell(row,c+1).Style.Font.Bold=true;sheet.Cell(row,c+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#F2F5F3");}sheet.Row(row).Height=38;row++;
   foreach(var cells in section.Rows){for(int c=0;c<cells.Count;c++){var cell=sheet.Cell(row,c+1);if(cells[c] is decimal n){cell.SetValue(n);cell.Style.NumberFormat.Format="#,##0.00;[Red](#,##0.00)";}else if(cells[c] is int i)cell.SetValue(i);else cell.SetValue(Text(cells[c]));cell.Style.Alignment.Vertical=XLAlignmentVerticalValues.Top;}sheet.Row(row).Height=cells.Any(c=>Text(c).Length>70)?55:32;row++;}
  }
  foreach(var text in r.ClosingNarrative)foreach(var line in text.Split('\n'))Banner(line);
  row++;Banner("Prepared by: "+r.PreparedBy);Banner("School head: "+r.SchoolHead);Banner(r.Source);Banner("ONE COPY · PORTRAIT · Securely fastened in a folder. Verify originals before submission.");
  sheet.Range(1,1,row,width).Style.Font.FontName="DejaVu Sans";sheet.Range(1,1,row,width).Style.Font.FontSize=10;sheet.Range(1,1,row,width).Style.Alignment.WrapText=true;sheet.Columns(1,width).Width=width>4?22:42;sheet.SheetView.FreezeRows(8);sheet.PageSetup.PageOrientation=XLPageOrientation.Portrait;sheet.PageSetup.PaperSize=XLPaperSize.A4Paper;sheet.PageSetup.PagesWide=1;sheet.PageSetup.PagesTall=0;sheet.PageSetup.PrintAreas.Add(1,1,row,width);
  using var stream=new MemoryStream();book.SaveAs(stream);return stream.ToArray();
 }
 static byte[] Docx(CanteenReport r){using var stream=new MemoryStream();using(var doc=WordprocessingDocument.Create(stream,WordprocessingDocumentType.Document,true)){
  var main=doc.AddMainDocumentPart();var body=new W.Body();main.Document=new W.Document(body);
  W.Paragraph Para(string text,bool bold=false)=>new(new W.ParagraphProperties(new W.SpacingBetweenLines{After="100"}),new W.Run(new W.RunProperties(new W.RunFonts{Ascii="DejaVu Sans",HighAnsi="DejaVu Sans"},bold?new W.Bold():new W.Bold{Val=false},new W.FontSize{Val="18"}),new W.Text(text){Space=SpaceProcessingModeValues.Preserve}));
  foreach(var text in Heading(r))body.Append(Para(text,true));foreach(var warning in r.Warnings)body.Append(Para("DRAFT / REVIEW: "+warning,true));foreach(var narrative in r.Narrative)foreach(var line in narrative.Split('\n'))body.Append(Para(line));
  foreach(var section in r.Sections){body.Append(Para(section.Title,true));var table=new W.Table();table.Append(new W.TableProperties(new W.TableWidth{Width="10466",Type=W.TableWidthUnitValues.Dxa},new W.TableBorders(new W.TopBorder{Val=W.BorderValues.Single,Size=4},new W.BottomBorder{Val=W.BorderValues.Single,Size=4},new W.InsideHorizontalBorder{Val=W.BorderValues.Single,Size=4}),new W.TableLayout{Type=W.TableLayoutValues.Fixed}));var grid=new W.TableGrid();int cellWidth=10466/section.Columns.Count;foreach(var _ in section.Columns)grid.Append(new W.GridColumn{Width=cellWidth.ToString()});table.Append(grid);
   W.TableRow Row(IEnumerable<object?> cells,bool header=false){var row=new W.TableRow();if(header)row.Append(new W.TableRowProperties(new W.TableHeader()));foreach(var value in cells){var cell=new W.TableCell(new W.TableCellProperties(new W.TableCellWidth{Width=cellWidth.ToString(),Type=W.TableWidthUnitValues.Dxa}),Para(Text(value),header));row.Append(cell);}return row;}
   table.Append(Row(section.Columns,true));foreach(var cells in section.Rows)table.Append(Row(cells));body.Append(table);body.Append(Para(""));
  }
  foreach(var text in r.ClosingNarrative)foreach(var line in text.Split('\n'))body.Append(Para(line));
  body.Append(Para("Prepared by: "+r.PreparedBy));body.Append(Para("School head: "+r.SchoolHead));body.Append(Para(r.Source));body.Append(Para("ONE COPY · PORTRAIT · Securely fastened in a folder. Verify original supporting documents."));
  body.Append(new W.SectionProperties(new W.PageSize{Width=11906,Height=16838,Orient=W.PageOrientationValues.Portrait},new W.PageMargin{Top=720,Bottom=720,Left=720,Right=720}));main.Document.Save();
 }return stream.ToArray();}
 static byte[] Pdf(CanteenReport r){using var pdf=new PdfDocument();pdf.Info.Title=r.Title;pdf.Info.Subject="School canteen report · "+r.Status;pdf.Info.Author=r.School;
  PdfPage page=null!;XGraphics graphics=null!;double y=0;var font=new XFont("Report",8.5,XFontStyleEx.Regular);var bold=new XFont("Report",8.5,XFontStyleEx.Bold);double left=34,width=527;
  void NewPage(){graphics?.Dispose();page=pdf.AddPage();page.Size=PdfSharp.PageSize.A4;page.Orientation=PdfSharp.PageOrientation.Portrait;graphics=XGraphics.FromPdfPage(page);y=34;graphics.DrawString($"DM 536, s. 2026 | {r.Status} | Page {pdf.PageCount}",font,XBrushes.Gray,new XPoint(left,818));}
  List<string> Wrap(string text,double max,XFont f){var result=new List<string>();foreach(var paragraph in text.Replace("\r","").Split('\n')){string line="";foreach(var word in paragraph.Split(' ')){var next=line.Length==0?word:line+" "+word;if(graphics.MeasureString(next,f).Width<=max){line=next;continue;}if(line!=""){result.Add(line);line="";}string chunk="";foreach(var ch in word){if(graphics.MeasureString(chunk+ch,f).Width>max){result.Add(chunk);chunk="";}chunk+=ch;}line=chunk;}result.Add(line);}return result;}
  void Paragraph(string text,bool strong=false){var f=strong?bold:font;foreach(var line in Wrap(text,width,f)){if(y>784)NewPage();graphics.DrawString(line,f,XBrushes.Black,new XPoint(left,y+10));y+=13;}y+=5;}
  NewPage();foreach(var line in Heading(r))Paragraph(line,true);foreach(var warning in r.Warnings)Paragraph("DRAFT / REVIEW: "+warning);foreach(var narrative in r.Narrative)Paragraph(narrative);
  foreach(var section in r.Sections){if(y>730)NewPage();Paragraph(section.Title,true);var col=width/section.Columns.Count;
   void TableRow(IEnumerable<object?> cells,bool header){var wrapped=cells.Select(c=>Wrap(Text(c),col-10,header?bold:font)).ToList();var max=wrapped.Max(c=>c.Count);for(int line=0;line<max;line++){if(y>784)NewPage();if(header)graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(236,244,238)),left,y,width,13);for(int c=0;c<wrapped.Count;c++)if(line<wrapped[c].Count)graphics.DrawString(wrapped[c][line],header?bold:font,XBrushes.Black,new XPoint(left+c*col+4,y+10));y+=13;}graphics.DrawLine(new XPen(XColor.FromArgb(224,231,226),.5),left,y+2,left+width,y+2);y+=6;}
   TableRow(section.Columns,true);foreach(var row in section.Rows){if(y>750){NewPage();TableRow(section.Columns,true);}TableRow(row,false);}y+=10;
  }
  foreach(var text in r.ClosingNarrative)Paragraph(text);
  Paragraph("Prepared by: "+r.PreparedBy);Paragraph("School head: "+r.SchoolHead);Paragraph(r.Source);Paragraph("ONE COPY · PORTRAIT · Securely fastened in a folder. Verify originals before submission.");graphics.Dispose();using var stream=new MemoryStream();pdf.Save(stream,false);return stream.ToArray();
 }
 sealed class ReportFontResolver:IFontResolver{
  public FontResolverInfo ResolveTypeface(string familyName,bool isBold,bool isItalic)=>new(isBold?"Bold":"Regular");
  public byte[] GetFont(string faceName){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Accounting.Api.Fonts.DejaVuSans"+(faceName=="Bold"?"-Bold":"")+".ttf")!;using var data=new MemoryStream();stream.CopyTo(data);return data.ToArray();}
 }
}
