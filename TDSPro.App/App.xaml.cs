using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using System.IO;
using System.Windows;
using TDSPro.BLL;
using TDSPro.DAL;

namespace TDSPro.App
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;
        private static string _logPath = "";

        public App()
        {
            // Catch any unhandled exception and write to a log before crashing
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var msg = e.ExceptionObject?.ToString() ?? "Unknown error";
                TryLog("UNHANDLED: " + msg);
                MessageBox.Show(msg, "TDS Pro — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            DispatcherUnhandledException += (s, e) =>
            {
                TryLog("DISPATCHER: " + e.Exception?.ToString());
                MessageBox.Show(e.Exception?.Message, "TDS Pro — Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                Services = services.BuildServiceProvider();
            }
            catch (Exception ex)
            {
                TryLog("DI BUILD FAILED: " + ex);
                MessageBox.Show("Failed to initialise services:\n\n" + ex.Message, "TDS Pro", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddWpfBlazorWebView();
            services.AddMudServices();

            services.AddSingleton<AppStateService>();

            // BLL
            services.AddTransient<DeductorService>();
            services.AddTransient<DeducteeService>();
            services.AddTransient<TdsEntryService>();
            services.AddTransient<ChallanService>();
            services.AddTransient<DashboardService>();
            services.AddTransient<PayrollService>();
            services.AddTransient<SalaryService>();
            services.AddTransient<TdsRulesService>();
            services.AddTransient<ReportsService>();
            services.AddTransient<ReturnService>();
            services.AddTransient<RulesUpdateService>();

            // DAL
            services.AddTransient<LicenseService>();
            services.AddTransient<DueDateService>();
            services.AddTransient<PanVerificationService>();

#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TDSPro");
                Directory.CreateDirectory(appDataPath);
                _logPath = Path.Combine(appDataPath, "blazor_startup.log");
                TryLog("=== TDSPro.App STARTUP ===");

                var state = Services.GetRequiredService<AppStateService>();
                state.AppDataPath = appDataPath;
                TryLog("AppDataPath: " + appDataPath);

                TryLog("Initialising DB...");
                Database.Initialize(appDataPath);
                TryLog("DB OK");

                try { new RulesUpdateService().AutoUpdateIfNeeded(); } catch (Exception ex) { TryLog("Rules: " + ex.Message); }

                state.CurrentFY = FolderManager.DetectFY(DateTime.Today);
                TryLog("FY: " + state.CurrentFY);

                try
                {
                    var licSvc = Services.GetRequiredService<LicenseService>();
                    state.CurrentLicense = licSvc.LoadSaved();
                }
                catch { state.CurrentLicense = LicenseService.BuildTrial(); }

                // Restore last-used deductor so CompanyFolder is set before any page loads
                try
                {
                    var lastId = int.Parse(Database.GetSetting("LAST_DEDUCTOR_ID", "0"));
                    if (lastId > 0)
                    {
                        var ded = new TDSPro.BLL.DeductorService().GetById(lastId);
                        if (ded != null) state.SetDeductor(ded.Id, ded.CompanyName, ded.Tan);
                    }
                }
                catch { }

                try { FolderManager.EnsureStructure(state.CurrentFY); } catch { }
                Task.Run(() => { try { Database.VacuumAndCheck(appDataPath); } catch { } });

                TryLog("Showing MainWindow...");
                var mainWindow = new MainWindow();
                mainWindow.Show();
                TryLog("MainWindow shown OK");
            }
            catch (Exception ex)
            {
                TryLog("STARTUP FAILED: " + ex);
                MessageBox.Show("Startup failed:\n\n" + ex.Message + "\n\n" + ex.InnerException?.Message,
                    "TDS Pro", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TDSPro");
                var dbPath     = Path.Combine(appDataPath, TDSPro.Common.AppConstants.DbFileName);
                var backupDir  = Path.Combine(appDataPath, "Backup");

                if (File.Exists(dbPath))
                {
                    Directory.CreateDirectory(backupDir);
                    var dest = Path.Combine(backupDir, $"TDSPro_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                    File.Copy(dbPath, dest, overwrite: false);

                    // Keep only the 10 most recent backups
                    var old = Directory.GetFiles(backupDir, "TDSPro_backup_*.db")
                                       .OrderByDescending(f => f)
                                       .Skip(10);
                    foreach (var f in old)
                        try { File.Delete(f); } catch { }
                }
            }
            catch { }

            base.OnExit(e);
        }

        private static void TryLog(string msg)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                {
                    _logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TDSPro", "blazor_startup.log");
                }
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }
    }
}
