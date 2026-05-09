using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// Form 27Q — TDS return for payments to Non-Residents (NRIs / foreign companies).
    ///
    /// Applicable sections:
    ///   194    — Dividends (NRI)
    ///   194LC  — Interest from Indian company (NRI bond)
    ///   194LD  — Interest on certain bonds (NRI)
    ///   195    — Other NRI payments: interest, royalties, technical fees
    ///   196A-D — Income in respect of units, FII securities, GDRs etc.
    ///
    /// 27Q uses the same NSDL FVU pipe-delimited format as 26Q,
    /// with Header.FormType = "27Q". The switch in FvuGenerator is extended
    /// to recognise "27Q" and treat it identically to "26Q".
    /// </summary>
    public static class Form27QGenerator
    {
        public static readonly string[] NriSections =
        {
            "194",    "194LC",  "194LD",
            "195",
            "196A",   "196B",   "196C",   "196D",
        };

        /// <summary>
        /// Validate that all deductee entries are for NRI sections,
        /// then generate the 27Q FVU text.
        /// Returns (ok, fvuText, errorMessage).
        /// </summary>
        public static (bool Ok, string FvuText, string Error) Generate(ReturnData data)
        {
            // Validate: all deductees must be non-resident
            var invalid = data.Deductees
                .Where(d => !NriSections.Any(s =>
                    d.Section.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (invalid.Any())
                return (false, "",
                    $"{invalid.Count} deductee(s) have non-NRI sections " +
                    $"(e.g. \"{invalid[0].Section}\"). " +
                    "Form 27Q is only for payments to Non-Residents. " +
                    "Use Form 26Q for resident deductees.");

            // Set form type to 27Q and generate
            data.Header.FormType = "27Q";

            try
            {
                var fvuText = FvuGenerator.Generate(data);
                Database.LogAction("system", "27Q_GENERATE", "Return",
                    $"27Q {data.Header.Quarter} {data.Header.FinancialYear}" +
                    $" — {data.Header.TanOfDeductor}");
                return (true, fvuText, "");
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        /// <summary>True if a section code is valid for 27Q (NRI payment).</summary>
        public static bool IsNriSection(string section) =>
            NriSections.Any(s => section.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
