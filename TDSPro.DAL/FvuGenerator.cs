using TDSPro.Common;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// NSDL/Protean FVU File Generator for Form 24Q and 26Q.
    ///
    /// Record sequence (NSDL e-TDS RPU spec):
    ///   FH  — File Header          (one per file)
    ///   BH  — Batch Header         (one per batch/return)
    ///   CD  — Challan Detail       (one per challan)
    ///   DD  — Deductee Detail      (one per deductee, under each challan)
    ///   BC  — Batch Control        (one per batch — totals)
    ///   FC  — File Control         (one per file  — grand totals)
    ///
    /// Delimiter: pipe  |  (NSDL RPU v9.x spec)
    /// Amounts:   in paise (multiply Rs by 100, no decimal point)
    /// Dates:     dd-MM-yyyy
    /// </summary>
    public static class FvuGenerator
    {
        // ── Public entry point ────────────────────────────────────────────────
        public static string Generate(ReturnData data)
        {
            return data.Header.FormType.ToUpper() switch
            {
                "26Q"  => Build(data, "26Q",  false),
                "24Q"  => Build(data, "24Q",  false),
                "138"  => Build(data, "138",  true),   // IT Act 2025 salary — Form 138 = 24Q equivalent
                "140"  => Build(data, "140",  true),   // IT Act 2025 non-salary — Form 140 = 26Q equivalent
                "27Q"  => Build(data, "27Q",  false),
                _      => throw new ArgumentException($"Unknown form type: {data.Header.FormType}")
            };
        }

        // ── Core builder (handles 24Q/26Q/138/140) ────────────────────────────
        private static string Build(ReturnData data, string formType, bool newAct)
        {
            var lines = new List<string>();
            var h     = data.Header;
            var fy    = FormatFY(h.FinancialYear);
            var ay    = AssessmentYear(h.FinancialYear);
            var today = DateTime.Today.ToString("dd-MM-yyyy");

            // FVU/RPU version — 9.4 for current act; 10.0 placeholder for IT Act 2025 forms
            string fvuVer = newAct ? "10.0" : "9.4";
            // Normalised form type for NSDL records (138→24Q equivalent, 140→26Q equivalent)
            string nsdlForm = formType switch { "138" => "24Q", "140" => "26Q", _ => formType };

            // Every record in the NSDL e-TDS file starts with a sequential 1-based line number.
            // FormValidator (iload 16 tableswitch) maps field-1 → qgd (line number), field-2 → record type tag.
            int lineNo = 0;
            string L() => (++lineNo).ToString();

            // ── FH — File Header ──────────────────────────────────────────────
            // FVU 9.4 NSDL FH field map (1-indexed, FormValidator tableswitch):
            //   1=lineNo  2=FH  3=SL1  4=R/C  5=ddMMyyyy  6=seq  7=D  8=TAN  9=batchCount
            //   10=RPU  11-17=empty hash fields  18=tzd (empty for Regular, PRN for Correction)
            // Fields 11-17 MUST be empty (not "0") — non-null triggers T-FV-1022 hash check.
            string returnType = data.Header.IsCorrection ? "C" : "R";
            var today8   = DateTime.Today.ToString("ddMMyyyy");
            var tanUpper = h.TanOfDeductor.PadRight(10).Trim().ToUpper();
            string tzd   = (data.Header.IsCorrection && !string.IsNullOrEmpty(data.Header.PreviousPrn))
                           ? data.Header.PreviousPrn.Trim() : "";
            // FH: exactly 15 fields after "FH" tag, with trailing "^" (verified vs FVU 9.4).
            // Fields 11-15 must be empty — non-empty hash fields trigger T-FV-1022.
            // PRN for correction returns goes in BH field 13, not in FH.
            lines.Add(string.Join("^", new[]
            {
                L(),          // field 1: line number
                "FH",         // field 2: record type
                "SL1",        // field 3
                returnType,   // field 4: R/C
                today8,       // field 5: ddMMyyyy
                "1",          // field 6
                "D",          // field 7
                tanUpper,     // field 8: TAN
                "1",          // field 9: batch count
                "NSDLRPU",    // field 10: RPU
                "",           // field 11
                "",           // field 12
                "",           // field 13
                "",           // field 14
                "",           // field 15
                "",           // field 16
                "",           // field 17
            }) + "^");

            // ── BH — Batch Header ─────────────────────────────────────────────
            // NSDL FVU 9.4: BH must have exactly 71 ^ chars (69 content fields after record tag).
            // Field positions (1-based after line# and "BH" tag):
            //   3=BatchNo  4=ChallanCount  5=FormType  6=TxnType  7=BatchUpdInd
            //   8=OrigPRN  9=PrevPRN  10=CurPRN  11=PRNDate  12=LastTAN  13=TAN
            //   14=ReceiptNo  15=PAN  16=AY  17=FY  18=Quarter
            //   19=DeductorName  20=Branch  21-24=Addr1-4  25=Addr5  26=State  27=PIN
            //   28=Email  29=STD  30=Phone  31=AddrChange  32=DeductorType
            //   33=RespName  34=RespDesig  35-39=RespAddr1-5  40=RespState  41=RespPIN
            //   42=RespEmail  43=RespMobile  44=RespSTD  45=RespPhone  46=RespAddrChange
            //   47=BatchTotalTDS  48=UnmatchedChallans  49=CountSD
            //   50-58=empty  59=RespPAN  60-71=empty
            string prn = h.IsCorrection ? (h.PreviousPrn ?? "").Trim() : "";
            double batchTds = data.Challans.Sum(c => c.TdsDeposited);
            string batchTdsStr = batchTds.ToString("F2"); // rupees with .00, e.g. "500000.00"
            // Split deductor address into up to 4 lines, max 25 chars each (NSDL BH field limit)
            string addr = Safe(h.DeductorAddress, 100);
            string addr1 = addr.Length > 25 ? addr[..25] : addr;
            string addr2 = addr.Length > 25 ? (addr.Length > 50 ? addr[25..50] : addr[25..]) : "";
            string addr3 = addr.Length > 50 ? (addr.Length > 75 ? addr[50..75] : addr[50..]) : "";
            string addr4 = addr.Length > 75 ? (addr.Length > 100 ? addr[75..100] : addr[75..]) : "";
            string stateCode = NsdlStateCode(h.DeductorState);
            lines.Add(PipeL(L(), "BH",
                "1",                                            //  3: Batch Number
                data.Challans.Count.ToString(),                 //  4: Count of Challan Records
                nsdlForm,                                       //  5: Form Number (24Q/26Q)
                "",                                             //  6: Transaction Type (empty=Regular)
                "",                                             //  7: Batch Updation Indicator (empty=Regular)
                "",                                             //  8: Original PRN (empty=Regular)
                h.PreviousPrn?.Trim() ?? "",                    //  9: Previous PRN (15-digit token from last quarter)
                prn,                                            // 10: Current PRN / Token (empty=Regular)
                "",                                             // 11: PRN Date
                "",                                             // 12: Last TAN (empty=Regular)
                tanUpper,                                       // 13: TAN of Deductor
                "",                                             // 14: Receipt Number (empty=online)
                h.PanOfDeductor.PadRight(10).Trim().ToUpper(), // 15: PAN of Deductor
                ay,                                             // 16: Assessment Year (202728)
                fy,                                             // 17: Financial Year (202627)
                h.Quarter.ToUpper(),                            // 18: Quarter (Q4)
                Safe(h.DeductorName, 75),                       // 19: Deductor Name
                "NA",                                           // 20: Branch / Division (mandatory for Regular)
                addr1,                                          // 21: Deductor Address Line 1
                addr2,                                          // 22: Deductor Address Line 2
                addr3,                                          // 23: Deductor Address Line 3
                addr4,                                          // 24: Deductor Address Line 4
                "",                                             // 25: Deductor Address Line 5
                stateCode,                                      // 26: Deductor State (2-digit NSDL code)
                h.DeductorPin.Trim(),                           // 27: Deductor PIN
                h.Email.Trim(),                                 // 28: Deductor Email
                "",                                             // 29: Deductor STD Code (leave empty; phone without STD is allowed)
                "",                                             // 30: Deductor Phone (omit to avoid T-FV-2213 STD mandatory)
                "N",                                            // 31: Change of Deductor Address (mandatory Y/N for Regular)
                DeductorCategory(h.DeductorType),               // 32: Deductor / Collector Type
                Safe(h.ResponsibleName, 75),                    // 33: Responsible Person Name
                Safe(h.Designation, 20),                        // 34: Responsible Person Designation (max 20)
                addr1,                                          // 35: Responsible Person Address 1 (mandatory for Regular)
                addr2,                                          // 36: Responsible Person Address 2
                addr3,                                          // 37: Responsible Person Address 3
                addr4,                                          // 38: Responsible Person Address 4
                "",                                             // 39: Responsible Person Address 5
                stateCode,                                      // 40: Responsible Person State (mandatory)
                h.DeductorPin.Trim(),                           // 41: Responsible Person PIN (mandatory)
                h.Email.Trim(),                                 // 42: Responsible Person Email
                h.Phone.Trim(),                                 // 43: Responsible Person Mobile (mandatory for non-A/S)
                "",                                             // 44: Responsible Person STD Code
                "",                                             // 45: Responsible Person Phone
                "N",                                            // 46: Change of Responsible Person Address
                batchTdsStr,                                    // 47: Batch Total TDS Deposited (rupees.paise)
                "0",                                            // 48: Unmatched Challan Count
                nsdlForm == "24Q" && h.Quarter == "Q4"
                    ? (data.SalaryDetails.Count > 0 ? data.SalaryDetails.Count : data.Deductees.Count).ToString() // 49: Count SD Records
                    : "",                                       // 49: empty for Q1/Q2/Q3 and 26Q
                nsdlForm == "24Q" && h.Quarter == "Q4"
                    ? data.TotalGrossSalary.ToString("F2")      // 50: Batch Total Gross Income for SD (24Q Q4)
                    : "",                                       // 50
                "N",                                            // 51: AO Approval (must be "N" for Regular)
                !string.IsNullOrEmpty(h.PreviousPrn) ? "Y" : "N", // 52: Has regular statement filed earlier
                "",                                             // 53: Last Deductor Type (empty for Regular)
                // 54: State Name — mandatory for S/E/H/N; must be empty for K/M/P/J/B/Q/F/T
                (new[]{"S","E","H","N"}.Contains(DeductorCategory(h.DeductorType)) ? stateCode : ""),
                "",                                             // 55: PAO Code (empty for non-govt)
                "",                                             // 56
                "",                                             // 57
                "",                                             // 58
                h.ResponsiblePan.PadRight(10).Trim().ToUpper(), // 59: Responsible Person PAN
                "",                                             // 60
                "",                                             // 61
                "",                                             // 62
                "",                                             // 63
                "",                                             // 64
                "",                                             // 65
                "",                                             // 66
                "",                                             // 67
                "",                                             // 68
                h.Gstin.Trim().ToUpper(),                       // 69: GSTIN (optional)
                nsdlForm == "24Q" && h.Quarter == "Q4" ? "0" : "", // 70: Count 194P Records (0 for no 194P employees)
                nsdlForm == "24Q" && h.Quarter == "Q4" ? "0.00" : "" // 71: Batch Total 194P Gross Income
            ));

            // ── CD + DD pairs — one CD per challan, DD records under it ───────
            // NSDL spec: each DD must appear under exactly one CD.
            // Entries linked to a challan go under that challan.
            // Entries with no challan link (00000/0000000) go under the first challan only.
            bool unlinkedAssigned = false;
            foreach (var ch in data.Challans)
            {
                // Linked entries: match this challan's serial number
                var linked = data.Deductees
                    .Where(d => !string.IsNullOrEmpty(d.ChallanNo) && d.ChallanNo == ch.ChallanNo)
                    .ToList();

                // Unlinked entries (no challan): assign to first challan only
                List<ReturnDeducteeDetail> unlinked = new();
                if (!unlinkedAssigned)
                {
                    unlinked = data.Deductees
                        .Where(d => string.IsNullOrEmpty(d.ChallanNo))
                        .ToList();
                    if (unlinked.Any()) unlinkedAssigned = true;
                }

                var deds = linked.Concat(unlinked).ToList();

                // Numeric-only OLTAS challan serial — strip any alpha prefix (e.g. "CHL001" → "00001")
                var numericChallan = new string(ch.ChallanNo.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(numericChallan)) numericChallan = ch.SlNo.ToString();
                numericChallan = numericChallan.PadLeft(5, '0')[^5..]; // exactly 5 digits

                // CD — Challan Detail (FVU 9.4: exactly 40 ^ chars = 38 content fields after lineNo + "CD")
                // Field layout (1-indexed, 1=lineNo, 2=CD tag):
                //  3=BatchNo  4=CDSeq  5=CountDeductees  6=NilChallanInd  7=UpdateInd(empty=Regular)
                //  8=Filler2(empty)  9=Filler3(empty)  10=Filler4(empty)
                //  11=LastBankChallanNo(empty=Regular)  12=BankChallanNo  13=LastTransferVoucher(empty)
                //  14=TransferVoucher(empty)  15=LastBSR(empty)  16=BSRCode  17=LastDate(empty)
                //  18=ChallanDate  19=Filler5(empty)  20=Filler6(empty)  21=SectionCode
                //  22=OltasTDS  23=OltasSurcharge  24=OltasCess  25=OltasInterest  26=OltasOthers
                //  27=TotalDepositPerChallan  28=LastTotalDeposit(empty=Regular)
                //  29=TotalTaxPerDeducteeAnnexure  30=TDSIncomeTax  31=TDSSurcharge  32=HealthEducationCess
                //  33=SumTotalTDS  34=TDSInterest  35=TDSOthers  36=ChequeDDNo(empty)
                //  37=ByBookEntryCash("N"=bank)  38=Remarks(empty=Regular)  39=Fee  40=MinorHeadCode
                bool isSalary = nsdlForm == "24Q" || nsdlForm == "138";
                double cdCess  = isSalary ? ch.Cess : 0;
                // OLTAS amounts: what was reported to OLTAS (== actual challan amounts)
                string oltasTds        = Rupees(ch.TdsDeposited);
                string oltasSurcharge  = Rupees(ch.Surcharge);
                string oltasCess       = Rupees(cdCess);
                string oltasInterest   = Rupees(ch.Interest);
                string oltasOthers     = Rupees(ch.LateFee);  // LateFee → "Others" in OLTAS
                double oltasTotal = ch.TdsDeposited + ch.Surcharge + cdCess + ch.Interest + ch.LateFee;
                string totalPerChallan = Rupees(oltasTotal);
                // Statement amounts (from deductee annexure): must match what DD records add up to
                double ddTds = deds.Sum(d => d.TdsDeducted);
                double ddSurcharge = deds.Sum(d => d.Surcharge);
                double ddCess = isSalary ? deds.Sum(d => d.Cess) : 0;
                double ddTotal = ddTds + ddSurcharge + ddCess;
                string stmtTds        = Rupees(ddTds);
                string stmtSurcharge  = Rupees(ddSurcharge);
                string stmtCess       = Rupees(ddCess);
                string stmtTotal      = Rupees(ddTotal);
                // Fee = 0.00 for regular filings (234E late fee is separate from section fee)
                lines.Add(PipeL(L(), "CD",
                    "1",                                            //  3: Batch Number (always 1)
                    ch.SlNo.ToString(),                             //  4: CD Sequential Record No
                    deds.Count.ToString(),                          //  5: Count of Deductee Records
                    "N",                                            //  6: NIL Challan Indicator (N=non-nil)
                    "",                                             //  7: Update Indicator (empty for Regular)
                    "",                                             //  8: Filler 2 (must be empty)
                    "",                                             //  9: Filler 3 (must be empty)
                    "",                                             // 10: Filler 4 (must be empty)
                    "",                                             // 11: Last Bank Challan No (empty for Regular)
                    numericChallan,                                 // 12: Bank Challan No (5-digit)
                    "",                                             // 13: Last Transfer Voucher No (empty)
                    "",                                             // 14: Transfer Voucher/DDO Serial (empty)
                    "",                                             // 15: Last Bank-Branch Code (empty for Regular)
                    ch.BsrCode.PadLeft(7, '0').Trim(),             // 16: Bank-Branch Code / BSR Code
                    "",                                             // 17: Last Date of Challan (empty for Regular)
                    ch.ChallanDate.ToString("ddMMyyyy"),            // 18: Date of Bank Challan (ddmmyyyy, no dashes)
                    "",                                             // 19: Filler 5 (must be empty)
                    "",                                             // 20: Filler 6 (must be empty)
                    // 21: Section code — must be EMPTY for 24Q (T-FV-3160); required for 26Q/27Q
                    nsdlForm == "24Q" ? "" : ChallanSectionCode(ch.Section, nsdlForm, newAct),
                    oltasTds,                                       // 22: Oltas TDS/TCS-Income Tax
                    oltasSurcharge,                                 // 23: Oltas TDS/TCS-Surcharge
                    oltasCess,                                      // 24: Oltas TDS/TCS-Cess
                    oltasInterest,                                  // 25: Oltas TDS/TCS-Interest
                    oltasOthers,                                    // 26: Oltas TDS/TCS-Others
                    totalPerChallan,                                // 27: Total of Deposit Amount as per Challan
                    "",                                             // 28: Last Total of Deposit (empty for Regular)
                    Rupees(ddTds),                                  // 29: Total Tax Deposit as per deductee annexure
                    stmtTds,                                        // 30: TDS/TCS-Income Tax (statement)
                    stmtSurcharge,                                  // 31: TDS/TCS-Surcharge (statement)
                    stmtCess,                                       // 32: Health and Education Cess (statement)
                    stmtTotal,                                      // 33: Sum Total Income Tax Deducted
                    "0.00",                                         // 34: TDS/TCS-Interest Amount (statement)
                    "0.00",                                         // 35: TDS/TCS-Others (statement)
                    "",                                             // 36: Cheque/DD No (empty for bank challan)
                    "N",                                            // 37: By Book entry/Cash (N=bank deposit)
                    "",                                             // 38: Remarks (must be empty for Regular)
                    "0.00",                                         // 39: Fee (u/s 234E; 0 for regular)
                    "200"                                           // 40: Minor Head Code (200=Regular TDS)
                ));

                // DD — Deductee Detail records under this challan
                int ddSeq = 1;
                foreach (var d in deds)
                {
                    if (nsdlForm == "26Q")
                        lines.Add(BuildDD26Q(d, ddSeq, newAct, L()));
                    else
                        lines.Add(BuildDD24Q(d, ddSeq, newAct, L()));
                    ddSeq++;
                }
            }

            // ── SD — Salary Detail (Annexure II) — mandatory for 24Q Q4 ─────────
            if (nsdlForm == "24Q" && h.Quarter == "Q4")
            {
                var sdList = data.SalaryDetails.Any()
                    ? data.SalaryDetails
                    : data.Deductees.Select(d => new ReturnSalaryDetail
                    {
                        Pan             = d.Pan,
                        Name            = d.Name,
                        ChallanNo       = d.ChallanNo,
                        Salary17_1      = d.AmountPaid,
                        GrossTotalIncome= d.AmountPaid,
                        TaxableIncome   = d.AmountPaid,
                        TaxPayable      = d.TdsDeducted,
                        TotalTaxPayable = d.TdsDeducted,
                        TdsDeducted     = d.TdsDeducted,
                        Cess            = 0,
                        Surcharge       = 0,
                        Chapter6ATotal  = 0,
                    }).ToList();

                int sdSeq = 1;
                foreach (var sd in sdList)
                {
                    // Find challan sl no for this SD (match by ChallanNo or first challan)
                    var ch = data.Challans.FirstOrDefault(c => c.ChallanNo == sd.ChallanNo)
                          ?? data.Challans.FirstOrDefault();
                    string sdChallanSlNo = ch?.SlNo.ToString() ?? "1";
                    lines.Add(BuildSD(sd, sdSeq, L(), sdChallanSlNo));
                    if (sd.StandardDeduction > 0)
                        lines.Add(BuildS16(sd, sdSeq, L(), sdChallanSlNo));
                    sdSeq++;
                }
            }

            // ── BC — Batch Control (totals for this batch) ────────────────────
            bool isSalaryReturn = nsdlForm == "24Q";
            double bcCess  = isSalaryReturn ? data.Challans.Sum(c => c.Cess) : 0;
            double bcTotal = data.Challans.Sum(c => c.TdsDeposited)
                           + data.Challans.Sum(c => c.Surcharge)
                           + bcCess
                           + data.Challans.Sum(c => c.Interest)
                           + data.Challans.Sum(c => c.LateFee);
            lines.Add(PipeL(L(), "BC",
                data.Challans.Count.ToString(),                   // Total CD records
                data.Deductees.Count.ToString(),                  // Total DD records
                Paise(data.TotalAmountPaid),                      // BC[4] = gross amount paid to deductees
                Paise(data.TotalTdsDeducted),                     // BC[5] = total TDS deducted
                Paise(data.Challans.Sum(c => c.Surcharge)),       // Total surcharge
                Paise(bcCess),                                    // Total cess (0 for 26Q)
                Paise(data.Challans.Sum(c => c.Interest)),        // Total interest
                Paise(data.Challans.Sum(c => c.LateFee)),         // Total late fee
                Paise(bcTotal)                                    // Total deposited
            ));

            // ── FC — File Control (grand totals across all batches) ───────────
            lines.Add(PipeL(L(), "FC",
                "1",                                              // Total batches
                data.Challans.Count.ToString(),                   // Total CD records
                data.Deductees.Count.ToString(),                  // Total DD records
                Paise(data.TotalAmountPaid),                      // FC[5] = gross amount paid
                Paise(data.TotalTdsDeducted)                      // FC[6] = total TDS deducted
            ));

            return string.Join("\r\n", lines) + "\r\n";
        }

        // ── DD record for 26Q / Form 140 (non-salary) ────────────────────────
        private static string BuildDD26Q(ReturnDeducteeDetail d, int seq, bool newAct, string lineNo)
        {
            string section = newAct ? MapToNewActSection(d.Section) : d.Section;
            // 192/192A (salary sections) are invalid in 26Q — remap to 194J as closest non-salary section
            if (!newAct && (section == "192" || section == "192A"))
                section = "194J";

            // TDS amount in DD must equal AmountPaid × Rate exactly (no cess rolled in)
            double tdsAmt = Math.Round(d.AmountPaid * d.Rate / 100, 2);
            if (tdsAmt <= 0 && d.TdsDeducted > 0) tdsAmt = d.TdsDeducted; // fallback

            // Cess is 0 in 26Q DD records (cess not applicable for non-salary)
            // Surcharge only for specific high-value cases; keep as-is from data

            // Numeric-only challan serial for DD (same as CD rule)
            var numChallan = string.IsNullOrEmpty(d.ChallanNo) ? "00000" :
                new string(d.ChallanNo.Where(char.IsDigit).ToArray()).PadLeft(5, '0')[^5..];
            var bsrCode = string.IsNullOrEmpty(d.BsrCode) ? "0000000" :
                d.BsrCode.PadLeft(7, '0').Trim();

            return PipeL(lineNo, "DD",
                seq.ToString(),
                d.Pan.PadRight(10).Trim().ToUpper(),
                Safe(d.Name, 75),
                DeducteeCode(d.DeducteeType),
                section.PadRight(6).Trim(),
                d.PaymentDate.ToString("dd-MM-yyyy"),
                Paise(d.AmountPaid),
                Paise(tdsAmt),             // TDS = Amount × Rate
                Paise(tdsAmt),             // TDS deposited = same (cess not separate in 26Q)
                Paise(d.Surcharge),
                "0",                       // Cess = 0 for 26Q non-salary
                d.Rate.ToString("F2"),
                numChallan,
                bsrCode,
                d.IsResidentIndian ? "Y" : "N",
                ""                         // Remarks: keep blank — no extra tokens
            );
        }

        // ── DD record for 24Q / Form 138 (salary) ────────────────────────────
        private static string BuildDD24Q(ReturnDeducteeDetail d, int seq, bool newAct, string lineNo)
        {
            // FY-aware section code: 392(1) for IT Act 2025, 192 for old act
            string section = newAct ? "392" : "192";
            return PipeL(lineNo, "DD",
                seq.ToString(),
                d.Pan.PadRight(10).Trim().ToUpper(),
                Safe(d.Name, 75),
                DeducteeCode(d.DeducteeType),
                section,
                d.PaymentDate.ToString("dd-MM-yyyy"),
                Paise(d.AmountPaid),           // Gross salary
                Paise(d.AmountPaid),           // Taxable salary
                "0",                           // Perquisites
                "0",                           // Profits in lieu
                Paise(d.TdsDeducted),
                Paise(d.TdsDeposited),
                Paise(d.Surcharge),
                Paise(d.Cess),
                "0",                           // Relief u/s 89
                d.Rate.ToString("F2"),
                d.ChallanNo.PadLeft(5, '0').Trim(),
                d.BsrCode.PadLeft(7, '0').Trim(),
                d.IsResidentIndian ? "Y" : "N",
                Safe(d.Remarks, 10)
            );
        }

        // ── SD — Salary Detail record (Annexure II, 24Q Q4 only) ────────────────
        // 88 fields. Layout confirmed from reference NSDL RPU output (4I03886B.txt).
        private static string BuildSD(ReturnSalaryDetail sd, int seq, string lineNo, string challanSlNo)
        {
            string F(double v) => v.ToString("F2");
            double totalSal = sd.Salary17_1 + sd.Perquisites17_2 + sd.ProfitSalary17_3;
            double balanceAfter10 = Math.Max(0, totalSal - sd.ExemptU10);
            double stdDed = sd.StandardDeduction > 0 ? sd.StandardDeduction : 0;
            // GTI = balanceAfter10 + entertainment (stdDed u/s 16(ia) is in S16 record, NOT subtracted from GTI)
            double gti = sd.GrossTotalIncome > 0 ? sd.GrossTotalIncome : balanceAfter10;
            double tax = sd.TaxPayable > 0 ? sd.TaxPayable : 0;
            // field28 = tax liability - TDS deducted; T-FV-4202: field28 = field24+25+26-27-field78(TDS)
            double taxLiability = tax + sd.Surcharge + sd.Cess - sd.Rebate87A;
            double field28 = taxLiability - sd.TdsDeducted - sd.PrevEmpTds;
            double shortfall = field28 - sd.Chapter6ATotal;

            return PipeL(lineNo, "SD",
                challanSlNo,                                //  3: Challan Sl No
                seq.ToString(),                             //  4: Employee Sl No
                sd.EmployeeCategory,                        //  5: Employee Category (A/W/S/G/O)
                "",                                         //  6: PAN Ref (blank when PAN present)
                sd.Pan.Trim().ToUpper(),                    //  7: PAN
                "",                                         //  8: blank
                Safe(sd.Name, 75),                          //  9: Name
                sd.Gender,                                  // 10: Gender (M/F/T/G/S)
                sd.EmploymentFrom.ToString("ddMMyyyy"),     // 11: Period From
                sd.EmploymentTo.ToString("ddMMyyyy"),       // 12: Period To
                F(sd.Salary17_1),                           // 13: Salary u/s 17(1)
                sd.Perquisites17_2 == 0 ? "" : F(sd.Perquisites17_2), // 14: Perquisites (blank if zero)
                sd.ExemptU10Count.ToString(),               // 15: Count of u/s 10 allowances
                F(sd.ExemptU10),                            // 16: Total exempt u/s 10
                F(balanceAfter10),                          // 17: Balance after u/s 10 (13+14-16)
                "0.00",                                     // 18: Entertainment allowance u/s 16(ii) (Govt only)
                F(gti),                                     // 19: GTI (= field17 + field18; stdDed is in S16, not here)
                "",                                         // 20: blank
                "0",                                        // 21: Chapter VI-A count (int)
                "0.00",                                     // 22: Chapter VI-A total
                F(gti),                                     // 23: Gross Total Income
                F(tax),                                     // 24: Income Tax on total income
                F(sd.Surcharge),                            // 25: Surcharge
                F(sd.Cess),                                 // 26: Health & Education Cess
                F(sd.Rebate87A),                            // 27: Rebate u/s 87A
                F(field28),                                 // 28: Net tax after TDS (24+25+26-27-field78)
                F(sd.Chapter6ATotal),                       // 29: Chapter VI-A deductions
                F(shortfall),                               // 30: Shortfall(+)/Excess(-)
                "0.00",                                     // 31: 0.00
                "",                                         // 32: blank
                "",                                         // 33: blank
                F(totalSal),                                // 34: Gross Salary
                F(sd.PrevEmpSalary),                        // 35: Previous employer salary
                F(sd.Chapter6ATotal),                       // 36: Chapter VI-A total (repeat)
                "0.00",                                     // 37: 0.00
                sd.TaxRegime,                               // 38: Tax Regime (N/O)
                "N",                                        // 39: N
                sd.Chapter6ACount.ToString(),               // 40: Count of Chapter VI-A sub-records
                "", "", "", "", "", "", "", "",             // 41-48: blank (8)
                "N",                                        // 49: N
                "0",                                        // 50: 0 (int)
                "", "", "", "", "", "", "", "",             // 51-58: blank (8)
                "N",                                        // 59: N
                "", "", "", "", "", "", "",                 // 60-66: blank (7)
                F(totalSal),                                // 67: Total salary
                F(sd.PrevEmpSalary),                        // 68: Previous employer salary (repeat)
                "0.00",                                     // 69: 0.00
                "",                                         // 70: blank
                "0.00",                                     // 71: TDS cur emp (per-challan; 0 for annual SD)
                "0.00",                                     // 72: TDS previous employer
                "0.00",                                     // 73: TDS other
                "",                                         // 74: blank
                "0.00",                                     // 75: 0.00
                "0.00",                                     // 76: 0.00
                "0.00",                                     // 77: 0.00
                F(sd.TdsDeducted + sd.PrevEmpTds),          // 78: Total TDS (annual, used in T-FV-4202)
                sd.TaxRegime,                               // 79: Tax Regime (repeat)
                "0.00",                                     // 80: 0.00
                "0.00",                                     // 81: 0.00
                "", "", "", "", "", ""                      // 82-87: blank (6)
            );
        }

        // S16 sub-record — one per employee after SD. Reference: lineNo^S16^challanSlNo^empSlNo^1^16(ia)^amount^
        private static string BuildS16(ReturnSalaryDetail sd, int seq, string lineNo, string challanSlNo)
        {
            string F(double v) => v.ToString("F2");
            double stdDed = sd.StandardDeduction > 0 ? sd.StandardDeduction : 75000;
            return PipeL(lineNo, "S16",
                challanSlNo,            // 3: Challan Sl No
                seq.ToString(),         // 4: Employee Sl No
                "1",                    // 5: S16 sequential number within employee
                "16(ia)",               // 6: Section code (standard deduction)
                F(stdDed)               // 7: Deductible amount
            );
        }

        // ── Validation (pre-generation) ───────────────────────────────────────
        public static List<FvuValidationError> Validate(ReturnData data)
        {
            var errors = new List<FvuValidationError>();
            var h = data.Header;

            // Deductor checks
            if (!Validators.IsValidTan(h.TanOfDeductor))
                errors.Add(new("DEDUCTOR", "E001", $"Invalid TAN: '{h.TanOfDeductor}'. Format: AAAA99999A", true));

            if (!Validators.IsValidPan(h.PanOfDeductor))
                errors.Add(new("DEDUCTOR", "E002", $"Invalid PAN: '{h.PanOfDeductor}'. Format: AAAAA9999A", true));

            if (string.IsNullOrWhiteSpace(h.DeductorName))
                errors.Add(new("DEDUCTOR", "E003", "Deductor name is required.", true));

            if (string.IsNullOrWhiteSpace(h.ResponsiblePan) || !Validators.IsValidPan(h.ResponsiblePan))
                errors.Add(new("DEDUCTOR", "E004", $"Invalid Responsible Person PAN: '{h.ResponsiblePan}'.", true));

            // Challan checks
            if (data.Challans.Count == 0)
                errors.Add(new("CHALLAN", "E010", "No challans found for selected quarter. Add Challan 281 entries first.", true));

            foreach (var ch in data.Challans)
            {
                if (ch.BsrCode.Length != 7 || !ch.BsrCode.All(char.IsDigit))
                    errors.Add(new("CHALLAN", "E011", $"Challan {ch.ChallanNo}: BSR code must be exactly 7 digits. Found: '{ch.BsrCode}'", true));

                if (string.IsNullOrWhiteSpace(ch.ChallanNo))
                    errors.Add(new("CHALLAN", "E012", $"Challan {ch.SlNo}: Challan number is required.", true));

                if (ch.TdsDeposited <= 0)
                    errors.Add(new("CHALLAN", "E013", $"Challan {ch.ChallanNo}: TDS deposited must be > 0.", false));

                if (ch.ChallanDate > DateTime.Today)
                    errors.Add(new("CHALLAN", "E014", $"Challan {ch.ChallanNo}: Future date {ch.ChallanDate:dd-MM-yyyy} not allowed.", true));
            }

            // Deductee checks
            if (data.Deductees.Count == 0)
                errors.Add(new("DEDUCTEE", "E020", "No TDS entries found for selected quarter.", true));

            foreach (var d in data.Deductees)
            {
                if (!Validators.IsValidPan(d.Pan))
                    errors.Add(new("DEDUCTEE", "E021", $"Deductee '{d.Name}': Invalid PAN '{d.Pan}'.", true));

                if (d.AmountPaid <= 0)
                    errors.Add(new("DEDUCTEE", "E022", $"Deductee '{d.Name}': Amount paid is 0.", true));

                if (d.TdsDeducted < 0)
                    errors.Add(new("DEDUCTEE", "E023", $"Deductee '{d.Name}': Negative TDS amount.", true));

                if (!IsKnownSection(d.Section))
                    errors.Add(new("DEDUCTEE", "E024", $"Deductee '{d.Name}': Unknown section '{d.Section}'.", false));

                // 192 (salary) is invalid in 26Q — warn user
                var formT = data.Header.FormType.ToUpper();
                if ((formT == "26Q" || formT == "140") && (d.Section == "192" || d.Section == "192A"))
                    errors.Add(new("DEDUCTEE", "W025", $"Deductee '{d.Name}': Section 192 (salary) is invalid in {formT}. Remapped to 194J. Move to 24Q if this is a salary payment.", false));
            }

            // Reconciliation check
            double totalDeducted  = data.TotalTdsDeducted;
            double totalDeposited = data.Challans.Sum(c => c.TdsDeposited);
            double diff = Math.Abs(totalDeducted - totalDeposited);
            if (diff > 1.0)
                errors.Add(new("RECONCILIATION", "W001",
                    $"TDS deducted (Rs {totalDeducted:N2}) differs from challan deposited (Rs {totalDeposited:N2}). Difference: Rs {diff:N2}.",
                    false));

            return errors;
        }

        // ── Sample .txt content (for preview) ────────────────────────────────
        public static string GetSampleStructure(string formType = "26Q")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"// Sample NSDL FVU file structure — {formType}");
            sb.AppendLine($"// Generated by TDS Pro | Format: NSDL RPU v9.0 | Delimiter: |");
            sb.AppendLine();
            sb.AppendLine("FH|T|26Q|9.0|1|0");
            sb.AppendLine("BH|DELA12345A|202627|26Q|1|AAAPL1234C|C|12/04/2026|R|2|5|1248000|15000000");
            sb.AppendLine("DE|DELA12345A|AAAPL1234C|ABC PRIVATE LIMITED|14 CONNAUGHT PLACE NEW DELHI|110001|9876543210|...|RAHUL SHARMA|AAAPL1234C|RAHUL SHARMA|DIRECTOR|12/04/2026");
            sb.AppendLine("CD|1|0001234|15/04/2026|00123|4500000|0|180000|0|0|4680000|2|194C|0");
            sb.AppendLine("DD|1|RAJKU1234A|RAJ KUMAR AND SONS|02|194C|12/04/2026|15000000|150000|150000|0|6000|1.00|00123|0001234|Y|");
            sb.AppendLine("DD|2|PRIYA5678B|PRIYA CONSULTANTS|02|194J|18/04/2026|8000000|800000|800000|0|32000|10.00|00123|0001234|Y|");
            sb.AppendLine("BC|2|5|1248000|15000000|0|248000|0|0|1496000");
            sb.AppendLine("FC|1|2|5|1248000|15000000");
            return sb.ToString();
        }

        // ── File name ────────────────────────────────────────────────────────
        public static string GetFileName(ReturnData data)
        {
            var fy = data.Header.FinancialYear.Replace("-", "");
            return $"{data.Header.FormType}_{data.Header.TanOfDeductor}_{fy}_{data.Header.Quarter}.txt";
        }

        // ── IT Act 2025 section mapping (old → new) ────────────────────────
        private static readonly Dictionary<string, string> OldToNewSection =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["192"]="392",["192A"]="393",["193"]="393",["194"]="393",
            ["194A"]="393",["194B"]="393",["194BA"]="393",["194BB"]="393",
            ["194C"]="393",["194D"]="393",["194DA"]="393",["194G"]="393",
            ["194H"]="393",["194I"]="393",["194IA"]="393",["194IB"]="393",
            ["194IC"]="393",["194J"]="393",["194K"]="393",["194LA"]="393",
            ["194M"]="393",["194N"]="393",["194O"]="393",["194Q"]="393",
            ["194R"]="393",["194S"]="393",["195"]="393",["206AB"]="397",
        };

        private static readonly HashSet<string> NewActSectionCodes =
            new(StringComparer.OrdinalIgnoreCase)
            { "392","392(1)","393","393(1)","393(2)","394","395","396","397","397(3)","398","398(3)" };

        private static bool IsKnownSection(string s) =>
            string.IsNullOrEmpty(s) ||
            AppConstants.KnownSections.Contains(s.ToUpper()) ||
            NewActSectionCodes.Contains(s);

        private static string MapToNewActSection(string old)
        {
            if (NewActSectionCodes.Contains(old)) return old; // already new
            return OldToNewSection.TryGetValue(old, out var n) ? n : old;
        }

        private static string ChallanSectionCode(string section, string nsdlForm, bool newAct)
        {
            if (nsdlForm == "24Q") return newAct ? "392" : "192";
            if (newAct) return MapToNewActSection(section);
            return string.IsNullOrEmpty(section) ? "194C" : section.ToUpper().Trim();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        // PipeL: prepends line number as field 1, then record tag as field 2
        private static string PipeL(string lineNo, string tag, params string[] fields)
            => lineNo + "^" + tag + "^" + string.Join("^", fields.Select(f => (f ?? "").Replace("^", ""))) + "^";

        private static string Pipe(string tag, params string[] fields)
            => tag + "^" + string.Join("^", fields.Select(f => (f ?? "").Replace("^", ""))) + "^";

        private static string Paise(double amount)
            => ((long)Math.Round(amount * 100)).ToString();

        // Rupees: formats as "500000.00" (no paise, integer rupees only, .00 suffix required by FVU)
        private static string Rupees(double amount)
            => ((long)Math.Round(amount)).ToString() + ".00";

        private static string Safe(string s, int maxLen)
        {
            // NSDL FVU allows only alphanumeric + space in text fields.
            // Any other character causes T-FV-1022 hash validation failure.
            s = (s ?? "").Trim().ToUpper();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^A-Z0-9 ]", "").Trim();
            return s.Length > maxLen ? s[..maxLen] : s;
        }

        private static string FormatFY(string fy) => fy.Replace("-", "");

        private static string AssessmentYear(string fy)
        {
            // "2025-26" → "202627"
            var parts = fy.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out int y))
            {
                int nextYear = y + 1;
                int afterNext = nextYear + 1;
                return $"{nextYear}{afterNext.ToString()[^2..]}";
            }
            return fy.Replace("-", "");
        }

        private static string QuarterCode(string q) => q.TrimStart('Q');

        // Maps deductor type to NSDL category code.
        // FVU 9.4 valid codes: A,S,D,E,G,H,L,N,K,M,P,J,B,Q,F,T
        // K=Company, M=Branch/Division of Company, Q=Individual/HUF, F=Firm
        // A=Central Govt, S=State Govt, D/E=Statutory body, G/H=Autonomous body
        // L/N=Local Authority, P=AOP, T=Trust, J=Artificial Juridical Person, B=BOI
        private static string DeductorCategory(string type) => (type ?? "K").ToUpper() switch
        {
            "A" or "CENTRAL GOVT"            => "A",
            "S" or "STATE GOVT"              => "S",
            "D"                              => "D",
            "E"                              => "E",
            "G"                              => "G",
            "H"                              => "H",
            "L" or "LOCAL AUTHORITY CENTRAL" => "L",
            "N" or "LOCAL AUTHORITY STATE"   => "N",
            "K" or "COMPANY" or "C"          => "K", // Company
            "M" or "BRANCH"                  => "M", // Branch/Division of Company
            "P" or "AOP"                     => "P",
            "J" or "AJP"                     => "J",
            "B" or "BOI"                     => "B",
            "Q" or "INDIVIDUAL" or "HUF"     => "Q",
            "F" or "FIRM"                    => "F",
            "T" or "TRUST"                   => "T",
            _                                => "K"
        };

        // Maps state name / abbreviation → NSDL 2-digit state code (FVU 9.4).
        // State code table from FormValidator k.class (salary detail validator).
        // Note: code "08" (old Daman & Diu) is invalid in FVU >= 9.4; use "07" for Dadra+Daman.
        private static string NsdlStateCode(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return "09"; // Default to Delhi if unknown
            var s = state.Trim().ToUpper();
            // If already a valid 2-digit code 01-37 (excluding 08), return as-is
            if (int.TryParse(s, out int code) && code >= 1 && code <= 37 && code != 8)
                return s.PadLeft(2, '0');
            return s switch
            {
                "ANDAMAN AND NICOBAR" or "A&N"     => "01",
                "ANDHRA PRADESH" or "AP"            => "02",
                "ARUNACHAL PRADESH"                 => "03",
                "ASSAM"                             => "04",
                "BIHAR"                             => "05",
                "CHANDIGARH"                        => "06",
                "DADRA AND NAGAR HAVELI" or "DAMAN AND DIU" or "DADRA" => "07",
                "DELHI" or "DEL" or "DELI" or "NEW DELHI" => "09",
                "GOA"                               => "10",
                "GUJARAT"                           => "11",
                "HARYANA"                           => "12",
                "HIMACHAL PRADESH" or "HP"          => "13",
                "JAMMU AND KASHMIR" or "J&K" or "J AND K" => "14",
                "KARNATAKA"                         => "15",
                "KERALA"                            => "16",
                "LAKSHADWEEP" or "LAKSHWADEEP"      => "17",
                "MADHYA PRADESH" or "MP"            => "18",
                "MAHARASHTRA"                       => "19",
                "MANIPUR"                           => "20",
                "MEGHALAYA"                         => "21",
                "MIZORAM"                           => "22",
                "NAGALAND"                          => "23",
                "ODISHA" or "ORISSA"                => "24",
                "PUDUCHERRY" or "PONDICHERRY"       => "25",
                "PUNJAB"                            => "26",
                "RAJASTHAN"                         => "27",
                "SIKKIM"                            => "28",
                "TAMIL NADU" or "TAMILNADU" or "TN" => "29",
                "TRIPURA"                           => "30",
                "UTTAR PRADESH" or "UP"             => "31",
                "WEST BENGAL" or "WB"               => "32",
                "CHHATTISGARH" or "CHATTISGARH"     => "33",
                "UTTARAKHAND" or "UTTARANCHAL"      => "34",
                "JHARKHAND"                         => "35",
                "TELANGANA"                         => "36",
                "LADAKH"                            => "37",
                _                                   => "09" // Default to Delhi
            };
        }

        private static string DeducteeCode(string type) => (type ?? "") switch
        {
            "01"            => "01",   // Individual/HUF
            "02"            => "02",   // Company / Non-Individual
            "Company"       => "02",   // Company → code 02
            "NRI - Company" => "02",   // NRI Company → code 02
            "Individual"    => "01",
            "NRI"           => "01",
            _               => "01"    // default to Individual
        };
    }  // end FvuGenerator

    // ── Validation error model ─────────────────────────────────────────────────
    public class FvuValidationError
    {
        public string Category   { get; }
        public string Code       { get; }
        public string Message    { get; }
        public bool   IsBlocking { get; }

        public FvuValidationError(string category, string code, string message, bool blocking)
        {
            Category = category; Code = code;
            Message = message; IsBlocking = blocking;
        }

        public override string ToString() =>
            $"[{(IsBlocking ? "ERROR" : "WARN")}] {Code} — {Message}";
    }
}
