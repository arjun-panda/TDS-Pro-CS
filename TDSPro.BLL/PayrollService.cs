using TDSPro.DAL;
using TDSPro.DAL.Models;
using TDSPro.Common;

namespace TDSPro.BLL
{
    /// <summary>
    /// Salary computation engine.
    /// Supports both Old and New tax regimes for FY 2025-26 (AY 2026-27).
    /// Monthly TDS = (Total annual tax - YTD TDS) / remaining months.
    /// </summary>
    public class PayrollService
    {
        private readonly PayrollRepository _repo = new();

        // ── Professional Tax by state ─────────────────────────────────────────
        // States with simple flat monthly rates
        private static readonly Dictionary<string, double> PtFlat = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Maharashtra"]    = 200,
            ["Karnataka"]      = 200,
            ["West Bengal"]    = 200,
            ["Andhra Pradesh"] = 200,
            ["Telangana"]      = 200,
            ["Madhya Pradesh"] = 202,
            ["Odisha"]         = 200,
        };

        /// <summary>
        /// Returns monthly Professional Tax for the given state and monthly gross salary.
        /// Tamil Nadu and Gujarat use slab-based PT; all others use flat monthly rates.
        /// </summary>
        public  static double GetPtPublic(string state, double monthlyGross)
            => GetProfessionalTax(state, monthlyGross);
        private static double GetProfessionalTax(string state, double monthlyGross)
        {
            if (string.IsNullOrEmpty(state)) return 0;

            // Tamil Nadu — annual PT billed half-yearly; slabs based on annual gross
            // Annual gross:  ≤21,000 → nil | ≤30,000 → ₹270 | ≤45,000 → ₹630
            //                ≤60,000 → ₹1,380 | ≤75,000 → ₹2,050 | >75,000 → ₹2,500
            if (state.Equals("Tamil Nadu", StringComparison.OrdinalIgnoreCase))
            {
                double annualGross = monthlyGross * 12;
                double annualPt =
                    annualGross <= 21000 ? 0 :
                    annualGross <= 30000 ? 270 :
                    annualGross <= 45000 ? 630 :
                    annualGross <= 60000 ? 1380 :
                    annualGross <= 75000 ? 2050 :
                    2500;
                return Math.Round(annualPt / 12, 0);
            }

            // Gujarat — monthly slab on monthly gross
            // <6,000 → nil | 6,000–8,999 → ₹80 | 9,000–11,999 → ₹150 | ≥12,000 → ₹200
            if (state.Equals("Gujarat", StringComparison.OrdinalIgnoreCase))
            {
                return monthlyGross < 6000  ? 0 :
                       monthlyGross < 9000  ? 80 :
                       monthlyGross < 12000 ? 150 : 200;
            }

            return PtFlat.TryGetValue(state, out var flat) ? flat : 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        // COMPUTE MONTHLY PAYROLL
        // ══════════════════════════════════════════════════════════════════════
        public SalaryComputeResult Compute(Employee emp, int month, int year, string fy,
            int daysWorked = 0, int totalDays = 0, int deductorId = 0)
        {
            var ss   = emp.Salary ?? new SalaryStructure();
            var decl = _repo.GetDeclaration(emp.Id, fy);
            var ytd  = _repo.GetYtdTds(emp.Id, fy, month, deductorId);

            // ── Pro-rata scaling ──────────────────────────────────────────────
            // Indian payroll standard (30-day method): per-day = monthly ÷ 30.
            // daysWorked=30 in ANY month (28/30/31 calendar days) = full salary.
            // daysWorked=15 = 50% salary. daysWorked=0 = zero salary.
            // Factor capped at 1.0 — never overpay for months with 31 days.
            const int StdDays    = 30;
            double proRataFactor = 1.0;
            bool   isProRata     = daysWorked >= 0 && totalDays > 0 && daysWorked < StdDays;
            if (isProRata)
                proRataFactor = (double)daysWorked / StdDays;

            // Work on a scaled copy — never mutate the master salary structure
            var scaled = new SalaryStructure
            {
                EmployeeId       = ss.EmployeeId,
                Basic            = Math.Round(ss.Basic            * proRataFactor),
                Hra              = Math.Round(ss.Hra              * proRataFactor),
                Da               = Math.Round(ss.Da               * proRataFactor),
                SpecialAllowance = Math.Round(ss.SpecialAllowance * proRataFactor),
                MedicalAllowance = Math.Round(ss.MedicalAllowance * proRataFactor),
                Lta              = Math.Round(ss.Lta              * proRataFactor),
                OtherAllowance   = Math.Round(ss.OtherAllowance   * proRataFactor),
                PfApplicable     = ss.PfApplicable,
                PfFixedAmount    = ss.PfFixedAmount > 0 ? Math.Round(ss.PfFixedAmount * proRataFactor) : 0,
                EsiApplicable    = ss.EsiApplicable,
                PtState          = ss.PtState,
                EffectiveFrom    = ss.EffectiveFrom,
            };
            ss = scaled;   // use scaled for all calculations below

            // Months remaining in FY (including current)
            int fyStart     = fy.StartsWith("20") ? int.Parse(fy[..4]) : DateTime.Today.Year;
            int fyEndMonth  = 3;   // March
            int fyEndYear   = fyStart + 1;
            int remainMonths = MonthsRemaining(month, year, fyEndMonth, fyEndYear);

            // ── Annual projection ─────────────────────────────────────────────
            double annualGross   = ss.GrossSalary * 12;
            double annualBasic   = ss.Basic        * 12;
            double annualHra     = ss.Hra          * 12;
            double annualRent    = decl.RentPaid   * 12;

            // PF (employee: 12% of basic, capped at ₹1800/month if ESI applicable)
            // PF: use employee's fixed amount if set, otherwise auto 12% of Basic
            double pfMonthly = 0;
            if (ss.PfFixedAmount > 0 || ss.PfApplicable)
                pfMonthly = ss.PfFixedAmount > 0
                    ? ss.PfFixedAmount                  // fixed (e.g. capped at ₹1,800)
                    : Math.Round(ss.Basic * 0.12);      // auto 12% of basic
            double annualPf  = pfMonthly * 12;

            // ESI (employee: 0.75% of gross, applicable if gross ≤ ₹21,000/month)
            double esiMonthly = ss.EsiApplicable && ss.GrossSalary <= 21000
                ? Math.Round(ss.GrossSalary * 0.0075)
                : 0;

            // Professional tax
            double ptMonthly = GetProfessionalTax(ss.PtState, ss.GrossSalary);
            double annualPt  = ptMonthly * 12;

            // ── Load FY-specific rules ────────────────────────────────────────
            // Age-based slab selection (Section 192 — use correct old-regime slabs)
            DateTime? dob = DateTime.TryParseExact(emp.DateOfBirth, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d :
                (DateTime.TryParse(emp.DateOfBirth, out var d2) ? d2 : (DateTime?)null);
            var ageCategory = TDSPro.Common.TaxRules.GetAgeCategory(dob, fy);

            var oldRules    = TDSPro.Common.TaxRules.GetRules(fy, false, ageCategory);
            var newRules    = TDSPro.Common.TaxRules.GetRules(fy, true);
            double stdDedOld = oldRules.StandardDeduction;   // ₹50,000 — old regime, fixed for all FYs
            double stdDedNew = newRules.StandardDeduction;   // ₹75,000 from FY 2024-25 (new regime only)

            // ── OLD REGIME ────────────────────────────────────────────────────
            double hraExemption = CalcHraExemption(ss.Basic, ss.Hra, decl.RentPaid, decl.HraCityType);
            double hraExAnnual  = hraExemption * 12;

            // 80D self-limit: ₹50K for senior citizen employee, ₹25K for general
            // 80D parent-limit: ₹50K if parent is senior citizen, ₹25K otherwise
            double limit80DSelf    = (ageCategory != TDSPro.Common.AgeCategory.Below60) ? 50000 : 25000;
            double limit80DParents = decl.IsParentSeniorCitizen ? 50000 : 25000;
            if (dob.HasValue)
            {
                int fyEnd = int.Parse(fy.Split('-')[0]) + 1;
                int ageAtFYEnd = fyEnd - dob.Value.Year
                    - (new DateTime(fyEnd,3,31) < dob.Value.AddYears(fyEnd - dob.Value.Year) ? 1 : 0);
                if (ageAtFYEnd >= 60) limit80DSelf = 50000;
            }

            double chap6a = Math.Min(decl.Sec80C + annualPf, 150000)         // PF counts in 80C, cap ₹1.5L
                          + Math.Min(decl.Sec80D_Self,    limit80DSelf)        // 80D self
                          + Math.Min(decl.Sec80D_Parents, limit80DParents)     // 80D parents
                          + decl.Sec80G                                         // 80G donations
                          + Math.Min(decl.Sec80CCD_Employee, 50000)            // 80CCD(1B) ₹50K
                          + decl.Sec80E                                         // education loan — no limit
                          + Math.Min(decl.Sec80EEA, 150000)                    // housing loan first home ₹1.5L
                          + Math.Min(decl.Sec80TTA, 10000)                     // savings interest non-senior ₹10K
                          + Math.Min(decl.Sec80TTB, 50000)                     // savings interest senior ₹50K
                          + Math.Min(decl.Sec80DD, 125000)                     // differently abled dependent ₹1.25L max
                          + Math.Min(decl.Sec80U,  125000)                     // self differently abled ₹1.25L max
                          + decl.OtherDeductions;                               // any other declared

            // 80CCD(2) — employer NPS: FY-aware rate (10% up to FY2023-24, 14% from FY2024-25 new regime)
            double nps80CCD2Rate = TDSPro.Common.TaxRules.Get80CCD2Rate(fy, false); // old regime rate
            double nps80CCD2RateNew = TDSPro.Common.TaxRules.Get80CCD2Rate(fy, true); // new regime rate
            double npsEmployer  = Math.Min(decl.Sec80CCD_Employer, annualBasic * nps80CCD2Rate);

            double taxableOld = annualGross
                              - hraExAnnual
                              - stdDedOld
                              - annualPf
                              - annualPt
                              - chap6a
                              - npsEmployer
                              - decl.LtaExemption                  // LTA — old regime only
                              + decl.IncomeOtherSources;
            taxableOld = Math.Max(0, Math.Round(taxableOld));

            double rawTaxOld               = TDSPro.Common.TaxRules.ComputeSlabTax(taxableOld, oldRules);
            var   (taxAfterOld, r87AO)      = TDSPro.Common.TaxRules.Apply87A(rawTaxOld, taxableOld, oldRules);
            double surchargeOld             = TDSPro.Common.TaxRules.CalcSurcharge(taxAfterOld, taxableOld, oldRules);
            double cessOld                  = Math.Round((taxAfterOld + surchargeOld) * 0.04);
            double totalTaxOld              = taxAfterOld + surchargeOld + cessOld;
            double taxOld = taxAfterOld;

            // ── NEW REGIME — use FY-aware NPS rate (14% from FY 2024-25) ─────
            double npsEmployerNew = Math.Min(decl.Sec80CCD_Employer, annualBasic * nps80CCD2RateNew);
            double taxableNew = annualGross
                              - stdDedNew
                              - npsEmployerNew
                              + decl.IncomeOtherSources;
            taxableNew = Math.Max(0, Math.Round(taxableNew));

            double rawTaxNew               = TDSPro.Common.TaxRules.ComputeSlabTax(taxableNew, newRules);
            var   (taxAfterNew, r87AN)      = TDSPro.Common.TaxRules.Apply87A(rawTaxNew, taxableNew, newRules);
            double surchargeNew             = TDSPro.Common.TaxRules.CalcSurcharge(taxAfterNew, taxableNew, newRules);
            double cessNew                  = Math.Round((taxAfterNew + surchargeNew) * 0.04);
            double totalTaxNew              = taxAfterNew + surchargeNew + cessNew;
            double taxNew = taxAfterNew;

            // ── Choose regime ─────────────────────────────────────────────────
            bool useOld = emp.TaxRegime == "Old";
            double stdDed        = useOld ? stdDedOld : stdDedNew;
            double chosenTax     = useOld ? totalTaxOld : totalTaxNew;
            double taxableChosen = useOld ? taxableOld  : taxableNew;

            // Monthly TDS = (remaining annual tax - ytd) / remaining months
            double remainingTax = Math.Max(0, chosenTax - ytd);
            double monthlyTds   = remainMonths > 0
                ? Math.Round(remainingTax / remainMonths)
                : 0;

            var run = new PayrollRun
            {
                EmployeeId        = emp.Id,
                DeductorId        = deductorId,
                EmployeeName      = emp.Name,
                EmployeeCode      = emp.EmployeeCode,
                Pan               = emp.Pan,
                Month             = month,
                Year              = year,
                FinancialYear     = fy,
                Basic             = ss.Basic,
                Hra               = ss.Hra,
                Da                = ss.Da,
                Special           = ss.SpecialAllowance,
                Medical           = ss.MedicalAllowance,
                Lta               = ss.Lta,
                Other             = ss.OtherAllowance,
                GrossSalary       = ss.GrossSalary,
                PfEmployee        = pfMonthly,
                EsiEmployee       = esiMonthly,
                ProfessionalTax   = ptMonthly,
                TdsDeducted       = monthlyTds,
                TaxRegimeUsed     = emp.TaxRegime,
                HraExemption      = hraExemption,
                StandardDeduction = stdDed,
                Chapter6ADeduction= chap6a,
                TaxableIncome     = taxableChosen,
                AnnualTax         = useOld ? taxOld     : taxNew,
                Surcharge         = useOld ? surchargeOld : surchargeNew,
                Cess              = useOld ? cessOld    : cessNew,
                TotalAnnualTax    = chosenTax,
                YtdTds            = ytd,
                Status            = "Draft",
                ProRataDays       = isProRata ? daysWorked  : 0,
                ProRataTotal      = isProRata ? totalDays   : 0,
            };

            return new SalaryComputeResult
            {
                Run               = run,
                TaxOldRegime      = totalTaxOld,
                TaxNewRegime      = totalTaxNew,
                TaxableOld        = taxableOld,
                TaxableNew        = taxableNew,
                RecommendedRegime = totalTaxNew <= totalTaxOld ? "New" : "Old",
            };
        }

        /// <summary>Run payroll for all active employees of a deductor for the given month.</summary>
        public List<SalaryComputeResult> RunBulk(int deductorId, int month, int year, string fy,
            Dictionary<int,int>? proRataOverrides = null)
        {
            var salaryRepo = new SalaryRepository();
            var employees = _repo.GetAllEmployees(deductorId)
                .Where(e => e.IsActive)
                .Where(e =>
                {
                    var monthStart = new DateTime(year, month, 1);
                    var monthEnd   = new DateTime(year, month, DateTime.DaysInMonth(year, month));

                    // Skip if employee hasn't joined yet this month
                    if (!string.IsNullOrEmpty(e.JoinDate)
                        && DateTime.TryParse(e.JoinDate, out var jd))
                        if (jd > monthEnd) return false;

                    // Skip if employee left before this month started
                    if (!string.IsNullOrEmpty(e.LeavingDate)
                        && DateTime.TryParse(e.LeavingDate, out var ld))
                        if (monthStart > ld) return false;

                    return true;
                })
                .ToList();

            var results = new List<SalaryComputeResult>();
            int totalDays = DateTime.DaysInMonth(year, month);
            foreach (var emp in employees)
                if (emp.Salary != null)
                {
                    int daysWorked;
                    if (proRataOverrides != null && proRataOverrides.TryGetValue(emp.Id, out int d))
                    {
                        daysWorked = d; // explicit override takes priority
                    }
                    else if (!string.IsNullOrEmpty(emp.JoinDate)
                        && DateTime.TryParse(emp.JoinDate, out var jd)
                        && jd.Month == month && jd.Year == year)
                    {
                        // Joining month: pro-rate from joining day
                        // days worked = 30 - ((joinDay - 1) * 30 / daysInMonth) using 30-day standard
                        int calDaysWorked = totalDays - jd.Day + 1;
                        daysWorked = (int)Math.Round(calDaysWorked * 30.0 / totalDays);
                    }
                    else
                    {
                        daysWorked = -1; // full month
                    }

                    var result = Compute(emp, month, year, fy,
                        daysWorked < 0 ? 0 : daysWorked,
                        daysWorked < 0 ? 0 : 30,
                        deductorId);

                    // If user manually entered TDS in Salary Data, use it instead of computed value
                    var savedEntry = salaryRepo.Get(emp.Id, fy, month);
                    if (savedEntry != null && savedEntry.TdsDeducted > 0)
                        result.Run.TdsDeducted = savedEntry.TdsDeducted;

                    results.Add(result);
                }

            return results;
        }

        public void SaveRun(PayrollRun run) => _repo.SaveRun(run);

        public List<PayrollRun> GetRuns(int month, int year, int? deductorId = null)
            => _repo.GetRuns(month, year, deductorId);

        public List<PayrollRun> GetRunsForFY(string fy, int? deductorId = null)
            => _repo.GetRunsForFY(fy, deductorId);

        /// <summary>
        /// Returns a year summary: one row per employee, one column per month (Apr–Mar).
        /// Each cell is the PayrollRun for that employee+month, or null if not yet run.
        /// </summary>
        public List<EmployeeYearSummary> GetYearSummary(string fy, int? deductorId = null)
        {
            var runs = _repo.GetRunsForFY(fy, deductorId);

            // Group by employee
            var grouped = runs
                .GroupBy(r => new { r.EmployeeId, r.EmployeeName, r.EmployeeCode, r.Pan })
                .Select(g => new EmployeeYearSummary
                {
                    EmployeeId   = g.Key.EmployeeId,
                    EmployeeName = g.Key.EmployeeName,
                    EmployeeCode = g.Key.EmployeeCode,
                    Pan          = g.Key.Pan,
                    // If duplicate rows exist for same month, keep the one with the highest Id (latest save)
                    MonthlyRuns  = g.GroupBy(r => r.Month)
                                    .ToDictionary(mg => mg.Key, mg => mg.OrderByDescending(r => r.Id).First()),
                })
                .OrderBy(s => s.EmployeeName)
                .ToList();

            return grouped;
        }

        public List<Employee> GetEmployees(int? deductorId = null)
            => _repo.GetAllEmployees(deductorId);

        public (bool ok, string msg) SaveEmployee(Employee e)
            => _repo.SaveEmployee(e);

        public (bool ok, string msg) DeleteEmployee(int id)
            => _repo.DeleteEmployee(id);

        public TaxDeclaration GetDeclaration(int empId, string fy)
            => _repo.GetDeclaration(empId, fy);

        public void SaveDeclaration(TaxDeclaration d)
            => _repo.SaveDeclaration(d);

        /// <summary>Push processed payroll runs to tds_entries for 24Q filing.</summary>
        public int PushTo24Q(List<PayrollRun> runs, int deductorId)
        {
            int pushed = 0;
            var unpushed = runs.Where(r => r.TdsEntryId == null && r.TdsDeducted > 0).ToList();
            if (!unpushed.Any()) return 0;

            using var conn = Database.GetConnection();
            using var tx   = conn.BeginTransaction();
            try
            {
                foreach (var run in unpushed)
                {
                    // ── Resolve deductee_id — employee must exist in deductees table ──
                    // For salary entries, auto-create a shadow deductee if not present
                    int deducteeId = GetOrCreateDeducteeForEmployee(conn, tx, run, deductorId);
                    if (deducteeId <= 0) continue;  // can't create — skip

                    // ── FY-aware section code (strip "Section " prefix for storage) ──
                    string sectionCode = run.FinancialYear != null
                        && TDSPro.Common.TaxRules.IsNewAct(run.FinancialYear)
                        ? "392(1)" : "192";

                    using var cntCmd = conn.CreateCommand();
                    cntCmd.Transaction = tx;
                    cntCmd.CommandText = "SELECT COUNT(*) FROM tds_entries WHERE entry_no LIKE 'SAL%'";
                    var salCnt = (long)(cntCmd.ExecuteScalar() ?? 0L);
                    string entryNo = $"SAL{salCnt + 1:D6}";

                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO tds_entries
                            (entry_no, deductor_id, deductee_id, entry_date, section,
                             nature_of_payment, financial_year, quarter,
                             amount, rate, surcharge, cess, tds_amount, total_tds,
                             status, remarks)
                        VALUES
                            (@eno, @did, @deid, @dt, @sec,
                             'Salary', @fy, @qtr,
                             @amt, 0, @sc, @cess, @tds, @tds,
                             'Pending', @rem)";
                    cmd.Parameters.AddWithValue("@eno",  entryNo);
                    cmd.Parameters.AddWithValue("@did",  deductorId);
                    cmd.Parameters.AddWithValue("@deid", deducteeId);
                    cmd.Parameters.AddWithValue("@dt",   new DateTime(run.Year, run.Month, 1).ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@sec",  sectionCode);
                    cmd.Parameters.AddWithValue("@fy",   run.FinancialYear);
                    cmd.Parameters.AddWithValue("@qtr",  MonthToQuarter(run.Month));
                    cmd.Parameters.AddWithValue("@amt",  run.GrossSalary);
                    cmd.Parameters.AddWithValue("@sc",   run.Surcharge / 12.0);
                    cmd.Parameters.AddWithValue("@cess", run.Cess      / 12.0);
                    cmd.Parameters.AddWithValue("@tds",  run.TdsDeducted);
                    cmd.Parameters.AddWithValue("@rem",  $"PAYROLL {run.MonthLabel} {run.FinancialYear}");

                    int rows = cmd.ExecuteNonQuery();
                    if (rows > 0)
                    {
                        using var lid = conn.CreateCommand();
                        lid.Transaction = tx;
                        lid.CommandText = "SELECT last_insert_rowid()";
                        int entryId = Convert.ToInt32(lid.ExecuteScalar());
                        _repo.MarkAsPushed(run.Id, entryId, tx);
                        pushed++;
                    }
                    else
                    {
                        // INSERT OR IGNORE skipped a duplicate — still mark as pushed
                        using var existCmd = conn.CreateCommand();
                        existCmd.Transaction = tx;
                        existCmd.CommandText = "SELECT id FROM tds_entries WHERE entry_no=@eno";
                        existCmd.Parameters.AddWithValue("@eno", entryNo);
                        var existId = existCmd.ExecuteScalar();
                        if (existId != null)
                        {
                            _repo.MarkAsPushed(run.Id, Convert.ToInt32(existId), tx);
                            pushed++;
                        }
                    }
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
            return pushed;
        }

        /// <summary>
        /// Find or create a deductee record for a salary employee.
        /// Salary employees live in the employees table; tds_entries requires a deductee_id.
        /// We create a minimal deductee shadow record if one doesn't exist for the PAN.
        /// </summary>
        private static int GetOrCreateDeducteeForEmployee(
            Microsoft.Data.Sqlite.SqliteConnection conn,
            Microsoft.Data.Sqlite.SqliteTransaction tx,
            PayrollRun run, int deductorId)
        {
            if (string.IsNullOrEmpty(run.Pan)) return 0;
            string pan = run.Pan.Trim().ToUpper();

            // Check existing deductee by PAN (globally unique)
            using var chk = conn.CreateCommand();
            chk.Transaction = tx;
            chk.CommandText = "SELECT id FROM deductees WHERE pan=@pan LIMIT 1";
            chk.Parameters.AddWithValue("@pan", pan);
            var existing = chk.ExecuteScalar();
            if (existing != null) return Convert.ToInt32(existing);

            // Auto-create minimal shadow deductee for this salary employee
            // deductee_code = "EMP-{PAN}" ensures uniqueness
            string code = $"EMP-{pan}";
            string sec  = TDSPro.Common.TaxRules.IsNewAct(run.FinancialYear) ? "392(1)" : "192";

            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
                INSERT OR IGNORE INTO deductees
                    (deductee_code, name, pan, section, deductee_type,
                     is_resident, rate, deductor_id, remarks)
                VALUES (@code, @name, @pan, @sec, 'Individual',
                        1, 0, @did, 'Auto-created from payroll')";
            ins.Parameters.AddWithValue("@code", code);
            ins.Parameters.AddWithValue("@name", run.EmployeeName);
            ins.Parameters.AddWithValue("@pan",  pan);
            ins.Parameters.AddWithValue("@sec",  sec);
            ins.Parameters.AddWithValue("@did",  deductorId);
            ins.ExecuteNonQuery();

            // Return the id (whether newly created or already existed)
            using var lid = conn.CreateCommand();
            lid.Transaction = tx;
            lid.CommandText = "SELECT id FROM deductees WHERE pan=@pan LIMIT 1";
            lid.Parameters.AddWithValue("@pan", pan);
            var result = lid.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        // TAX SLABS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Old regime slabs — pure computation, no rebate inside.
        /// Slabs: 0-2.5L=0%, 2.5-5L=5%, 5-10L=20%, >10L=30%.
        /// </summary>
        private static double ComputeTaxOldRegime(double income)
        {
            if (income <= 250000) return 0;
            double tax = 0;
            if (income > 1000000) { tax += (income - 1000000) * 0.30; income = 1000000; }
            if (income > 500000)  { tax += (income - 500000)  * 0.20; income = 500000;  }
            if (income > 250000)  { tax += (income - 250000)  * 0.05; }
            return Math.Round(tax);
        }

        /// <summary>
        /// New regime slabs FY 2025-26 — pure computation, no rebate inside.
        /// Slabs: 0-4L=0%, 4-8L=5%, 8-12L=10%, 12-16L=15%, 16-20L=20%, 20-24L=25%, >24L=30%.
        /// </summary>
        private static double ComputeTaxNewRegime(double income)
        {
            if (income <= 400000) return 0;
            double tax = 0;
            if (income > 2400000) { tax += (income - 2400000) * 0.30; income = 2400000; }
            if (income > 2000000) { tax += (income - 2000000) * 0.25; income = 2000000; }
            if (income > 1600000) { tax += (income - 1600000) * 0.20; income = 1600000; }
            if (income > 1200000) { tax += (income - 1200000) * 0.15; income = 1200000; }
            if (income >  800000) { tax += (income - 800000)  * 0.10; income = 800000;  }
            if (income >  400000) { tax += (income - 400000)  * 0.05; }
            return Math.Round(tax);
        }

        /// <summary>
        /// Apply Section 87A rebate + marginal relief.
        /// Returns (taxAfterRebate, rebateAmount).
        /// Marginal relief: if income is just above the rebate threshold,
        /// tax is capped at (income − threshold) so the cliff is smoothed.
        /// </summary>
        private static (double taxAfter, double rebate) Apply87A(double tax, double income, bool isNew)
        {
            double threshold  = isNew ? 1200000 : 500000;
            double maxRebate  = isNew ? 60000   : 12500;

            if (income <= threshold)
            {
                // Full rebate — income is within the 0-tax zone
                double rebate = Math.Min(tax, maxRebate);
                return (Math.Max(0, tax - rebate), rebate);
            }
            else
            {
                // Marginal relief: effective tax cannot exceed (income − threshold)
                // This prevents the cliff where ₹1 extra income costs ₹60,000 tax
                double marginalCap = income - threshold;
                double capped      = Math.Min(tax, marginalCap);
                double rebate      = Math.Max(0, tax - capped);
                return (capped, rebate);
            }
        }

        private static double CalcSurcharge(double tax, double income)
        {
            if (income > 50000000) return Math.Round(tax * 0.37);
            if (income > 20000000) return Math.Round(tax * 0.25);
            if (income > 10000000) return Math.Round(tax * 0.15);
            if (income >  5000000) return Math.Round(tax * 0.10);
            return 0;
        }

        /// <summary>HRA exemption = min(actual HRA, 50%/40% basic, rent - 10% basic). Monthly.</summary>
        public  static double CalcHraExemptionPublic(double basic, double hra, double rent, string cityType)
            => CalcHraExemption(basic, hra, rent, cityType);
        private static double CalcHraExemption(double basic, double hra, double rent, string cityType)
        {
            if (rent <= 0 || hra <= 0) return 0;
            double pct    = cityType == "Metro" ? 0.50 : 0.40;
            double limit1 = hra;
            double limit2 = basic * pct;
            double limit3 = Math.Max(0, rent - basic * 0.10);
            return Math.Round(Math.Min(limit1, Math.Min(limit2, limit3)));
        }

        private static int MonthsRemaining(int month, int year, int endMonth, int endYear)
        {
            var start = new DateTime(year, month, 1);
            var end   = new DateTime(endYear, endMonth, 1);
            int diff  = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
            return Math.Max(1, diff);
        }

        private static string MonthToQuarter(int month)
        {
            return month switch
            {
                4 or 5 or 6  => "Q1",
                7 or 8 or 9  => "Q2",
                10 or 11 or 12 => "Q3",
                _            => "Q4",
            };
        }
    }
}
