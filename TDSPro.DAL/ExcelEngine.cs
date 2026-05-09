using ClosedXML.Excel;
using TDSPro.Common;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// Excel Import / Export using ClosedXML.
    /// MIT License — 100% free, no Excel installation needed.
    /// No license key required.
    /// </summary>
    public static class ExcelEngine
    {
        // ── Theme colors ─────────────────────────────────────────────────────
        private static readonly XLColor HeaderBg    = XLColor.FromHtml("#1F3864");
        private static readonly XLColor HeaderFg    = XLColor.White;
        private static readonly XLColor AltRowBg    = XLColor.FromHtml("#F8FAFC");
        private static readonly XLColor TotalBg     = XLColor.FromHtml("#D6E4F0");
        private static readonly XLColor SampleBg    = XLColor.FromHtml("#FFFFCC");
        private static readonly XLColor GreenFg     = XLColor.FromHtml("#1E6B3C");
        private static readonly XLColor AmberFg     = XLColor.FromHtml("#7F4F24");
        private static readonly XLColor RedFg       = XLColor.FromHtml("#990000");
        private static readonly XLColor TallyHdrBg  = XLColor.FromHtml("#00467F");

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — TDS Entries
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportEntries(List<TdsEntry> entries, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TDS Entries");

            var headers = new[]
            {
                "Entry No", "Entry Date", "Deductor", "Deductee", "PAN",
                "Section", "Payment Nature", "Amount (Rs)", "Rate %",
                "TDS Amount", "Surcharge", "Cess", "Total TDS",
                "Interest", "Late Fee", "Due Date", "Payment Date",
                "Challan No", "Status", "Financial Year", "Quarter", "Remarks"
            };

            // Header row
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold       = true;
                cell.Style.Font.FontColor  = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderBg;
                cell.Style.Alignment.WrapText   = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder  = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.White;
            }
            ws.Row(1).Height = 30;

            // Data rows
            int row = 2;
            foreach (var e in entries)
            {
                ws.Cell(row, 1).Value  = e.EntryNo;
                ws.Cell(row, 2).Value  = e.EntryDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 3).Value  = e.DeductorName;
                ws.Cell(row, 4).Value  = e.DeducteeName;
                ws.Cell(row, 5).Value  = e.DeducteePan;
                ws.Cell(row, 6).Value  = e.Section;
                ws.Cell(row, 7).Value  = e.PaymentNature;
                ws.Cell(row, 8).Value  = e.Amount;
                ws.Cell(row, 9).Value  = e.Rate;
                ws.Cell(row, 10).Value = e.TdsAmount;
                ws.Cell(row, 11).Value = e.Surcharge;
                ws.Cell(row, 12).Value = e.Cess;
                ws.Cell(row, 13).Value = e.TotalTds;
                ws.Cell(row, 14).Value = e.Interest;
                ws.Cell(row, 15).Value = e.LateFee;
                ws.Cell(row, 16).Value = e.DueDate?.ToString("dd-MM-yyyy") ?? "";
                ws.Cell(row, 17).Value = e.PaymentDate?.ToString("dd-MM-yyyy") ?? "";
                ws.Cell(row, 18).Value = e.ChallanNo;
                ws.Cell(row, 19).Value = e.Status;
                ws.Cell(row, 20).Value = e.FinancialYear;
                ws.Cell(row, 21).Value = e.Quarter;
                ws.Cell(row, 22).Value = e.Remarks;

                // Alternate row shading
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;

                // Status color
                var statusColor = e.Status switch
                {
                    "Paid"    => GreenFg,
                    "Pending" => AmberFg,
                    _         => RedFg
                };
                ws.Cell(row, 19).Style.Font.FontColor = statusColor;
                ws.Cell(row, 19).Style.Font.Bold      = true;

                // Number format for amount columns
                var amtFmt = "#,##0.00";
                foreach (int col in new[] { 8, 9, 10, 11, 12, 13, 14, 15 })
                    ws.Cell(row, col).Style.NumberFormat.Format = amtFmt;

                row++;
            }

            // Total row
            if (entries.Count > 0)
            {
                int totalRow = row;
                ws.Cell(totalRow, 1).Value = "TOTAL";
                ws.Cell(totalRow, 1).Style.Font.Bold = true;
                ws.Cell(totalRow, 8).FormulaA1  = $"SUM(H2:H{totalRow - 1})";
                ws.Cell(totalRow, 13).FormulaA1 = $"SUM(M2:M{totalRow - 1})";
                ws.Cell(totalRow, 14).FormulaA1 = $"SUM(N2:N{totalRow - 1})";

                var totalRange = ws.Range(totalRow, 1, totalRow, headers.Length);
                totalRange.Style.Fill.BackgroundColor = TotalBg;
                totalRange.Style.Font.Bold            = true;

                foreach (int col in new[] { 8, 13, 14 })
                    ws.Cell(totalRow, col).Style.NumberFormat.Format = "#,##0.00";
            }

            // Freeze header, auto-fit columns
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 40);

            // Metadata
            wb.Properties.Author  = "TDS Pro";
            wb.Properties.Subject = $"TDS Entries Export — {DateTime.Today:dd-MMM-yyyy}";

            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Challans
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportChallans(List<Challan> challans, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Challans");

            var headers = new[]
            {
                "Challan No", "Date", "Deductor", "BSR Code", "Section",
                "Quarter", "TDS Amount", "Surcharge", "Cess", "Interest",
                "Late Fee", "Total Amount", "Bank Name", "Ack No",
                "Financial Year", "Status", "Remarks"
            };

            StyleHeaderRow(ws, 1, headers.Length);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            int row = 2;
            foreach (var c in challans)
            {
                ws.Cell(row, 1).Value  = c.ChallanNo;
                ws.Cell(row, 2).Value  = c.ChallanDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 3).Value  = c.DeductorName;
                ws.Cell(row, 4).Value  = c.BsrCode;
                ws.Cell(row, 5).Value  = c.Section;
                ws.Cell(row, 6).Value  = c.Quarter;
                ws.Cell(row, 7).Value  = c.TdsAmount;
                ws.Cell(row, 8).Value  = c.Surcharge;
                ws.Cell(row, 9).Value  = c.Cess;
                ws.Cell(row, 10).Value = c.Interest;
                ws.Cell(row, 11).Value = c.LateFee;
                ws.Cell(row, 12).Value = c.TotalAmount;
                ws.Cell(row, 13).Value = c.BankName;
                ws.Cell(row, 14).Value = c.AckNo;
                ws.Cell(row, 15).Value = c.FinancialYear;
                ws.Cell(row, 16).Value = c.Status;
                ws.Cell(row, 17).Value = c.Remarks;

                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;

                var statusColor = c.Status == "Paid" ? GreenFg : AmberFg;
                ws.Cell(row, 16).Style.Font.FontColor = statusColor;
                ws.Cell(row, 16).Style.Font.Bold = true;

                foreach (int col in new[] { 7, 8, 9, 10, 11, 12 })
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";

                row++;
            }

            // Totals
            if (challans.Count > 0)
            {
                var tr = row;
                ws.Cell(tr, 1).Value = "TOTAL";
                ws.Cell(tr, 7).FormulaA1  = $"SUM(G2:G{tr - 1})";
                ws.Cell(tr, 12).FormulaA1 = $"SUM(L2:L{tr - 1})";
                ws.Range(tr, 1, tr, headers.Length).Style.Fill.BackgroundColor = TotalBg;
                ws.Range(tr, 1, tr, headers.Length).Style.Font.Bold = true;
                foreach (int col in new[] { 7, 12 })
                    ws.Cell(tr, col).Style.NumberFormat.Format = "#,##0.00";
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 35);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Deductee Master
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportDeductees(List<Deductee> deductees, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Deductees");

            var headers = new[]
            {
                "Deductee Code", "Name", "PAN", "Section", "Rate %",
                "Deductee Type", "Resident", "Lower Cert No",
                "Lower Rate %", "Lower Cert Till", "Remarks"
            };

            StyleHeaderRow(ws, 1, headers.Length);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            int row = 2;
            foreach (var d in deductees)
            {
                ws.Cell(row, 1).Value  = d.DeducteeCode;
                ws.Cell(row, 2).Value  = d.Name;
                ws.Cell(row, 3).Value  = d.Pan;
                ws.Cell(row, 4).Value  = d.Section;
                ws.Cell(row, 5).Value  = d.Rate;
                ws.Cell(row, 6).Value  = d.DeducteeType;
                ws.Cell(row, 7).Value  = d.IsResident ? "Yes" : "No";
                ws.Cell(row, 8).Value  = d.LowerCertNo;
                ws.Cell(row, 9).Value  = d.LowerCertRate;
                ws.Cell(row, 10).Value = d.LowerCertTill;
                ws.Cell(row, 11).Value = d.Remarks;

                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;
                row++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 35);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Tally-compatible journal voucher format
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportTallyFormat(List<TdsEntry> entries, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Tally Import");

            var headers = new[]
            {
                "Date", "Voucher Type", "Voucher No", "Ledger Name",
                "Dr/Cr", "Amount", "TDS Section", "TDS Rate", "TDS Amount",
                "PAN of Party", "Party Name", "Narration"
            };

            StyleHeaderRow(ws, 1, headers.Length, TallyHdrBg);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            int row = 2;
            foreach (var e in entries)
            {
                // Dr line — expense / party
                ws.Cell(row, 1).Value  = e.EntryDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 2).Value  = "Journal";
                ws.Cell(row, 3).Value  = e.EntryNo;
                ws.Cell(row, 4).Value  = e.DeducteeName;
                ws.Cell(row, 5).Value  = "Dr";
                ws.Cell(row, 6).Value  = e.Amount;
                ws.Cell(row, 7).Value  = e.Section;
                ws.Cell(row, 8).Value  = e.Rate;
                ws.Cell(row, 9).Value  = e.TdsAmount;
                ws.Cell(row, 10).Value = e.DeducteePan;
                ws.Cell(row, 11).Value = e.DeducteeName;
                ws.Cell(row, 12).Value = $"TDS u/s {e.Section} on payment to {e.DeducteeName}";
                ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#1F3864");
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                row++;

                // Cr line — TDS payable
                ws.Cell(row, 1).Value  = e.EntryDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 2).Value  = "Journal";
                ws.Cell(row, 3).Value  = e.EntryNo;
                ws.Cell(row, 4).Value  = $"TDS Payable u/s {e.Section}";
                ws.Cell(row, 5).Value  = "Cr";
                ws.Cell(row, 6).Value  = e.TotalTds;
                ws.Cell(row, 7).Value  = e.Section;
                ws.Cell(row, 8).Value  = e.Rate;
                ws.Cell(row, 9).Value  = e.TdsAmount;
                ws.Cell(row, 10).Value = "";
                ws.Cell(row, 11).Value = "";
                ws.Cell(row, 12).Value = $"TDS payable u/s {e.Section}";
                ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#990000");
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                row++;

                // Blank separator row
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F6F9");
                row++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 40);
            wb.Properties.Author = "TDS Pro";
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Import Template (blank with instructions)
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportImportTemplate(string path, string templateType)
        {
            using var wb = new XLWorkbook();

            if (templateType == "entries")
            {
                var ws   = wb.Worksheets.Add("TDS Entries");
                var info = wb.Worksheets.Add("Instructions");

                var headers = new[]
                {
                    "Entry Date*\n(dd-MM-yyyy)",
                    "Deductor TAN*\n(e.g. DELA12345A)",
                    "Deductee PAN*\n(e.g. ABCDE1234F)",
                    "Section*\n(e.g. 194C)",
                    "Payment Amount*\n(numbers only)",
                    "TDS Rate*\n(e.g. 2.00)",
                    "Surcharge\n(0 if none)",
                    "Interest\n(0 if none)",
                    "Late Fee\n(0 if none)",
                    "Payment Date\n(dd-MM-yyyy)",
                    "Challan No\n(optional)",
                    "Status\n(Paid/Pending)",
                    "Financial Year*\n(e.g. 2024-25)",
                    "Quarter*\n(Q1/Q2/Q3/Q4)",
                    "Remarks\n(optional)"
                };

                StyleHeaderRow(ws, 1, headers.Length);
                for (int c = 0; c < headers.Length; c++)
                    ws.Cell(1, c + 1).Value = headers[c];
                ws.Row(1).Height = 36;

                // Sample row (yellow)
                var sample = new object[]
                {
                    "15-04-2024", "DELA12345A", "ABCDE1234F", "194C",
                    150000, 2.00, 0, 0, 0, "15-04-2024",
                    "12345", "Paid", "2024-25", "Q1",
                    "Sample row — DELETE before importing"
                };
                for (int c = 0; c < sample.Length; c++)
                    ws.Cell(2, c + 1).Value = XLCellValue.FromObject(sample[c]);

                ws.Range(2, 1, 2, headers.Length).Style.Fill.BackgroundColor = SampleBg;
                ws.Range(2, 1, 2, headers.Length).Style.Font.Italic = true;

                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents(12, 28);

                // Instructions sheet
                var instrData = new[]
                {
                    ("TDS Entry Import Template — Instructions", "", true),
                    ("", "", false),
                    ("MANDATORY FIELDS (marked with *)", "", true),
                    ("Entry Date",    "Format dd-MM-yyyy  e.g. 15-04-2024", false),
                    ("Deductor TAN",  "10-char TAN of the deductor company", false),
                    ("Deductee PAN",  "10-char PAN — format ABCDE1234F", false),
                    ("Section",       "TDS Section e.g. 194C 194J 194A 192", false),
                    ("Payment Amount","Amount paid (numbers only, no Rs symbol)", false),
                    ("TDS Rate",      "Rate in % e.g. 2.00 for 2%", false),
                    ("Financial Year","Format 2024-25", false),
                    ("Quarter",       "Q1 Q2 Q3 or Q4", false),
                    ("", "", false),
                    ("OPTIONAL FIELDS", "", true),
                    ("Surcharge",     "0 if not applicable", false),
                    ("Interest",      "Interest for late deduction", false),
                    ("Late Fee",      "Late filing fee u/s 234E", false),
                    ("Payment Date",  "Date TDS was deposited", false),
                    ("Challan No",    "Challan number for the payment", false),
                    ("Status",        "Paid or Pending (default: Pending)", false),
                    ("Remarks",       "Any additional notes", false),
                    ("", "", false),
                    ("IMPORTANT NOTES", "", true),
                    ("Row 2 (yellow)", "Sample row — DELETE before importing", false),
                    ("Deductor TAN",   "Must exist in Deductor Master first", false),
                    ("Deductee PAN",   "Must exist in Deductee Master first", false),
                    ("TDS Amount",     "Auto-calculated — do not add this column", false),
                    ("Cess",           "Auto-calculated at 4% — do not add", false),
                };

                for (int i = 0; i < instrData.Length; i++)
                {
                    var (label, value, bold) = instrData[i];
                    info.Cell(i + 1, 1).Value = label;
                    info.Cell(i + 1, 2).Value = value;
                    if (bold)
                    {
                        info.Cell(i + 1, 1).Style.Font.Bold = true;
                        info.Cell(i + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#1F3864");
                    }
                }
                info.Columns().AdjustToContents(15, 60);
            }
            else if (templateType == "deductees")
            {
                var ws = wb.Worksheets.Add("Deductees");
                var headers = new[]
                {
                    "Name*", "PAN*\n(ABCDE1234F)", "Section*\n(e.g. 194C)",
                    "Rate %*", "Deductee Type*\n(Individual/Company/Firm/HUF)",
                    "Resident\n(Yes/No)", "Lower Cert No",
                    "Lower Rate %", "Lower Cert Till\n(YYYY-MM-DD)", "Remarks"
                };

                StyleHeaderRow(ws, 1, headers.Length);
                for (int c = 0; c < headers.Length; c++)
                    ws.Cell(1, c + 1).Value = headers[c];
                ws.Row(1).Height = 36;

                // Sample
                var sRow = new object[]
                {
                    "Raj Kumar", "RAJKU1234A", "194C",
                    1.00, "Individual", "Yes", "", 0, "", "Sample — delete before import"
                };
                for (int c = 0; c < sRow.Length; c++)
                    ws.Cell(2, c + 1).Value = XLCellValue.FromObject(sRow[c]);

                ws.Range(2, 1, 2, headers.Length).Style.Fill.BackgroundColor = SampleBg;
                ws.Range(2, 1, 2, headers.Length).Style.Font.Italic = true;
                ws.SheetView.FreezeRows(1);
                ws.Columns().AdjustToContents(10, 30);
            }

            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // IMPORT — TDS Entries from Excel
        // ══════════════════════════════════════════════════════════════════════
        public static ImportResult ImportEntries(string path)
        {
            var result = new ImportResult();

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("TDS", StringComparison.OrdinalIgnoreCase) ||
                w.Name.Contains("Entries", StringComparison.OrdinalIgnoreCase) ||
                w.Name.Equals("Sheet1", StringComparison.OrdinalIgnoreCase)) ??
                wb.Worksheets.First();

            if (ws.LastRowUsed() == null)
            {
                result.Errors.Add("Worksheet is empty.");
                return result;
            }

            int lastRow  = ws.LastRowUsed()!.RowNumber();
            int startRow = FindHeaderRow(ws) + 1;
            if (startRow < 2) startRow = 2;

            using var conn = Database.GetConnection();

            for (int row = startRow; row <= lastRow; row++)
            {
                // Skip yellow sample rows and blank rows
                var firstCell = ws.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrEmpty(firstCell)) continue;
                if (ws.Cell(row, 1).Style.Fill.BackgroundColor.Color.Name == "ffff00" ||
                    ws.Cell(row, 15).GetString().Contains("Sample", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var dateStr  = ws.Cell(row, 1).GetString().Trim();
                    var tan      = ws.Cell(row, 2).GetString().Trim().ToUpper();
                    var pan      = ws.Cell(row, 3).GetString().Trim().ToUpper();
                    var section  = ws.Cell(row, 4).GetString().Trim().ToUpper();
                    var amtText  = ws.Cell(row, 5).GetString().Trim().Replace(",", "");
                    var rateText = ws.Cell(row, 6).GetString().Trim();

                    // Row validation
                    var rowErrs = new List<string>();
                    if (!DateTime.TryParseExact(dateStr,
                        new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "M/d/yyyy" },
                        null, System.Globalization.DateTimeStyles.None, out var entryDate))
                        rowErrs.Add($"Row {row}: Invalid date '{dateStr}' — use dd-MM-yyyy");

                    if (!double.TryParse(amtText, out var amount) || amount <= 0)
                        rowErrs.Add($"Row {row}: Invalid amount '{amtText}'");

                    if (string.IsNullOrEmpty(section))
                        rowErrs.Add($"Row {row}: Section is required");

                    if (!Validators.IsValidPan(pan))
                        rowErrs.Add($"Row {row}: Invalid PAN '{pan}'");

                    if (rowErrs.Count > 0)
                    {
                        result.Errors.AddRange(rowErrs);
                        result.FailCount++;
                        continue;
                    }

                    // Deductor lookup by TAN
                    using var c1 = conn.CreateCommand();
                    c1.CommandText = "SELECT id FROM deductors WHERE tan=@t AND is_active=1";
                    c1.Parameters.AddWithValue("@t", tan);
                    var deductorId = c1.ExecuteScalar();
                    if (deductorId == null)
                    {
                        result.Errors.Add($"Row {row}: Deductor TAN '{tan}' not found. Add to Deductor Master first.");
                        result.FailCount++;
                        continue;
                    }

                    // Deductee lookup by PAN
                    using var c2 = conn.CreateCommand();
                    c2.CommandText = "SELECT id FROM deductees WHERE pan=@p";
                    c2.Parameters.AddWithValue("@p", pan);
                    var deducteeId = c2.ExecuteScalar();
                    if (deducteeId == null)
                    {
                        result.Errors.Add($"Row {row}: Deductee PAN '{pan}' not found. Add to Deductee Master first.");
                        result.FailCount++;
                        continue;
                    }

                    // Parse optional fields
                    double.TryParse(rateText, out var rate);
                    double.TryParse(ws.Cell(row, 7).GetString().Replace(",", ""), out var surcharge);
                    double.TryParse(ws.Cell(row, 8).GetString().Replace(",", ""), out var interest);
                    double.TryParse(ws.Cell(row, 9).GetString().Replace(",", ""), out var lateFee);

                    var tdsAmt = Validators.CalculateTds(amount, rate);
                    var cess   = Validators.CalculateCess(tdsAmt);
                    var total  = tdsAmt + surcharge + cess + interest + lateFee;

                    DateTime.TryParseExact(
                        ws.Cell(row, 10).GetString().Trim(),
                        new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd" },
                        null, System.Globalization.DateTimeStyles.None, out var payDate);

                    var challanNo = ws.Cell(row, 11).GetString().Trim();
                    var status    = ws.Cell(row, 12).GetString().Trim();
                    if (status != "Paid" && status != "Pending" && status != "Overdue")
                        status = "Pending";

                    var fy      = ws.Cell(row, 13).GetString().Trim();
                    if (string.IsNullOrEmpty(fy)) fy = "2024-25";

                    var quarter = ws.Cell(row, 14).GetString().Trim().ToUpper();
                    if (!AppConstants.QuarterCodes.Contains(quarter)) quarter = "Q1";

                    var remarks = ws.Cell(row, 15).GetString().Trim();

                    // Generate entry number
                    using var cntCmd = conn.CreateCommand();
                    cntCmd.CommandText = "SELECT COUNT(*) FROM tds_entries";
                    var cnt     = (long)(cntCmd.ExecuteScalar() ?? 0L);
                    var entryNo = $"TDS{cnt + 1:D6}";

                    using var ins = conn.CreateCommand();
                    ins.CommandText = @"
                        INSERT INTO tds_entries
                        (entry_no, entry_date, deductor_id, deductee_id, section,
                         amount, rate, tds_amount, surcharge, cess, total_tds,
                         payment_date, interest, late_fee, challan_no,
                         status, financial_year, quarter, remarks)
                        VALUES
                        (@en,@ed,@di,@dei,@s,@am,@ra,@ta,@su,@ce,@tt,
                         @pd,@in,@lf,@cn,@st,@fy,@qt,@rm)";
                    ins.Parameters.AddWithValue("@en", entryNo);
                    ins.Parameters.AddWithValue("@ed", entryDate.ToString("yyyy-MM-dd"));
                    ins.Parameters.AddWithValue("@di", deductorId);
                    ins.Parameters.AddWithValue("@dei",deducteeId);
                    ins.Parameters.AddWithValue("@s",  section);
                    ins.Parameters.AddWithValue("@am", amount);
                    ins.Parameters.AddWithValue("@ra", rate);
                    ins.Parameters.AddWithValue("@ta", tdsAmt);
                    ins.Parameters.AddWithValue("@su", surcharge);
                    ins.Parameters.AddWithValue("@ce", cess);
                    ins.Parameters.AddWithValue("@tt", total);
                    ins.Parameters.AddWithValue("@pd", payDate != default ? payDate.ToString("yyyy-MM-dd") : "");
                    ins.Parameters.AddWithValue("@in", interest);
                    ins.Parameters.AddWithValue("@lf", lateFee);
                    ins.Parameters.AddWithValue("@cn", challanNo);
                    ins.Parameters.AddWithValue("@st", status);
                    ins.Parameters.AddWithValue("@fy", fy);
                    ins.Parameters.AddWithValue("@qt", quarter);
                    ins.Parameters.AddWithValue("@rm", remarks);
                    ins.ExecuteNonQuery();

                    result.SuccessCount++;
                    result.ImportedEntries.Add(entryNo);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                    result.FailCount++;
                }
            }

            Database.LogAction("system", "IMPORT_EXCEL", "TdsEntry",
                $"{result.SuccessCount} imported, {result.FailCount} failed");
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // IMPORT — Deductees from Excel
        // ══════════════════════════════════════════════════════════════════════
        public static ImportResult ImportDeductees(string path)
        {
            var result = new ImportResult();

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("Deductee", StringComparison.OrdinalIgnoreCase)) ??
                wb.Worksheets.First();

            if (ws.LastRowUsed() == null) { result.Errors.Add("Worksheet is empty."); return result; }

            int lastRow  = ws.LastRowUsed()!.RowNumber();
            int startRow = FindHeaderRow(ws) + 1;
            if (startRow < 2) startRow = 2;

            using var conn = Database.GetConnection();

            for (int row = startRow; row <= lastRow; row++)
            {
                try
                {
                    var name    = ws.Cell(row, 1).GetString().Trim();
                    var pan     = ws.Cell(row, 2).GetString().Trim().ToUpper();
                    var section = ws.Cell(row, 3).GetString().Trim().ToUpper();

                    if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(pan)) continue;
                    if (ws.Cell(row, 10).GetString().Contains("Sample", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!Validators.IsValidPan(pan))
                    {
                        result.Errors.Add($"Row {row}: Invalid PAN '{pan}' for '{name}'");
                        result.FailCount++;
                        continue;
                    }

                    double.TryParse(ws.Cell(row, 4).GetString(), out var rate);
                    var type     = ws.Cell(row, 5).GetString().Trim();
                    if (string.IsNullOrEmpty(type)) type = "Individual";
                    var resident = !ws.Cell(row, 6).GetString().Trim().Equals("No", StringComparison.OrdinalIgnoreCase);
                    var certNo   = ws.Cell(row, 7).GetString().Trim();
                    double.TryParse(ws.Cell(row, 8).GetString(), out var certRate);
                    var certTill = ws.Cell(row, 9).GetString().Trim();
                    var remarks  = ws.Cell(row, 10).GetString().Trim();

                    // Check if PAN exists
                    using var chk = conn.CreateCommand();
                    chk.CommandText = "SELECT id FROM deductees WHERE pan=@p";
                    chk.Parameters.AddWithValue("@p", pan);
                    var existing = chk.ExecuteScalar();

                    if (existing != null)
                    {
                        // Update
                        using var upd = conn.CreateCommand();
                        upd.CommandText = @"UPDATE deductees SET
                            name=@n, section=@s, rate=@r, deductee_type=@dt,
                            is_resident=@ir, lower_cert_no=@lc,
                            lower_cert_rate=@lr, lower_cert_till=@lt, remarks=@rm
                            WHERE pan=@p";
                        upd.Parameters.AddWithValue("@n",  name);
                        upd.Parameters.AddWithValue("@s",  section);
                        upd.Parameters.AddWithValue("@r",  rate);
                        upd.Parameters.AddWithValue("@dt", type);
                        upd.Parameters.AddWithValue("@ir", resident ? 1 : 0);
                        upd.Parameters.AddWithValue("@lc", certNo);
                        upd.Parameters.AddWithValue("@lr", certRate);
                        upd.Parameters.AddWithValue("@lt", certTill);
                        upd.Parameters.AddWithValue("@rm", remarks);
                        upd.Parameters.AddWithValue("@p",  pan);
                        upd.ExecuteNonQuery();
                        result.UpdatedCount++;
                    }
                    else
                    {
                        // Insert new
                        using var cnt = conn.CreateCommand();
                        cnt.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM deductees";
                        var nextId = (long)(cnt.ExecuteScalar() ?? 1L);
                        var code   = $"DED{nextId:D5}";

                        using var ins = conn.CreateCommand();
                        ins.CommandText = @"INSERT INTO deductees
                            (deductee_code, name, pan, section, rate, deductee_type,
                             is_resident, lower_cert_no, lower_cert_rate, lower_cert_till, remarks)
                            VALUES
                            (@dc,@n,@p,@s,@r,@dt,@ir,@lc,@lr,@lt,@rm)";
                        ins.Parameters.AddWithValue("@dc", code);
                        ins.Parameters.AddWithValue("@n",  name);
                        ins.Parameters.AddWithValue("@p",  pan);
                        ins.Parameters.AddWithValue("@s",  section);
                        ins.Parameters.AddWithValue("@r",  rate);
                        ins.Parameters.AddWithValue("@dt", type);
                        ins.Parameters.AddWithValue("@ir", resident ? 1 : 0);
                        ins.Parameters.AddWithValue("@lc", certNo);
                        ins.Parameters.AddWithValue("@lr", certRate);
                        ins.Parameters.AddWithValue("@lt", certTill);
                        ins.Parameters.AddWithValue("@rm", remarks);
                        ins.ExecuteNonQuery();
                        result.SuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                    result.FailCount++;
                }
            }

            Database.LogAction("system", "IMPORT_EXCEL", "Deductee",
                $"{result.SuccessCount} new, {result.UpdatedCount} updated, {result.FailCount} failed");
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════════
        private static void StyleHeaderRow(IXLWorksheet ws, int row, int cols,
            XLColor? bg = null)
        {
            var bgColor = bg ?? HeaderBg;
            var range   = ws.Range(row, 1, row, cols);
            range.Style.Font.Bold            = true;
            range.Style.Font.FontColor       = XLColor.White;
            range.Style.Fill.BackgroundColor = bgColor;
            range.Style.Alignment.WrapText   = true;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.White;
        }

        private static int FindHeaderRow(IXLWorksheet ws)
        {
            for (int row = 1; row <= Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1); row++)
            {
                var cell = ws.Cell(row, 1).GetString().ToLower();
                if (cell.Contains("date") || cell.Contains("entry") ||
                    cell.Contains("name") || cell.Contains("tan") || cell.Contains("pan"))
                    return row;
            }
            return 1;
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Full Year Salary Summary
        // ══════════════════════════════════════════════════════════════════════
        public static string ExportYearSummary(
            List<TDSPro.DAL.Models.EmployeeYearSummary> summary,
            string fy,
            string[] monthNames,
            int[]    monthNums)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Year Summary");

            // ── Title row ─────────────────────────────────────────────────────
            ws.Cell(1,1).Value = $"Full Year Salary & TDS Summary — FY {fy}";
            ws.Cell(1,1).Style.Font.Bold = true;
            ws.Cell(1,1).Style.Font.FontSize = 13;
            ws.Cell(1,1).Style.Font.FontColor = XLColor.FromArgb(23,52,140);

            // ── Header row ────────────────────────────────────────────────────
            int hRow = 3;
            var headers = new List<string> { "Code", "Employee", "PAN" };
            headers.AddRange(monthNames);
            headers.AddRange(new[]{ "Months Run","Total Gross","Total TDS","Total PF","Total Net" });

            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(hRow, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(23,52,140);
                cell.Style.Font.FontColor       = XLColor.White;
                cell.Style.Font.Bold            = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // ── Data rows ─────────────────────────────────────────────────────
            int row = hRow + 1;
            double grandGross=0, grandTds=0, grandPf=0, grandNet=0;

            foreach (var emp in summary)
            {
                ws.Cell(row, 1).Value = emp.EmployeeCode;
                ws.Cell(row, 2).Value = emp.EmployeeName;
                ws.Cell(row, 3).Value = emp.Pan;

                for (int mi = 0; mi < 12; mi++)
                {
                    int col = 4 + mi;
                    if (emp.MonthlyRuns.TryGetValue(monthNums[mi], out var run))
                    {
                        ws.Cell(row, col).Value = run.GrossSalary;
                        ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.FromArgb(22,101,52);
                        if (run.ProRataDays > 0)
                            ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromArgb(255,251,235);
                    }
                    else
                    {
                        ws.Cell(row, col).Value = "-";
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.LightGray;
                    }
                }

                int sc = 16;
                ws.Cell(row, sc).Value   = $"{emp.MonthsRun}/12";
                ws.Cell(row, sc+1).Value = emp.TotalGross;
                ws.Cell(row, sc+2).Value = emp.TotalTds;
                ws.Cell(row, sc+3).Value = emp.TotalPf;
                ws.Cell(row, sc+4).Value = emp.TotalNet;

                foreach (int c in new[]{sc+1, sc+2, sc+3, sc+4})
                    ws.Cell(row, c).Style.NumberFormat.Format = "#,##0";

                ws.Cell(row, sc+1).Style.Font.Bold = true;
                ws.Cell(row, sc+2).Style.Font.FontColor = XLColor.FromArgb(29,78,216);
                ws.Cell(row, sc+4).Style.Font.FontColor = XLColor.FromArgb(5,150,105);

                // Alternate row shading
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(249,251,253);

                grandGross += emp.TotalGross;
                grandTds   += emp.TotalTds;
                grandPf    += emp.TotalPf;
                grandNet   += emp.TotalNet;
                row++;
            }

            // ── Totals row ────────────────────────────────────────────────────
            ws.Cell(row, 2).Value = "TOTAL";
            ws.Cell(row, 2).Style.Font.Bold = true;
            int tsc = 16;
            foreach (var (col, val) in new[]{ (tsc+1, grandGross),(tsc+2, grandTds),(tsc+3, grandPf),(tsc+4, grandNet) })
            {
                ws.Cell(row, col).Value = val;
                ws.Cell(row, col).Style.Font.Bold = true;
                ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromArgb(219,234,254);
            }

            // ── Freeze + autofit ──────────────────────────────────────────────
            ws.SheetView.FreezeRows(hRow);
            ws.SheetView.FreezeColumns(3);
            ws.Columns().AdjustToContents(8, 40);
            ws.Column(2).Width = 25;

            wb.Properties.Author  = "TDS Pro";
            wb.Properties.Subject = $"Year Summary {fy}";

            // Save to Reports folder
            string dir  = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TDSPro", fy.Replace("/","-"), "Reports");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"YearSummary_{fy.Replace("/","-")}_{DateTime.Today:yyyyMMdd}.xlsx");
            wb.SaveAs(path);
            return path;
        }
    }

    // ── Import result ─────────────────────────────────────────────────────────
    public class ImportResult
    {
        public int SuccessCount  { get; set; }
        public int UpdatedCount  { get; set; }
        public int FailCount     { get; set; }
        public List<string> Errors          { get; set; } = new();
        public List<string> ImportedEntries { get; set; } = new();
        public bool HasErrors => Errors.Count > 0;
        public string Summary =>
            $"{SuccessCount} imported, {UpdatedCount} updated, {FailCount} failed.";
    }
}
