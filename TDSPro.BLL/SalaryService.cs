using TDSPro.DAL;
using TDSPro.DAL.Models;
using TDSPro.Common;

namespace TDSPro.BLL
{
    public class SalaryService
    {
        private readonly SalaryRepository _repo = new();

        public void Save(MonthlySalaryEntry e) => _repo.Save(e);
        public MonthlySalaryEntry? Get(int empId, string fy, int month) => _repo.Get(empId, fy, month);
        public List<MonthlySalaryEntry> GetAllForFY(int empId, string fy) => _repo.GetAllForFY(empId, fy);

        // ── FY helpers ───────────────────────────────────────────────────────
        public static int[] FyMonths  => new[]{4,5,6,7,8,9,10,11,12,1,2,3};
        public static string[] FyMonthNames => new[]{"Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar"};

        public static int FyStartYear(string fy)
            => fy.Length >= 4 && int.TryParse(fy[..4], out int y) ? y : DateTime.Today.Year;

        public static int CalendarYear(string fy, int month)
        {
            int s = FyStartYear(fy);
            return month >= 4 ? s : s + 1;
        }

        public static int MonthsRemaining(string fy, int fromMonth)
        {
            int s = FyStartYear(fy);
            int fromY = CalendarYear(fy, fromMonth);
            var start = new DateTime(fromY, fromMonth, 1);
            var end   = new DateTime(s + 1, 3, 31);
            int months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
            return Math.Max(1, months);
        }

        // ── Compute monthly TDS ───────────────────────────────────────────────
        /// <summary>
        /// Fills TaxComputed, SurchargeAmt, CessAmt on the entry.
        /// Does NOT override TdsDeducted — caller decides.
        /// </summary>
        public AnnualComputation ComputeMonth(
            MonthlySalaryEntry entry,
            Employee emp,
            string fy,
            TaxDeclaration decl)
        {
            entry.RecalcGross();

            // Pull all saved months for this employee
            var saved = _repo.GetAllForFY(emp.Id, fy)
                             .Where(e => !(e.Month == entry.Month && e.Year == entry.Year))
                             .ToList();
            saved.Add(entry);  // include current month

            var result = ComputeAnnual(saved, emp, fy, decl, entry.Month);

            // Fill computed TDS breakdown on entry
            double tdsPerMonth = result.ThisMonthTds;
            entry.TaxComputed = tdsPerMonth;
            // Proportional surcharge / cess allocation
            double chosen = result.ChosenRegime == "New" ? result.NewRegime.TotalTax : result.OldRegime.TotalTax;
            if (chosen > 0)
            {
                double sc   = result.ChosenRegime == "New" ? result.NewRegime.Surcharge : result.OldRegime.Surcharge;
                double cess = result.ChosenRegime == "New" ? result.NewRegime.Cess      : result.OldRegime.Cess;
                entry.SurchargeAmt = Math.Round(tdsPerMonth * sc   / chosen);
                entry.CessAmt      = Math.Round(tdsPerMonth * cess / chosen);
            }
            return result;
        }

