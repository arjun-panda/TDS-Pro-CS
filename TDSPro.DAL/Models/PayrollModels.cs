namespace TDSPro.DAL.Models
{
    public class Employee
    {
        public int     Id              { get; set; }
        public int     DeductorId      { get; set; }
        public bool    IsActive        { get; set; } = true;

        // ── Identity ──────────────────────────────────────────────────────────
        public string  EmployeeCode    { get; set; } = "";
        public string  Name            { get; set; } = "";
        public string  FathersName     { get; set; } = "";
        public string  Sex             { get; set; } = "Male";  // Male / Female / Other
        public string  DateOfBirth     { get; set; } = "";      // dd-MMM-yyyy
        public string  Pan             { get; set; } = "";
        public string  PfNumber        { get; set; } = "";      // PF account number
        public string  WardCircleRange { get; set; } = "";      // TDS ward for filing

        // ── Contact ───────────────────────────────────────────────────────────
        public string  Email           { get; set; } = "";
        public string  Phone           { get; set; } = "";
        public string  StdCode         { get; set; } = "";
        public string  TelephoneNo     { get; set; } = "";

        // ── Residential Address ───────────────────────────────────────────────
        public string  FlatDoorBlockNo         { get; set; } = "";
        public string  PremisesBuildingVillage { get; set; } = "";
        public string  RoadStreetPostOffice    { get; set; } = "";
        public string  AreaLocality            { get; set; } = "";
        public string  TownCityDistrict        { get; set; } = "";
        public string  PinCode                 { get; set; } = "";
        public string  State                   { get; set; } = "";

        // ── Employment ────────────────────────────────────────────────────────
        public string  Designation     { get; set; } = "";
        public string  Department      { get; set; } = "";
        public string  JoinDate        { get; set; } = "";
        public string  LeavingDate     { get; set; } = "";      // empty = still employed

        // ── Bank ──────────────────────────────────────────────────────────────
        public string  BankAccount     { get; set; } = "";
        public string  BankIfsc        { get; set; } = "";

        // ── Extended Identity ─────────────────────────────────────────────────
        public string  AadhaarNumber      { get; set; } = "";
        public string  ResidentialStatus  { get; set; } = "Resident"; // Resident / NRI / Foreign
        public string  MaritalStatus      { get; set; } = "Single";
        public string  BloodGroup         { get; set; } = "";
        public string  EmploymentType     { get; set; } = "Permanent";

        // ── Extended Contact ──────────────────────────────────────────────────
        public string  WorkEmail          { get; set; } = "";
        public string  EmergencyContact   { get; set; } = "";
        public string  EmergencyMobile    { get; set; } = "";

        // ── Extended Statutory ────────────────────────────────────────────────
        public string  Uan                { get; set; } = "";
        public string  EsiIpNumber        { get; set; } = "";

        // ── Extended Bank ─────────────────────────────────────────────────────
        public string  BankName           { get; set; } = "";
        public string  BankBranch         { get; set; } = "";
        public string  BankAccountType    { get; set; } = "Savings";

        // ── Previous Employer (Form 12B) ──────────────────────────────────────
        public string  PrevEmployerName   { get; set; } = "";
        public double  PrevEmployerIncome { get; set; } = 0;
        public double  PrevEmployerTds    { get; set; } = 0;

        // ── Salary / Tax settings ─────────────────────────────────────────────
        public string  TaxRegime       { get; set; } = "New";  // New / Old
        public bool    HraMonthlyBasis { get; set; } = true;   // Calculate HRA on monthly basis
        public bool    DaForRetirement { get; set; } = true;   // DA forms part of retirement salary
        public bool    IsDifferentlyAbled { get; set; } = false;

        // ── Salary structure (loaded with employee) ───────────────────────────
        public SalaryStructure? Salary { get; set; }

        public override string ToString() =>
            string.IsNullOrEmpty(EmployeeCode) ? Name : $"{EmployeeCode} — {Name}";
    }

    public class SalaryStructure
    {
        public int    Id              { get; set; }
        public int    EmployeeId     { get; set; }
        public double Basic          { get; set; }
        public double Hra            { get; set; }
        public double Da             { get; set; }
        public double SpecialAllowance   { get; set; }
        public double MedicalAllowance   { get; set; }
        public double Lta                { get; set; }
        public double OtherAllowance     { get; set; }
        public bool   PfApplicable       { get; set; } = true;
        /// <summary>
        /// 0 = auto (12% of Basic).  Any positive value = fixed monthly PF deduction chosen by employee.
        /// Common use: cap at ₹1,800 (12% of ₹15,000 statutory ceiling) when Basic > ₹15,000.
        /// </summary>
        public double PfFixedAmount  { get; set; } = 0;
        public bool   EsiApplicable  { get; set; } = false;
        public string PtState        { get; set; } = ""; // e.g. "Maharashtra"
        public string EffectiveFrom  { get; set; } = "";

        public double GrossSalary =>
            Basic + Hra + Da + SpecialAllowance + MedicalAllowance + Lta + OtherAllowance;
    }

    public class TaxDeclaration
    {
        public int    Id              { get; set; }
        public int    EmployeeId     { get; set; }
        public string FinancialYear  { get; set; } = "";
        // HRA
        public double RentPaid       { get; set; }
        public string HraCityType    { get; set; } = "Non-Metro"; // Metro / Non-Metro
        // Chapter VI-A (Old regime)
        public double Sec80C         { get; set; }
        public double Sec80D_Self    { get; set; }
        public double Sec80D_Parents { get; set; }
        public double Sec80G         { get; set; }
        public double Sec80CCD_Employee { get; set; } // NPS employee
        public double Sec80CCD_Employer { get; set; } // NPS employer (both regimes)
        public double OtherDeductions      { get; set; }
        public double IncomeOtherSources    { get; set; }   // interest, rent, other income
        // Additional Chapter VI-A
        public double Sec80E                { get; set; }   // education loan interest
        public double Sec80EEA              { get; set; }   // housing loan interest (first home)
        public double Sec80TTA              { get; set; }   // savings interest (non-senior, max ₹10K)
        public double Sec80TTB              { get; set; }   // savings interest (senior, max ₹50K)
        public double Sec80DD               { get; set; }   // differently abled dependent
        public double Sec80U                { get; set; }   // self differently abled
        public double LtaExemption          { get; set; }   // LTA claimed
        // HRA details
        public string LandlordPan           { get; set; } = ""; // mandatory if rent > ₹1L/yr
        // 80D details
        public bool   IsParentSeniorCitizen { get; set; }   // 80D parent limit: ₹50K if true, ₹25K
    }

    public class PayrollRun
    {
        public int    Id              { get; set; }
        public int    EmployeeId     { get; set; }
        public int    DeductorId     { get; set; }
        public int    Month          { get; set; }
        public int    Year           { get; set; }
        public string FinancialYear  { get; set; } = "";

        // Earnings
        public double Basic          { get; set; }
        public double Hra            { get; set; }
        public double Da             { get; set; }
        public double Special        { get; set; }
        public double Medical        { get; set; }
        public double Lta            { get; set; }
        public double Other          { get; set; }
        public double GrossSalary    { get; set; }

        // Deductions
        public double PfEmployee     { get; set; }
        public double EsiEmployee    { get; set; }
        public double ProfessionalTax { get; set; }
        public double TdsDeducted    { get; set; }
        public double OtherDeductions { get; set; }
        public double TotalDeductions => PfEmployee + EsiEmployee + ProfessionalTax + TdsDeducted + OtherDeductions;

        // Tax computation
        public string TaxRegimeUsed  { get; set; } = "New";
        public double HraExemption   { get; set; }
        public double StandardDeduction { get; set; } = 75000;
        public double Chapter6ADeduction { get; set; }
        public double TaxableIncome  { get; set; }
        public double AnnualTax      { get; set; }
        public double Surcharge      { get; set; }
        public double Cess           { get; set; }
        public double TotalAnnualTax { get; set; }
        public double YtdTds         { get; set; }  // TDS already deducted in earlier months

        // Net pay
        public double NetPay         => GrossSalary - TotalDeductions;
        public string Status         { get; set; } = "Draft"; // Draft / Processed / Paid
        public int?   TdsEntryId     { get; set; }  // FK to tds_entries after 24Q push
        public int    ProRataDays    { get; set; } = 0;   // 0 = full month, >0 = partial
        public int    ProRataTotal   { get; set; } = 0;   // total days in that month

        // For display
        public string EmployeeName   { get; set; } = "";
        public string EmployeeCode   { get; set; } = "";
        public string Pan            { get; set; } = "";
        public string MonthLabel     => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    }

    public class SalaryComputeResult
    {
        public PayrollRun Run        { get; set; } = new();
        public double TaxOldRegime   { get; set; }
        public double TaxNewRegime   { get; set; }
        public double TaxableOld     { get; set; }
        public double TaxableNew     { get; set; }
        public string RecommendedRegime { get; set; } = "New";
    }

    /// <summary>Full-year view for one employee — keyed by month number (4=Apr … 3=Mar).</summary>
    public class EmployeeYearSummary
    {
        public int    EmployeeId   { get; set; }
        public string EmployeeName { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string Pan          { get; set; } = "";

        // Key = month number (4–12, 1–3).  Null = payroll not yet run for that month.
        public Dictionary<int, PayrollRun> MonthlyRuns { get; set; } = new();

        public double TotalGross => MonthlyRuns.Values.Sum(r => r.GrossSalary);
        public double TotalTds   => MonthlyRuns.Values.Sum(r => r.TdsDeducted);
        public double TotalPf    => MonthlyRuns.Values.Sum(r => r.PfEmployee);
        public double TotalPt    => MonthlyRuns.Values.Sum(r => r.ProfessionalTax);
        public double TotalNet   => MonthlyRuns.Values.Sum(r => r.NetPay);
        public int    MonthsRun  => MonthlyRuns.Count;
    }
}
