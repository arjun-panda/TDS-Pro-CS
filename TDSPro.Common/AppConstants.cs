namespace TDSPro.Common
{
    /// <summary>
    /// Application-wide constants.
    /// NOTE: TDS rates and thresholds are NOT stored here.
    /// They live in the tds_rules database table (dynamic engine).
    /// </summary>
    public static class AppConstants
    {
        public const string AppName    = "TDS Pro";
        public const string AppVersion = "2.0.0";
        public const string DbFileName = "tds_pro.db";

        // ── Known section codes (for UI dropdowns only) ───────────────────────
        // Descriptions pulled from DB at runtime; this list is just for validation
        public static readonly string[] KnownSections = new[]
        {
            "192","192A","193","194","194A","194B","194BA","194BB",
            "194C","194D","194DA","194E","194F","194G","194H",
            "194I","194IA","194IB","194IC","194J","194K","194LA",
            "194LB","194LC","194LD","194M","194N","194O","194P",
            "194Q","194R","194S","195","196A","196B","196C","196D",
            "206AB","206CCA"
        };

        // ── Higher TDS rate when PAN not available ────────────────────────────
        public const double HigherTdsRateNoPan = 20.0;   // Section 206AA

        // ── Higher TDS rate when ITR not filed ───────────────────────────────
        public const double HigherTdsRateNoItr = 20.0;   // Section 206AB

        // ── Deductee types ────────────────────────────────────────────────────
        public static readonly string[] DeducteeTypes = new[]
        {
            "Individual", "HUF", "Firm", "Company",
            "AOP", "BOI", "Trust", "Government",
            "NRI - Individual", "NRI - Company", "Other"
        };

        // ── Financial years — generated dynamically so the list never expires ─
        // Shows 3 past FYs + current + 1 future FY
        // e.g. today = Apr 2026 → current FY = 2026-27
        //      list  = 2023-24, 2024-25, 2025-26, 2026-27, 2027-28
        public static string[] FinancialYears
        {
            get
            {
                // Determine current FY: after 31-March means new FY has started
                int today     = DateTime.Today.Month >= 4
                                ? DateTime.Today.Year
                                : DateTime.Today.Year - 1;
                var list = new List<string>();
                for (int y = today - 3; y <= today + 1; y++)
                    list.Add($"{y}-{(y + 1) % 100:D2}");
                return list.ToArray();
            }
        }

        // ── Quarters ──────────────────────────────────────────────────────────
        public static readonly string[] Quarters     = new[] { "Q1 (Apr-Jun)","Q2 (Jul-Sep)","Q3 (Oct-Dec)","Q4 (Jan-Mar)" };
        public static readonly string[] QuarterCodes = new[] { "Q1","Q2","Q3","Q4" };

        // ── Indian states ─────────────────────────────────────────────────────
        public static readonly string[] IndianStates = new[]
        {
            "Andhra Pradesh","Arunachal Pradesh","Assam","Bihar","Chhattisgarh",
            "Goa","Gujarat","Haryana","Himachal Pradesh","Jharkhand","Karnataka",
            "Kerala","Madhya Pradesh","Maharashtra","Manipur","Meghalaya","Mizoram",
            "Nagaland","Odisha","Punjab","Rajasthan","Sikkim","Tamil Nadu","Telangana",
            "Tripura","Uttar Pradesh","Uttarakhand","West Bengal","Delhi",
            "Jammu & Kashmir","Ladakh","Puducherry","Chandigarh","Other"
        };

        // ── Return form types ─────────────────────────────────────────────────
        public static readonly string[] ReturnFormTypes = new[] { "24Q", "26Q", "27Q", "27EQ" };

        // ── Nature of payment by section (for auto-fill in TDS entry) ─────────
        public static readonly Dictionary<string, string> SectionNature = new()
        {
            ["192"]   = "Salaries",
            ["192A"]  = "Premature withdrawal from EPF",
            ["193"]   = "Interest on securities",
            ["194"]   = "Dividend",
            ["194A"]  = "Interest other than interest on securities",
            ["194B"]  = "Winnings from lottery / crossword puzzle",
            ["194BA"] = "Winnings from online games",
            ["194BB"] = "Winnings from horse race",
            ["194C"]  = "Payment to contractors / sub-contractors",
            ["194D"]  = "Insurance commission",
            ["194DA"] = "Payment in respect of life insurance policy",
            ["194E"]  = "Payment to non-resident sportsman / entertainer",
            ["194F"]  = "Payment on account of repurchase of units (UTI/MF)",
            ["194G"]  = "Commission on sale of lottery tickets",
            ["194H"]  = "Commission or brokerage",
            ["194I"]  = "Rent",
            ["194IA"] = "Payment on transfer of immovable property (other than agricultural land)",
            ["194IB"] = "Rent paid by individual / HUF (not liable to audit)",
            ["194IC"] = "Payment under Joint Development Agreement",
            ["194J"]  = "Fees for professional or technical services",
            ["194K"]  = "Income in respect of units of mutual fund",
            ["194LA"] = "Compensation on acquisition of immovable property",
            ["194LB"] = "Interest from infrastructure debt fund",
            ["194LC"] = "Income by way of interest from Indian company",
            ["194LD"] = "Interest on rupee denominated bonds / government securities",
            ["194M"]  = "Payment to contractor / professional by certain individuals / HUF",
            ["194N"]  = "Cash withdrawal from bank / post office",
            ["194O"]  = "Payment by e-commerce operator to participant",
            ["194P"]  = "Tax deduction in case of specified senior citizens",
            ["194Q"]  = "Payment for purchase of goods",
            ["194R"]  = "Benefit or perquisite to a business / profession",
            ["194S"]  = "Payment for transfer of virtual digital asset",
            ["195"]   = "Payment to non-residents (other than salary)",
            ["196A"]  = "Income in respect of units of non-resident",
            ["196B"]  = "Income from units (including long-term capital gain)",
            ["196C"]  = "Income from foreign currency bonds or shares of Indian company",
            ["196D"]  = "Income of foreign institutional investors from securities",
            ["206AB"] = "Payment to specified person (non-filer of ITR)",
            ["206CCA"]= "Collected from specified person (non-filer of ITR)",
        };

        // ── User roles ────────────────────────────────────────────────────────
        public static readonly string[] UserRoles = new[]
            { "Super Admin", "Admin", "Operator", "View Only" };

        // ── TRACES / FVU config key names ─────────────────────────────────────
        public const string FvuConfigKey26Q = "FVU_FORMAT_26Q";
        public const string FvuConfigKey24Q = "FVU_FORMAT_24Q";
    }
}