        // ── Annual dual-regime computation ────────────────────────────────────
        public AnnualComputation ComputeAnnual(
            List<MonthlySalaryEntry> entries,
            Employee emp,
            string fy,
            TaxDeclaration decl,
            int forMonth)
        {
            int fyStart = FyStartYear(fy);
            var fyMonths = FyMonths;

            // Separate actual (saved) months vs remaining (projected)
            var actualByMonth = entries.ToDictionary(e => e.Month);
            int currentFyIndex = Array.IndexOf(fyMonths, forMonth);
            if (currentFyIndex < 0) currentFyIndex = 0;   // fallback: treat as April
            int actualCount = 0; double projectionBasis = 0;

            // Sum actual gross (Apr to current month)
            double annualGrossActual = 0;
            for (int i = 0; i <= currentFyIndex; i++)
            {
                int m = fyMonths[i];
                if (actualByMonth.TryGetValue(m, out var e)) { annualGrossActual += e.GrossTaxableSalary; actualCount++; }
            }
            // Projection basis = current month's gross taxable salary
            projectionBasis = actualByMonth.TryGetValue(forMonth, out var cur) ? cur.GrossTaxableSalary : 0;

            // Project remaining months (after current)
            int projectedMonths = 12 - currentFyIndex - 1;
            double annualGrossProjected = projectionBasis * projectedMonths;

            double annualGross = annualGrossActual + annualGrossProjected;

            // Sum actual deductions
            double annualPf  = 0, annualPt  = 0, annualNps = 0;
            for (int i = 0; i <= currentFyIndex; i++)
            {
                int m = fyMonths[i];
                if (actualByMonth.TryGetValue(m, out var e))
                {
                    annualPf  += e.PfEmployee + e.VPF;
                    annualPt  += e.ProfessionalTax;
                    annualNps += e.NpsEmployer;
                }
            }
            // Project remaining months using current
            if (cur != null)
            {
                annualPf  += (cur.PfEmployee + cur.VPF) * projectedMonths;
                annualPt  += cur.ProfessionalTax        * projectedMonths;
                annualNps += cur.NpsEmployer             * projectedMonths;
            }

            // HRA exemption (monthly × 12 for simplicity, use current month data)
            double annualBasic = (cur?.Basic ?? 0) * 12;
            double hraExemption = PayrollService.CalcHraExemptionPublic(cur?.Basic ?? 0, cur?.HRA ?? 0, decl.RentPaid, decl.HraCityType) * 12;

            // Age category — needed for 80D self-limit (senior citizen gets ₹50K, others ₹25K)
            DateTime? dob = DateTime.TryParseExact(emp.DateOfBirth, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d1) ? d1 :
                (DateTime.TryParse(emp.DateOfBirth, out var d2) ? d2 : (DateTime?)null);
            var ageCategory  = TDSPro.Common.TaxRules.GetAgeCategory(dob, fy);
            bool isSelfSenior = ageCategory != TDSPro.Common.AgeCategory.Below60;

            // 80D limits: ₹50K if employee is senior citizen, ₹25K otherwise (Section 80D)
            double limit80DSelf    = isSelfSenior ? 50000 : 25000;
            // 80D parents: ₹50K if parent is senior citizen, ₹25K otherwise
            double limit80DParents = decl.IsParentSeniorCitizen ? 50000 : 25000;

            // ── OLD REGIME ────────────────────────────────────────────────────
            // Old regime std deduction: ₹50,000 for all FYs (old regime never raised to ₹75K)
            var oldRules = TDSPro.Common.TaxRules.GetRules(fy, false, ageCategory);
            double stdDedOld = oldRules.StandardDeduction;

            // Chapter VI-A deductions (old regime only — all applicable sections)
            double chap6a = Math.Min(decl.Sec80C + annualPf, 150000)          // 80C: LIC/PF/ELSS — PF counts, cap ₹1.5L
                          + Math.Min(decl.Sec80D_Self,    limit80DSelf)         // 80D self/family health insurance
                          + Math.Min(decl.Sec80D_Parents, limit80DParents)      // 80D parents health insurance
                          + decl.Sec80G                                          // 80G donations (no cap for 100% eligible)
                          + Math.Min(decl.Sec80CCD_Employee, 50000)             // 80CCD(1B) additional NPS — ₹50K cap
                          + decl.Sec80E                                          // 80E education loan interest — no cap
                          + Math.Min(decl.Sec80EEA, 150000)                     // 80EEA housing loan first home — ₹1.5L cap
                          + Math.Min(decl.Sec80TTA, 10000)                      // 80TTA savings a/c interest — ₹10K (non-senior)
                          + Math.Min(decl.Sec80TTB, 50000)                      // 80TTB savings interest — ₹50K (senior citizen only)
                          + Math.Min(decl.Sec80DD,  125000)                     // 80DD differently abled dependent — ₹1.25L
                          + Math.Min(decl.Sec80U,   125000)                     // 80U self differently abled — ₹1.25L
                          + decl.LtaExemption                                    // LTA u/s 10(5) — old regime only
                          + decl.OtherDeductions;
            // 80CCD(2) — FY-aware: 10% old regime all years; 14% new regime from FY 2024-25
            double nps80CCD2    = Math.Min(annualNps, annualBasic * TDSPro.Common.TaxRules.Get80CCD2Rate(fy, false));
            double nps80CCD2New = Math.Min(annualNps, annualBasic * TDSPro.Common.TaxRules.Get80CCD2Rate(fy, true));

            double taxableOld = annualGross
                - stdDedOld - hraExemption - annualPt - chap6a - nps80CCD2
                + decl.IncomeOtherSources;
            taxableOld = Math.Max(0, taxableOld);

            var oldR = BuildRegime("Old Regime", taxableOld, annualGross, stdDedOld, hraExemption, annualPt, chap6a, nps80CCD2, decl.IncomeOtherSources, false, fy, ageCategory);

            // ── NEW REGIME — FY-aware std deduction and slabs ─────────────────
            // New regime: same slabs for all ages — no HRA, no LTA, no Chapter VI-A
            var newRules     = TDSPro.Common.TaxRules.GetRules(fy, true);
            double stdDedNew = newRules.StandardDeduction;
            double taxableNew = Math.Max(0, annualGross - stdDedNew - nps80CCD2New + decl.IncomeOtherSources);

            var newR = BuildRegime("New Regime", taxableNew, annualGross, stdDedNew, 0, 0, 0, nps80CCD2New, decl.IncomeOtherSources, true, fy);

            // YTD TDS
            int fromYear = CalendarYear(fy, forMonth);
            double ytd = _repo.GetYtdTds(emp.Id, fy, forMonth, fromYear);
            int remaining = MonthsRemaining(fy, forMonth);

            return new AnnualComputation
            {
                OldRegime        = oldR,
                NewRegime        = newR,
                ChosenRegime     = emp.TaxRegime ?? "New",
                YtdTdsDeducted   = ytd,
                MonthsRemaining  = remaining,
                ComputedForMonth = forMonth,
                MonthsActual     = actualCount,
                MonthsProjected  = projectedMonths,
            };
        }

