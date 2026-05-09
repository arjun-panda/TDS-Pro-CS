namespace TDSPro.DAL.Models
{
    /// <summary>
    /// Complete per-month salary entry for one employee.
    /// Stores every component shown in the IITRET-style data entry form.
    /// </summary>
    public class MonthlySalaryEntry
    {
        public int    Id            { get; set; }
        public int    EmployeeId    { get; set; }
        public int    DeductorId    { get; set; }
        public string FinancialYear { get; set; } = "";
        public int    Month         { get; set; }   // calendar month 1-12
        public int    Year          { get; set; }   // calendar year

        // Navigation (joined from employees)
        public string EmployeeName  { get; set; } = "";
        public string Pan           { get; set; } = "";

        // ── EARNINGS (all monthly ₹) ─────────────────────────────────────────
        public double Basic             { get; set; }
        public double GradePay          { get; set; }   // for Govt employees
        public double HRA               { get; set; }
        public double DaPercent         { get; set; }   // DA as % of (Basic+GradePay)
        public double DaAmount          { get; set; }   // auto-computed or overridden

        public double SpecialAllowance  { get; set; }
        public double MedicalAllowance  { get; set; }
        public double Lta               { get; set; }   // Leave Travel Allowance

        public double Bonus             { get; set; }
        public double Commission        { get; set; }
        public double AdvanceSalary     { get; set; }   // advance received
        public double Arrears           { get; set; }   // arrears of salary
        public double OtherAllowances   { get; set; }
        public double NpsEmployer       { get; set; }   // employer NPS contribution

        // Perquisites (Section 17(2))
        public double PerqTotal         { get; set; }
        public double PerqExempted      { get; set; }
        public double PerqTaxable       => Math.Max(0, PerqTotal - PerqExempted);

        // Leave Encashment (Section 10(10AA))
        public double LeaveEncTotal     { get; set; }
        public double LeaveEncExempted  { get; set; }   // max ₹25L lifetime (FY 2023-24+)
        public double LeaveEncTaxable   => Math.Max(0, LeaveEncTotal - LeaveEncExempted);

        // ── DEDUCTIONS ───────────────────────────────────────────────────────
        public double PfEmployee        { get; set; }   // employee PF contribution
        public double VPF               { get; set; }   // voluntary PF (extra PF)
        public double ProfessionalTax   { get; set; }   // state PT
        public double EsiEmployee       { get; set; }   // employee ESI 0.75%

        // ── TDS ─────────────────────────────────────────────────────────────
        public double TaxComputed       { get; set; }   // computed by engine
        public double SurchargeAmt      { get; set; }
        public double CessAmt           { get; set; }
        public double TdsDeducted       { get; set; }   // actual deducted (editable override)

        // ── COMPUTED (cached) ────────────────────────────────────────────────
        public double GrossPayment      { get; set; }   // total earnings
        public double GrossTaxableSalary{ get; set; }   // after exemptions
        public double NetSalary         { get; set; }

        // ── HELPERS ─────────────────────────────────────────────────────────
        public string MonthName => System.Globalization.CultureInfo.InvariantCulture
            .DateTimeFormat.GetAbbreviatedMonthName(Month);

        /// <summary>Compute GrossPayment and GrossTaxableSalary from current fields.</summary>
        public void RecalcGross()
        {
            double da = DaPercent > 0 ? Math.Round((Basic + GradePay) * DaPercent / 100.0) : DaAmount;
            DaAmount = da;

            GrossPayment = Basic + GradePay + HRA + da
                         + SpecialAllowance + MedicalAllowance + Lta
                         + Bonus + Commission + AdvanceSalary + Arrears + OtherAllowances
                         + PerqTaxable + LeaveEncTaxable;

            GrossTaxableSalary = GrossPayment - PerqExempted - LeaveEncExempted;

            NetSalary = GrossPayment - PfEmployee - VPF - ProfessionalTax - EsiEmployee - TdsDeducted;
        }
    }

    /// <summary>
    /// Annual tax computation result for one regime.
    /// </summary>
    public class RegimeResult
    {
        public string   RegimeName          { get; set; } = "";
        public double   GrossSalary         { get; set; }   // total annual (actual + projected)
        public double   StandardDeduction   { get; set; }
        public double   HraExemption        { get; set; }
        public double   ProfTaxDeduction    { get; set; }
        public double   Chapter6A           { get; set; }
        public double   NpsEmployer80CCD2   { get; set; }
        public double   IncomeOtherSources  { get; set; }
        public double   TotalIncome         { get; set; }

        // Tax
        public double   TaxOnIncome         { get; set; }
        public double   Rebate87A           { get; set; }
        public double   TaxAfterRebate      { get; set; }
        public double   Surcharge           { get; set; }
        public double   Cess                { get; set; }
        public double   TotalTax            { get; set; }
    }

    /// <summary>
    /// Full annual computation for one employee at a given month.
    /// </summary>
    public class AnnualComputation
    {
        public RegimeResult  OldRegime        { get; set; } = new();
        public RegimeResult  NewRegime        { get; set; } = new();
        public string        ChosenRegime     { get; set; } = "New";
        public double        ChosenTax        => ChosenRegime == "New" ? NewRegime.TotalTax : OldRegime.TotalTax;

        // TDS position
        public double        YtdTdsDeducted   { get; set; }
        public int           MonthsRemaining  { get; set; }
        public double        BalanceTax       => ChosenTax - YtdTdsDeducted;
        public double        ThisMonthTds     => MonthsRemaining > 0
                                                 ? Math.Round(BalanceTax / MonthsRemaining)
                                                 : 0;

        // Context
        public int           ComputedForMonth { get; set; }
        public int           MonthsActual     { get; set; }   // months with real data
        public int           MonthsProjected  { get; set; }   // months estimated
    }
}
