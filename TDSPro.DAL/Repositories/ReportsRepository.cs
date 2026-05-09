using Microsoft.Data.Sqlite;
using TDSPro.DAL.Models;

namespace TDSPro.DAL.Repositories
{
    public class ReportsRepository
    {
        // ── Quarter Summary ───────────────────────────────────────────────────
        public List<QuarterSummary> GetQuarterSummary(string fy)
        {
            var list = new List<QuarterSummary>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT quarter,
                       COUNT(*)                              AS entries,
                       COALESCE(SUM(amount),0)               AS gross,
                       COALESCE(SUM(tds_amount),0)           AS tds,
                       COALESCE(SUM(surcharge),0)            AS sc,
                       COALESCE(SUM(cess),0)                 AS cess,
                       COALESCE(SUM(interest),0)             AS interest,
                       COALESCE(SUM(total_tds),0)            AS total,
                       SUM(CASE WHEN status='Paid'    THEN 1 ELSE 0 END) AS paid,
                       SUM(CASE WHEN status='Pending' THEN 1 ELSE 0 END) AS pending
                FROM tds_entries
                WHERE financial_year = @fy
                GROUP BY quarter
                ORDER BY quarter";
            cmd.Parameters.AddWithValue("@fy", fy);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new QuarterSummary
                {
                    Quarter      = r.GetString(r.GetOrdinal("quarter")),
                    Entries      = Convert.ToInt32(r["entries"]),
                    GrossAmount  = Convert.ToDouble(r["gross"]),
                    TdsAmount    = Convert.ToDouble(r["tds"]),
                    Surcharge    = Convert.ToDouble(r["sc"]),
                    Cess         = Convert.ToDouble(r["cess"]),
                    Interest     = Convert.ToDouble(r["interest"]),
                    TotalTds     = Convert.ToDouble(r["total"]),
                    PaidCount    = Convert.ToInt32(r["paid"]),
                    PendingCount = Convert.ToInt32(r["pending"]),
                });
            }
            return list;
        }

        // ── Deductee-wise ──────────────────────────────────────────────────────
        public List<DeducteeReport> GetDeducteeReport(string fy, string? quarter = null)
        {
            var list = new List<DeducteeReport>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            var qc = quarter != null ? " AND e.quarter=@qt" : "";
            cmd.CommandText = $@"
                SELECT d.name, d.pan, d.deductee_type,
                       GROUP_CONCAT(DISTINCT e.section) AS sections,
                       COUNT(e.id)                       AS entries,
                       COALESCE(SUM(e.amount),0)         AS gross,
                       COALESCE(SUM(e.tds_amount),0)     AS tds,
                       COALESCE(SUM(e.interest),0)       AS interest,
                       COALESCE(SUM(e.total_tds),0)      AS total,
                       SUM(CASE WHEN e.status='Paid'    THEN 1 ELSE 0 END) AS paid,
                       SUM(CASE WHEN e.status='Pending' THEN 1 ELSE 0 END) AS pending
                FROM tds_entries e
                JOIN deductees d ON e.deductee_id = d.id
                WHERE e.financial_year = @fy {qc}
                GROUP BY e.deductee_id
                ORDER BY total DESC";
            cmd.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) cmd.Parameters.AddWithValue("@qt", quarter);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new DeducteeReport
                {
                    Name         = r.GetString(r.GetOrdinal("name")),
                    Pan          = r.GetString(r.GetOrdinal("pan")),
                    DeducteeType = r.IsDBNull(r.GetOrdinal("deductee_type")) ? "" : r.GetString(r.GetOrdinal("deductee_type")),
                    Section      = r.IsDBNull(r.GetOrdinal("sections")) ? "" : r.GetString(r.GetOrdinal("sections")),
                    Entries      = Convert.ToInt32(r["entries"]),
                    GrossAmount  = Convert.ToDouble(r["gross"]),
                    TdsAmount    = Convert.ToDouble(r["tds"]),
                    Interest     = Convert.ToDouble(r["interest"]),
                    TotalTds     = Convert.ToDouble(r["total"]),
                    PaidCount    = Convert.ToInt32(r["paid"]),
                    PendingCount = Convert.ToInt32(r["pending"]),
                });
            }
            return list;
        }

        // ── Section-wise ───────────────────────────────────────────────────────
        public List<SectionReport> GetSectionReport(string fy, string? quarter = null)
        {
            var list = new List<SectionReport>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            var qc = quarter != null ? " AND e.quarter=@qt" : "";
            cmd.CommandText = $@"
                SELECT e.section,
                       CASE e.section
                           WHEN '192'   THEN 'Salary payments'
                           WHEN '193'   THEN 'Interest on securities'
                           WHEN '194'   THEN 'Dividends'
                           WHEN '194A'  THEN 'Interest other than securities'
                           WHEN '194B'  THEN 'Winnings from lottery'
                           WHEN '194C'  THEN 'Payment to contractors'
                           WHEN '194D'  THEN 'Insurance commission'
                           WHEN '194H'  THEN 'Commission or brokerage'
                           WHEN '194I'  THEN 'Rent'
                           WHEN '194J'  THEN 'Professional / technical fees'
                           WHEN '194K'  THEN 'Income from mutual funds'
                           WHEN '194LA' THEN 'Compensation on acquisition'
                           WHEN '194Q'  THEN 'Purchase of goods'
                           WHEN '195'   THEN 'NRI payments'
                           WHEN '206AA' THEN 'Higher rate - no PAN'
                           WHEN '206AB' THEN 'Higher rate - non-filer'
                           ELSE 'Other'
                       END AS description,
                       COUNT(*)                     AS entries,
                       COALESCE(SUM(e.amount),0)    AS gross,
                       COALESCE(SUM(e.tds_amount),0)AS tds,
                       COALESCE(SUM(e.surcharge),0) AS sc,
                       COALESCE(SUM(e.cess),0)      AS cess,
                       COALESCE(SUM(e.interest),0)  AS interest,
                       COALESCE(SUM(e.total_tds),0) AS total
                FROM tds_entries e
                WHERE e.financial_year = @fy {qc}
                GROUP BY e.section
                ORDER BY total DESC";
            cmd.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) cmd.Parameters.AddWithValue("@qt", quarter);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SectionReport
                {
                    Section     = r.GetString(r.GetOrdinal("section")),
                    Description = r.GetString(r.GetOrdinal("description")),
                    Entries     = Convert.ToInt32(r["entries"]),
                    GrossAmount = Convert.ToDouble(r["gross"]),
                    TdsAmount   = Convert.ToDouble(r["tds"]),
                    Surcharge   = Convert.ToDouble(r["sc"]),
                    Cess        = Convert.ToDouble(r["cess"]),
                    Interest    = Convert.ToDouble(r["interest"]),
                    TotalTds    = Convert.ToDouble(r["total"]),
                });
            }
            return list;
        }

        // ── Challan Reconciliation ─────────────────────────────────────────────
        public ChallanReconciliation GetChallanReconciliation(string fy, string? quarter = null)
        {
            using var conn = Database.GetConnection();
            var qc = quarter != null ? " AND quarter=@qt" : "";

            using var c1 = conn.CreateCommand();
            c1.CommandText = $"SELECT COALESCE(SUM(total_tds),0) FROM tds_entries WHERE financial_year=@fy{qc}";
            c1.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) c1.Parameters.AddWithValue("@qt", quarter);
            var payable = Convert.ToDouble(c1.ExecuteScalar() ?? 0.0);

            using var c2 = conn.CreateCommand();
            c2.CommandText = $"SELECT COALESCE(SUM(tds_amount),0) FROM challans WHERE financial_year=@fy{qc}";
            c2.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) c2.Parameters.AddWithValue("@qt", quarter);
            var deposited = Convert.ToDouble(c2.ExecuteScalar() ?? 0.0);

            var challanList = new ChallanRepository().GetAll(fy: fy);
            if (quarter != null)
                challanList = challanList.Where(c => c.Quarter == quarter).ToList();

            return new ChallanReconciliation
            {
                TdsPayable       = payable,
                ChallanDeposited = deposited,
                Challans         = challanList,
            };
        }

        // ── Return data builder ────────────────────────────────────────────────
        public ReturnData BuildReturnData(int deductorId, string fy, string quarter, string formType)
        {
            using var conn = Database.GetConnection();

            // Deductor
            var dr = new DeductorRepository().GetById(deductorId);
            if (dr == null) throw new Exception("Deductor not found.");

            var header = new ReturnHeader
            {
                FormType        = formType,
                FinancialYear   = fy,
                Quarter         = quarter,
                TanOfDeductor   = dr.Tan,
                PanOfDeductor   = dr.Pan,
                DeductorName    = dr.CompanyName,
                DeductorAddress = dr.Address,
                DeductorCity    = dr.City,
                DeductorState   = dr.State,
                DeductorPin     = dr.Pincode,
                ContactPerson   = dr.ContactPerson,
                Phone           = dr.Phone,
                Email           = dr.Email,
                FilingDate      = DateTime.Today,
                ResponsiblePan  = dr.Pan,
                ResponsibleName = dr.ContactPerson,
                Designation     = "Director",
            };

            // Challans for quarter
            using var cc = conn.CreateCommand();
            cc.CommandText = @"SELECT * FROM challans
                               WHERE deductor_id=@did
                               AND financial_year=@fy
                               AND quarter=@qt
                               ORDER BY challan_date";
            cc.Parameters.AddWithValue("@did", deductorId);
            cc.Parameters.AddWithValue("@fy",  fy);
            cc.Parameters.AddWithValue("@qt",  quarter);
            var challans = new List<ReturnChallanDetail>();
            int slNo = 1;
            using (var r = cc.ExecuteReader())
            {
                while (r.Read())
                {
                    challans.Add(new ReturnChallanDetail
                    {
                        SlNo          = slNo++,
                        BsrCode       = r.GetString(r.GetOrdinal("bsr_code")),
                        ChallanDate   = DateTime.Parse(r.GetString(r.GetOrdinal("challan_date"))),
                        ChallanNo     = r.GetString(r.GetOrdinal("challan_no")),
                        TdsDeposited  = Convert.ToDouble(r["tds_amount"]),
                        Surcharge     = Convert.ToDouble(r["surcharge"]),
                        Cess          = Convert.ToDouble(r["cess"]),
                        Interest      = Convert.ToDouble(r["interest"]),
                        LateFee       = Convert.ToDouble(r["late_fee"]),
                        TotalDeposited= Convert.ToDouble(r["total_amount"]),
                        Section       = r.IsDBNull(r.GetOrdinal("section")) ? "" : r.GetString(r.GetOrdinal("section")),
                        Quarter       = quarter,
                    });
                }
            }

            // TDS entries — filter by form type:
            // 24Q = salary only  (192, 192A, 392, 392(1), 392(2))
            // 26Q = non-salary   (everything else)
            bool is24Q = formType == "24Q";
            string sectionFilter = is24Q
                ? "AND (e.section IN ('192','192A','392','392(1)','392(2)') OR e.section LIKE '192%' OR e.section LIKE '392%')"
                : "AND (e.section NOT IN ('192','192A','392','392(1)','392(2)') AND e.section NOT LIKE '192%' AND e.section NOT LIKE '392%')";
            using var ec = conn.CreateCommand();
            ec.CommandText = $@"SELECT e.*, d.name AS dname, d.pan AS dpan,
                                      d.deductee_type AS dtype,
                                      d.is_resident AS dis_resident
                               FROM tds_entries e
                               JOIN deductees d ON e.deductee_id = d.id
                               WHERE e.deductor_id=@did
                               AND e.financial_year=@fy
                               AND e.quarter=@qt
                               {sectionFilter}
                               ORDER BY e.entry_date";
            ec.Parameters.AddWithValue("@did", deductorId);
            ec.Parameters.AddWithValue("@fy",  fy);
            ec.Parameters.AddWithValue("@qt",  quarter);
            var deductees = new List<ReturnDeducteeDetail>();
            slNo = 1;
            using (var r = ec.ExecuteReader())
            {
                while (r.Read())
                {
                    var dtype = r.IsDBNull(r.GetOrdinal("dtype")) ? "Individual" : r.GetString(r.GetOrdinal("dtype"));
                    deductees.Add(new ReturnDeducteeDetail
                    {
                        SlNo            = slNo++,
                        Pan             = r.GetString(r.GetOrdinal("dpan")),
                        Name            = r.GetString(r.GetOrdinal("dname")),
                        Section         = r.GetString(r.GetOrdinal("section")),
                        PaymentDate     = r.IsDBNull(r.GetOrdinal("payment_date")) || r.GetString(r.GetOrdinal("payment_date")) == ""
                                          ? DateTime.Parse(r.GetString(r.GetOrdinal("entry_date")))
                                          : DateTime.Parse(r.GetString(r.GetOrdinal("payment_date"))),
                        AmountPaid      = Convert.ToDouble(r["amount"]),
                        TdsDeducted     = Convert.ToDouble(r["tds_amount"]),
                        TdsDeposited    = Convert.ToDouble(r["total_tds"]),
                        Surcharge       = Convert.ToDouble(r["surcharge"]),
                        Cess            = Convert.ToDouble(r["cess"]),
                        ChallanNo       = r.IsDBNull(r.GetOrdinal("challan_no")) ? "" : r.GetString(r.GetOrdinal("challan_no")),
                        Rate            = Convert.ToDouble(r["rate"]),
                        DeducteeType    = dtype.Equals("Company", StringComparison.OrdinalIgnoreCase) ? "02" : "01",
                        IsResidentIndian= !r.IsDBNull(r.GetOrdinal("dis_resident")) && r.GetInt32(r.GetOrdinal("dis_resident")) == 1,
                        Remarks         = r.IsDBNull(r.GetOrdinal("remarks")) ? "" : r.GetString(r.GetOrdinal("remarks")),
                    });
                }
            }

            // Link challan BSR to deductee entries
            // Rule: match by ChallanNo first; fallback to single challan only if there is exactly one
            for (int i = 0; i < deductees.Count; i++)
            {
                if (string.IsNullOrEmpty(deductees[i].BsrCode) && challans.Count > 0)
                {
                    var match = challans.FirstOrDefault(c =>
                        !string.IsNullOrEmpty(deductees[i].ChallanNo) &&
                        c.ChallanNo == deductees[i].ChallanNo);
                    if (match != null)
                        deductees[i].BsrCode = match.BsrCode;
                    else if (challans.Count == 1)
                        deductees[i].BsrCode = challans[0].BsrCode;  // safe only when single challan
                    // else: leave blank — FVU will flag as E020 so CA can correct it
                }
            }

            // Set NoOfDeductees per challan (linked + unlinked assigned to first challan)
            bool unlinkedCounted = false;
            foreach (var ch in challans)
            {
                ch.NoOfDeductees = deductees.Count(d => d.ChallanNo == ch.ChallanNo);
                if (!unlinkedCounted)
                {
                    ch.NoOfDeductees += deductees.Count(d => string.IsNullOrEmpty(d.ChallanNo));
                    unlinkedCounted = true;
                }
            }

            return new ReturnData { Header = header, Challans = challans, Deductees = deductees };
        }
    }
}