        private static RegimeResult BuildRegime(
            string name, double taxableIncome, double grossSalary,
            double stdDed, double hraEx, double pt, double chap6a, double nps2, double otherSrc,
            bool isNew, string fy,
            TDSPro.Common.AgeCategory age = TDSPro.Common.AgeCategory.Below60)
        {
            var rules                = TDSPro.Common.TaxRules.GetRules(fy, isNew, age);
            double rawTax            = TDSPro.Common.TaxRules.ComputeSlabTax(taxableIncome, rules);
            var (taxAfter, rebate)   = TDSPro.Common.TaxRules.Apply87A(rawTax, taxableIncome, rules);
            double sc                = TDSPro.Common.TaxRules.CalcSurcharge(taxAfter, taxableIncome, rules);
            double cess              = Math.Round((taxAfter + sc) * 0.04);
            double total             = Math.Round(taxAfter + sc + cess);

            return new RegimeResult
            {
                RegimeName        = name,
                GrossSalary       = grossSalary,
                StandardDeduction = stdDed,
                HraExemption      = hraEx,
                ProfTaxDeduction  = pt,
                Chapter6A         = chap6a,
                NpsEmployer80CCD2 = nps2,
                IncomeOtherSources= otherSrc,
                TotalIncome       = taxableIncome,
                TaxOnIncome       = rawTax,
                Rebate87A         = rebate,
                TaxAfterRebate    = taxAfter,
                Surcharge         = sc,
                Cess              = cess,
                TotalTax          = total,
            };
        }
    }
}
