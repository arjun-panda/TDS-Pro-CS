using TDSPro.DAL;

namespace TDSPro.App
{
    /// <summary>
    /// Singleton that replaces Program.cs static state. Injected everywhere via DI.
    /// Raises OnChange so Blazor components can re-render when FY or Deductor switches.
    /// </summary>
    public class AppStateService
    {
        /// <summary>True when --login flag is passed on the command line (dev convenience).</summary>
        public bool AutoLogin { get; } =
            Environment.GetCommandLineArgs().Contains("--login", StringComparer.OrdinalIgnoreCase);

        public string AppDataPath     { get; set; } = "";
        public string CurrentUser     { get; private set; } = "";
        public string CurrentRole     { get; private set; } = "";
        public string CurrentFY       { get; set; } = "";
        public int    CurrentDeductorId   { get; private set; } = 0;
        public string CurrentDeductorName { get; private set; } = "";
        public string CurrentDeductorTan  { get; private set; } = "";
        public LicenseInfo CurrentLicense { get; set; } = new();
        public bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUser);

        public string CurrentAY =>
            TDSPro.Common.TaxRules.AssessmentYearLabel(CurrentFY);

        public event Action? OnChange;

        public void Login(string user, string role)
        {
            CurrentUser = user;
            CurrentRole = role;
            NotifyChanged();
        }

        public void Logout()
        {
            CurrentUser = "";
            CurrentRole = "";
            CurrentDeductorId = 0;
            CurrentDeductorName = "";
            CurrentDeductorTan = "";
            NotifyChanged();
        }

        public void SetFY(string fy)
        {
            CurrentFY = fy;
            NotifyChanged();
        }

        public void SetDeductor(int id, string name, string tan)
        {
            CurrentDeductorId   = id;
            CurrentDeductorName = name;
            CurrentDeductorTan  = tan;
            FolderManager.SetCompany(tan, name);
            try { Database.SetSetting("LAST_DEDUCTOR_ID", id.ToString()); } catch { }
            NotifyChanged();
        }

        public void NotifyStateChanged() => OnChange?.Invoke();

        private void NotifyChanged() => OnChange?.Invoke();
    }
}
