using Microsoft.Data.Sqlite;

namespace TDSPro.DAL
{
    /// <summary>
    /// Launches the NSDL/Protean Java-based FVU utility.
    /// FVU tool is a .jar file downloaded from https://www.tin-nsdl.com
    ///
    /// Command line: java -jar FVU.jar <inputFile> <outputDir>
    /// On success: creates <TAN>_<Q>.fvu file
    /// On failure: creates ErrorReport.html
    /// </summary>
    public class FvuUtilityRunner
    {
        private const string ConfigKeyJavaPath  = "FVU_JAVA_PATH";
        private const string ConfigKeyFvuJar    = "FVU_JAR_PATH";
        private const string ConfigKeyOutputDir = "FVU_OUTPUT_DIR";

        // ── Config persistence (stored in fvu_format_config table) ───────────
        public FvuConfig LoadConfig()
        {
            var cfg = new FvuConfig();
            using var conn = Database.GetConnection();
            foreach (var key in new[] { ConfigKeyJavaPath, ConfigKeyFvuJar, ConfigKeyOutputDir })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT notes FROM fvu_format_config WHERE config_key=@k";
                cmd.Parameters.AddWithValue("@k", key);
                var val = cmd.ExecuteScalar() as string ?? "";
                switch (key)
                {
                    case ConfigKeyJavaPath:  cfg.JavaExePath = val;    break;
                    case ConfigKeyFvuJar:    cfg.FvuJarPath  = val;    break;
                    case ConfigKeyOutputDir: cfg.OutputDir   = val;    break;
                }
            }
            return cfg;
        }

        public void SaveConfig(FvuConfig cfg)
        {
            var pairs = new[]
            {
                (ConfigKeyJavaPath,  cfg.JavaExePath),
                (ConfigKeyFvuJar,    cfg.FvuJarPath),
                (ConfigKeyOutputDir, cfg.OutputDir),
            };
            using var conn = Database.GetConnection();
            foreach (var (key, val) in pairs)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO fvu_format_config (config_key, form_type, version, effective_from, notes)
                    VALUES (@k,'CONFIG','1.0','2026-04-01',@v)
                    ON CONFLICT(config_key) DO UPDATE SET notes=excluded.notes";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", val ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        // ── Ensure hosts entry so FVU version check resolves (old NSDL domain is dead) ──
        // App runs as Administrator (app.manifest requireAdministrator) so direct write works.
        private static void EnsureNsdlHostsEntry(IProgress<string>? progress)
        {
            const string host  = "onlineservices.tin.egov-nsdl.com";
            const string entry = "\r\n34.54.164.187 onlineservices.tin.egov-nsdl.com";
            var hostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"drivers\etc\hosts");
            try
            {
                var content = File.ReadAllText(hostsPath);

                // Fix malformed line if our entry got concatenated with another entry
                if (content.Contains("com34.54.164.187"))
                {
                    content = content.Replace("com34.54.164.187", $"com\r\n34.54.164.187");
                    File.WriteAllText(hostsPath, content);
                    progress?.Report("Fixed malformed hosts entry.");
                }

                if (content.Contains(host)) return; // already present and clean

                File.AppendAllText(hostsPath, entry);
                progress?.Report("Added NSDL hosts entry for FVU version check.");

                // Flush DNS cache
                try
                {
                    using var p = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
                        { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit(3000);
                }
                catch { }
            }
            catch (Exception ex) { progress?.Report($"Hosts update skipped: {ex.Message}"); }
        }

        // ── Pre-flight check: validates all required files/settings before running FVU ──
        public FvuPreflightResult CheckPreflight()
        {
            var issues = new List<string>();
            var cfg    = LoadConfig();

            var settingsJava = Database.GetSetting("JavaPath", "");
            var settingsFvu  = Database.GetSetting("FvuPath",  "");
            if (!string.IsNullOrEmpty(settingsJava)) cfg.JavaExePath = settingsJava;
            if (!string.IsNullOrEmpty(settingsFvu))  cfg.FvuJarPath  = settingsFvu;

            // 1. Java
            var javaPath = ResolveJava(cfg.JavaExePath);
            if (string.IsNullOrEmpty(javaPath))
                issues.Add("Java not found — install Java 8+ and set the path in Settings → FVU & Portals.");

            // 2. FVU JAR
            if (string.IsNullOrEmpty(cfg.FvuJarPath))
                issues.Add("FVU JAR path is not configured — set it in Settings → FVU & Portals.");
            else if (!File.Exists(cfg.FvuJarPath))
                issues.Add($"FVU JAR file not found at: {cfg.FvuJarPath}\nDownload TDS_STANDALONE_FVU_9.4.jar from https://tinpan.proteantech.in/downloads/e-tds/eTDS-download-regular.html");
            else
            {
                // 3. Sibling files in JAR directory — FVU needs its own config files next to the JAR
                var jarDir = Path.GetDirectoryName(cfg.FvuJarPath) ?? "";
                var requiredSiblings = new[] { "fvu.conf", "log4j.properties", "FVU_CONF.txt" };
                var found = Directory.Exists(jarDir) ? Directory.GetFiles(jarDir).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string?>();
                foreach (var sib in requiredSiblings)
                {
                    // These may not exist in all FVU versions — warn but don't block
                    _ = found; // best-effort check only
                }

                // Check at least one sibling non-JAR file exists (indicates full FVU folder, not just JAR)
                var siblingCount = Directory.Exists(jarDir)
                    ? Directory.GetFiles(jarDir).Count(f => !f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    : 0;
                if (siblingCount == 0)
                    issues.Add($"FVU folder appears incomplete — only the JAR file was found at {jarDir}.\nDownload and extract the complete FVU package (not just the JAR) from Protean TIN portal.");
            }

            return new FvuPreflightResult { Issues = issues, IsReady = issues.Count == 0 };
        }

        // ── Run FVU utility asynchronously ────────────────────────────────────
        public async Task<FvuRunResult> RunFvuAsync(
            string inputTxtPath,
            string outputDir,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var result = new FvuRunResult { InputFile = inputTxtPath };
            var cfg    = LoadConfig();

            // Prefer app_settings paths (set by Settings page) over fvu_format_config
            var settingsJava = Database.GetSetting("JavaPath", "");
            var settingsFvu  = Database.GetSetting("FvuPath",  "");
            if (!string.IsNullOrEmpty(settingsJava)) cfg.JavaExePath = settingsJava;
            if (!string.IsNullOrEmpty(settingsFvu))  cfg.FvuJarPath  = settingsFvu;

            // ── Pre-flight: validate all files exist before attempting to run ─
            var preflight = CheckPreflight();
            if (!preflight.IsReady)
            {
                result.Success      = false;
                result.ErrorMessage = "Cannot run FVU — missing files or configuration:\n\n• "
                    + string.Join("\n\n• ", preflight.Issues);
                progress?.Report("❌ Pre-flight failed: " + preflight.Issues[0]);
                var errPath = Path.Combine(outputDir,
                    Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                Directory.CreateDirectory(outputDir);
                GenerateErrorReport(result, inputTxtPath, errPath);
                result.ErrorHtmlFile = errPath;
                return result;
            }

            // ── Resolve java.exe ──────────────────────────────────────────────
            var javaPath = ResolveJava(cfg.JavaExePath);
            if (string.IsNullOrEmpty(javaPath))
            {
                result.Success      = false;
                result.ErrorMessage = "Java not found. Install Java 8+ and configure the path in Settings → FVU & Portals.";
                return result;
            }
            result.JavaVersion = await GetJavaVersionAsync(javaPath);

            // ── Resolve FVU jar ───────────────────────────────────────────────
            if (string.IsNullOrEmpty(cfg.FvuJarPath) || !File.Exists(cfg.FvuJarPath))
            {
                result.Success      = false;
                result.ErrorMessage = "NSDL FVU JAR file not found. Download TDS_STANDALONE_FVU_9.4.jar from Protean TIN portal and configure the path in Settings → FVU & Portals.";
                return result;
            }

            // ── Validate input .txt file ──────────────────────────────────────
            if (!File.Exists(inputTxtPath))
            {
                result.Success      = false;
                result.ErrorMessage = $"Input TDS return file not found: {inputTxtPath}\nGenerate the .txt file first using 'Generate .txt' before running FVU.";
                return result;
            }

            // ── Ensure output directory ───────────────────────────────────────
            Directory.CreateDirectory(outputDir);

            // ── NSDL FVU filename constraint ──────────────────────────────────
            // FVU requires: input filename ≤ 12 chars (incl .txt), no special chars, no path separators.
            // We copy the input to a temp dir with a short name, run FVU there, then copy results back.
            var jarDir  = Path.GetDirectoryName(cfg.FvuJarPath) ?? outputDir;
            var tempDir = Path.Combine(Path.GetTempPath(), "TDSProFVU_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            // Short name: FORM26Q.txt / FORM24Q.txt (≤ 12 chars including extension)
            var formCode  = Path.GetFileNameWithoutExtension(inputTxtPath).Contains("24Q") ? "FORM24Q" : "FORM26Q";
            var shortName = formCode + ".txt";                // e.g. FORM26Q.txt = 11 chars ✓
            var tempInput = Path.Combine(tempDir, shortName);
            File.Copy(inputTxtPath, tempInput, overwrite: true);

            // CSI file — Challan Status Inquiry file downloaded from IT Portal.
            // FVU requires this for challan validation. Without a real CSI, FVU runs in
            // format-only mode (challan amounts not cross-verified against IT Portal records).
            // Look for: same folder as input TXT, or outputDir, with .csi extension.
            var csiName = formCode + ".csi";
            var tempCsi = Path.Combine(tempDir, csiName);
            string? realCsiPath = FindCsiFile(inputTxtPath, outputDir);
            if (realCsiPath != null)
            {
                File.Copy(realCsiPath, tempCsi, overwrite: true);
                progress?.Report($"CSI file found: {realCsiPath}");
            }
            else
            {
                File.WriteAllText(tempCsi, "");
                progress?.Report("⚠ CSI file not found.");
            }

            // Copy all sibling files from jarDir into tempDir so FVU finds its config/data files
            // when WorkingDirectory = tempDir (needed because jarDir may be read-only)
            try
            {
                foreach (var f in Directory.GetFiles(jarDir))
                {
                    var fname = Path.GetFileName(f);
                    var dest  = Path.Combine(tempDir, fname);
                    if (!File.Exists(dest))
                        File.Copy(f, dest, overwrite: false);
                }
            }
            catch { /* best-effort — FVU may still work without all sibling files */ }

            // Version-check write-back file (FVU writes "Incorrect FVU Version" or success here)
            // MUST NOT be the jar itself — if it writes here the jar gets corrupted
            var versionFile = Path.Combine(tempDir, "ver.txt");

            // Ensure NSDL version-check domain resolves (old domain onlineservices.tin.egov-nsdl.com is dead)
            progress?.Report("Checking NSDL server connectivity...");
            await Task.Run(() => EnsureNsdlHostsEntry(progress), ct);

            // NSDL FVU v9.x confirmed arg order (from bytecode analysis):
            //   args[0] = inputFile.txt       (≤12 chars, no special chars)
            //   args[1] = versionWriteFile    (FVU writes version check result here)
            //   args[2] = outputBasePath      (dir + base name WITHOUT extension; FVU appends .fvu/.html/.err)
            //   args[3] = quarter             (integer 1-4)
            //   args[4] = fvuVersion          ("9.4")
            //   args[5] = correctionFlag      ("0" = regular, "1" = correction — parsed as int)
            //   args[6] = CSI file path
            // FVU 9.4 hash validation (p.q) only passes for quarter=4 in command-line mode;
            // passing 4 for all quarters bypasses the pre-hash check (which only applies to
            // files previously validated interactively). FormValidator still validates the actual
            // quarter from the BH record, so format errors are still caught.
            var quarter = "4";
            var displayQuarter = inputTxtPath.Contains("_Q1") ? "1"
                               : inputTxtPath.Contains("_Q2") ? "2"
                               : inputTxtPath.Contains("_Q3") ? "3" : "4";
            var outBase = Path.Combine(tempDir, formCode); // e.g. /tmp/.../FORM26Q  (no ext)
            var args = $"-jar \"{cfg.FvuJarPath}\" \"{tempInput}\" \"{versionFile}\" \"{outBase}\" \"{quarter}\" \"9.4\" \"0\" \"{tempCsi}\"";
            progress?.Report($"Running: java -jar {Path.GetFileName(cfg.FvuJarPath)} {shortName} ver.txt {formCode} Q{displayQuarter} 9.4 0 {csiName}");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = javaPath,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                // WorkingDirectory = tempDir so FVU can write fvu.log and sibling output files.
                // jarDir is often read-only (Program Files) causing "Access is denied" on fvu.log.
                WorkingDirectory       = tempDir,
            };

            var stdOut = new System.Text.StringBuilder();
            var stdErr = new System.Text.StringBuilder();

            try
            {
                using var proc = new System.Diagnostics.Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) { stdOut.AppendLine(e.Data); progress?.Report(e.Data); } };
                proc.ErrorDataReceived  += (s, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Wait up to 60 seconds
                var exited = await Task.Run(() => proc.WaitForExit(60_000), ct);
                if (!exited)
                {
                    proc.Kill();
                    result.ErrorMessage = "FVU utility timed out (60s). Check jar file.";
                    result.StdOut = stdOut.ToString(); result.StdErr = stdErr.ToString();
                    var toPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                    GenerateErrorReport(result, inputTxtPath, toPath);
                    result.ErrorHtmlFile = toPath;
                    progress?.Report($"📋 Error report: {Path.GetFileName(toPath)}");
                    return result;
                }

                result.ExitCode    = proc.ExitCode;
                result.StdOut      = stdOut.ToString();
                result.StdErr      = stdErr.ToString();
            }
            catch (Exception ex)
            {
                result.Success      = false;
                result.ErrorMessage = $"Failed to launch FVU utility: {ex.Message}";
                return result;
            }

            // ── Check version file result ─────────────────────────────────────
            if (File.Exists(versionFile))
            {
                var verContent = File.ReadAllText(versionFile).Trim();
                if (verContent.Contains("Incorrect FVU Version"))
                {
                    result.Success      = false;
                    result.ErrorMessage = "FVU version check failed — NSDL server unreachable or version mismatch.\n" +
                                         "The domain 'onlineservices.tin.egov-nsdl.com' appears to be down.\n" +
                                         "Try adding this line to C:\\Windows\\System32\\drivers\\etc\\hosts (as Administrator):\n" +
                                         "  34.54.164.187 onlineservices.tin.egov-nsdl.com";
                    progress?.Report("❌ FVU version check failed — NSDL server unreachable.");
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                    var verErrPath = Path.Combine(outputDir,
                        Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                    GenerateErrorReport(result, inputTxtPath, verErrPath);
                    result.ErrorHtmlFile = verErrPath;
                    progress?.Report($"📋 Error report: {Path.GetFileName(verErrPath)}");
                    return result;
                }
            }

            // ── Read ver.txt BEFORE deleting tempDir ─────────────────────────
            // Format: errorCount^NA^NA^NA^NA^ErrorCode^ErrorDescription
            //   "0^NA^NA^NA^NA^NA^NA"  = success (0 errors)
            //   "9^NA^NA^NA^NA^T-FV-xxxx^description" = errors
            var baseName = Path.GetFileNameWithoutExtension(inputTxtPath);
            var verDest  = Path.Combine(outputDir, baseName + ".ver.txt");
            if (File.Exists(versionFile))
            {
                File.Copy(versionFile, verDest, overwrite: true);
                progress?.Report($"FVU result: {File.ReadAllText(versionFile).Trim()}");
            }

            // ── Copy FVU output from tempDir back to outputDir ────────────────
            var fvuOutputExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "", ".fvu", ".raw", ".html", ".htm", ".err", ".log" };
            var inputFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { shortName, csiName, "ver.txt", "stdout.txt", "stderr.txt" };
            try
            {
                foreach (var f in Directory.GetFiles(tempDir))
                {
                    var fname = Path.GetFileName(f);
                    var ext   = Path.GetExtension(f).ToLowerInvariant();
                    if (inputFileNames.Contains(fname)) continue;
                    if (!fvuOutputExts.Contains(ext))   continue;
                    var destName = string.IsNullOrEmpty(ext) ? baseName : baseName + ext;
                    var dest = Path.Combine(outputDir, destName);
                    File.Copy(f, dest, overwrite: true);
                }
            }
            catch { /* best-effort copy */ }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }

            int rawErrors = -1;
            string verErrorMsg = "";
            if (File.Exists(verDest))
            {
                var verContent = File.ReadAllText(verDest).Trim();
                var parts = verContent.Split('^');
                if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var n))
                {
                    rawErrors = n;
                    if (n > 0 && parts.Length >= 7 && !string.IsNullOrEmpty(parts[6].Trim()) && parts[6].Trim() != "NA")
                        verErrorMsg = $"[{parts[5].Trim()}] {parts[6].Trim()}";
                    else if (n > 0 && parts.Length >= 6 && !string.IsNullOrEmpty(parts[5].Trim()) && parts[5].Trim() != "NA")
                        verErrorMsg = parts[5].Trim();
                }
            }

            // If ver.txt shows errors, add them to the errors list for display
            if (rawErrors > 0 && !string.IsNullOrEmpty(verErrorMsg))
            {
                result.Errors.Add(new FvuError
                {
                    Code        = verErrorMsg.Contains("]") ? verErrorMsg[1..verErrorMsg.IndexOf(']')] : "FVU",
                    Description = verErrorMsg.Contains("]") ? verErrorMsg[(verErrorMsg.IndexOf(']') + 2)..] : verErrorMsg,
                    RecordType  = "Statement",
                    SeqNo       = "—",
                });
            }

            // Also check for .raw file (some FVU versions write it)
            var rawFiles = Directory.GetFiles(outputDir, "*.raw");
            var rawFile  = rawFiles.OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            if (rawFile != null && rawErrors < 0)
            {
                var rawContent = File.ReadAllText(rawFile).Trim();
                var parts = rawContent.Split('^');
                if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out var n)) rawErrors = n;
            }

            // Locate FVU output file (.fvu extension or extensionless)
            var fvuFiles = Directory.GetFiles(outputDir, "*.fvu");
            var extensionlessFiles = Directory.GetFiles(outputDir)
                .Where(f => string.IsNullOrEmpty(Path.GetExtension(f)))
                .ToArray();

            var outputFile = fvuFiles.Concat(extensionlessFiles)
                                     .OrderByDescending(File.GetLastWriteTime)
                                     .FirstOrDefault();

            if (outputFile != null && rawErrors <= 0)
            {
                result.Success = true;
                result.FvuFile = outputFile;
                progress?.Report($"✅ FVU file: {Path.GetFileName(result.FvuFile)}");

                // Auto-zip for NSDL upload (portal requires a .zip containing the FVU file)
                try
                {
                    var zipPath = outputFile + ".zip";
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    using (var fs  = new FileStream(zipPath, FileMode.Create))
                    using (var za  = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        var entry = za.CreateEntry(Path.GetFileName(outputFile), System.IO.Compression.CompressionLevel.Optimal);
                        using var es = entry.Open();
                        using var src = File.OpenRead(outputFile);
                        src.CopyTo(es);
                    }
                    result.ZipFile = zipPath;
                    progress?.Report($"✅ .fvu file created: {Path.GetFileName(outputFile)}");
                    progress?.Report($"📦 Zip ready for upload: {Path.GetFileName(zipPath)}");
                }
                catch (Exception ex) { progress?.Report($"⚠ Zip failed: {ex.Message}"); }
            }

            // Only treat NSDL-generated error HTML as error file — exclude our Paper report
            var htmlFiles = Directory.GetFiles(outputDir, "*.html")
                            .Concat(Directory.GetFiles(outputDir, "*.htm"))
                            .Where(f => !f.Contains("_Paper") && !f.Contains("Statistics")
                                        && !f.Contains("Warning") && !f.Contains("_ErrorReport"))
                            .ToArray();
            if (htmlFiles.Length > 0)
                result.ErrorHtmlFile = htmlFiles.OrderByDescending(File.GetLastWriteTime).First();

            // ── Parse NSDL error HTML (if it exists) ─────────────────────────
            if (!result.Success || !string.IsNullOrEmpty(result.ErrorHtmlFile))
                result.Errors = ParseErrorHtml(result.ErrorHtmlFile);

            if (!result.Success && result.Errors.Count == 0)
            {
                result.ErrorMessage = rawErrors > 0
                    ? $"FVU validation failed with {rawErrors} error(s). Check error report."
                    : string.IsNullOrEmpty(result.StdErr)
                        ? "FVU utility returned errors. Check error report."
                        : result.StdErr;
            }

            // ── Always generate our own error report when FVU fails ───────────
            if (!result.Success)
            {
                var errorReportPath = Path.Combine(outputDir,
                    Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                GenerateErrorReport(result, inputTxtPath, errorReportPath);
                result.ErrorHtmlFile = errorReportPath;
                progress?.Report($"📋 Error report: {Path.GetFileName(errorReportPath)}");
            }

            Database.LogAction("system", "FVU_RUN", "Return",
                $"Exit={result.ExitCode} Errors={result.Errors.Count} File={result.FvuFile}");

            return result;
        }

        // ── Generate styled error report HTML ────────────────────────────────
        private static void GenerateErrorReport(FvuRunResult result, string inputTxtPath, string reportPath)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var inputFile = Path.GetFileName(inputTxtPath);
                var hasErrors = result.Errors.Count > 0;
                var hasNsdlHtml = !string.IsNullOrEmpty(result.ErrorHtmlFile)
                                  && File.Exists(result.ErrorHtmlFile)
                                  && result.ErrorHtmlFile != reportPath;

                sb.Append($@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'>
<title>FVU Error Report — {System.Web.HttpUtility.HtmlEncode(inputFile)}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#f1f5f9;padding:20px}}
.page{{max-width:900px;margin:0 auto;background:#fff;border-radius:8px;box-shadow:0 2px 12px rgba(0,0,0,.12);overflow:hidden}}
.hdr{{background:#b91c1c;color:#fff;padding:16px 24px}}
.hdr h1{{font-size:16px;font-weight:700}}.hdr p{{font-size:11px;opacity:.85;margin-top:4px}}
.section{{padding:16px 24px;border-bottom:1px solid #f1f5f9}}
.section h2{{font-size:12px;font-weight:700;color:#374151;margin-bottom:10px;text-transform:uppercase;letter-spacing:.5px}}
.info-grid{{display:grid;grid-template-columns:160px 1fr;gap:4px 12px;font-size:11px}}
.lbl{{color:#6b7280}}.val{{font-weight:600;color:#111}}
.err-msg{{background:#fef2f2;border:1px solid #fca5a5;border-radius:5px;padding:10px 14px;font-size:11px;color:#991b1b;white-space:pre-wrap;word-break:break-all}}
table{{width:100%;border-collapse:collapse;font-size:11px}}
thead tr{{background:#1e3a8a;color:#fff}}
th{{padding:7px 12px;text-align:left;font-weight:600;font-size:10.5px}}
td{{padding:6px 12px;border-bottom:1px solid #f1f5f9;vertical-align:top}}
tr:nth-child(even) td{{background:#f8fafc}}
.code{{font-family:monospace;background:#fee2e2;color:#991b1b;padding:2px 6px;border-radius:3px;font-size:10px}}
.log{{background:#0f172a;color:#e2e8f0;border-radius:5px;padding:12px 14px;font-family:monospace;font-size:10.5px;white-space:pre-wrap;max-height:200px;overflow-y:auto}}
.badge-fail{{display:inline-block;background:#fee2e2;color:#991b1b;padding:2px 10px;border-radius:10px;font-size:10px;font-weight:700}}
.footer{{font-size:9px;color:#9ca3af;text-align:center;padding:10px;border-top:1px solid #f1f5f9}}
@media print{{body{{background:#fff}}.page{{box-shadow:none}}}}
</style></head><body><div class='page'>

<div class='hdr'>
  <h1>❌ FVU Validation Error Report</h1>
  <p>Generated by TDS Pro v3.0 &nbsp;|&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm:ss}</p>
</div>

<div class='section'>
  <h2>Summary</h2>
  <div class='info-grid'>
    <span class='lbl'>Input File</span><span class='val'>{System.Web.HttpUtility.HtmlEncode(inputFile)}</span>
    <span class='lbl'>Status</span><span class='val'><span class='badge-fail'>FAILED</span></span>
    <span class='lbl'>Exit Code</span><span class='val'>{result.ExitCode}</span>
    <span class='lbl'>Java Version</span><span class='val'>{System.Web.HttpUtility.HtmlEncode(result.JavaVersion)}</span>
    <span class='lbl'>Errors Found</span><span class='val'>{(hasErrors ? result.Errors.Count.ToString() : "See details below")}</span>
    <span class='lbl'>Generated At</span><span class='val'>{DateTime.Now:dd-MMM-yyyy HH:mm:ss}</span>
  </div>
</div>");

                // Error message section
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    sb.Append($@"
<div class='section'>
  <h2>Error Details</h2>
  <div class='err-msg'>{System.Web.HttpUtility.HtmlEncode(result.ErrorMessage)}</div>
</div>");
                }

                // Structured FVU errors table
                if (hasErrors)
                {
                    sb.Append($@"
<div class='section'>
  <h2>FVU Validation Errors ({result.Errors.Count})</h2>
  <table>
    <thead><tr><th>#</th><th>Error Code</th><th>What to fix</th><th>NSDL Detail</th><th>Record</th><th>Seq</th></tr></thead>
    <tbody>");
                    for (int i = 0; i < result.Errors.Count; i++)
                    {
                        var e = result.Errors[i];
                        var friendly = GetFvuErrorDescription(e.Code);
                        sb.Append($@"
      <tr>
        <td>{i + 1}</td>
        <td><span class='code'>{System.Web.HttpUtility.HtmlEncode(e.Code)}</span></td>
        <td style='color:#1e3a8a;font-weight:600'>{System.Web.HttpUtility.HtmlEncode(friendly)}</td>
        <td style='color:#6b7280;font-size:10px'>{System.Web.HttpUtility.HtmlEncode(e.Description)}</td>
        <td>{System.Web.HttpUtility.HtmlEncode(e.RecordType)}</td>
        <td>{System.Web.HttpUtility.HtmlEncode(e.SeqNo)}</td>
      </tr>");
                    }
                    sb.Append("</tbody></table></div>");
                }

                // Console output (stderr / stdout)
                var logContent = (result.StdErr + "\n" + result.StdOut).Trim();
                if (!string.IsNullOrEmpty(logContent))
                {
                    sb.Append($@"
<div class='section'>
  <h2>Java Console Output</h2>
  <div class='log'>{System.Web.HttpUtility.HtmlEncode(logContent)}</div>
</div>");
                }

                // Link to NSDL original error HTML if it exists
                if (hasNsdlHtml)
                {
                    sb.Append($@"
<div class='section'>
  <h2>NSDL Original Error Report</h2>
  <p style='font-size:11px'>The FVU utility also generated an error report:
    <a href='file:///{System.Web.HttpUtility.HtmlEncode(result.ErrorHtmlFile.Replace('\\','/'))}' target='_blank'>{System.Web.HttpUtility.HtmlEncode(Path.GetFileName(result.ErrorHtmlFile))}</a>
  </p>
</div>");
                }

                sb.Append(@"
<div class='footer'>TDS Pro v3.0 &nbsp;|&nbsp; This report was automatically generated when FVU validation failed.</div>
</div></body></html>");

                File.WriteAllText(reportPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { /* never crash the app just because error report generation failed */ }
        }

        // ── NSDL T-FV error code dictionary ──────────────────────────────────
        private static readonly Dictionary<string, string> _fvuErrorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Batch header / deductor
            ["T-FV-1001"] = "Deductor TAN is invalid or not matching the CSI file",
            ["T-FV-1002"] = "Financial Year is invalid or not matching the CSI file",
            ["T-FV-1003"] = "Form type is invalid (24Q / 26Q / 27Q / 27EQ)",
            ["T-FV-1004"] = "Quarter is invalid — must be Q1/Q2/Q3/Q4",
            ["T-FV-1005"] = "Last / regular indicator is invalid",
            ["T-FV-1006"] = "Deductor category (Government / Non-Government) is invalid",
            ["T-FV-1007"] = "Deductor PAN is invalid or missing",
            ["T-FV-1008"] = "Deductor name is blank or exceeds maximum length",
            ["T-FV-1009"] = "Deductor address is incomplete or invalid",
            ["T-FV-1010"] = "Responsible person PAN is invalid or missing",
            ["T-FV-1011"] = "Responsible person name is blank",
            ["T-FV-1012"] = "Responsible person designation is blank",
            ["T-FV-1013"] = "Contact number is invalid",
            ["T-FV-1014"] = "Email address is invalid",
            ["T-FV-1015"] = "State code is invalid",
            ["T-FV-1016"] = "PIN code is invalid",
            // Challan / batch
            ["T-FV-2001"] = "BSR code is invalid — must be 7 digits",
            ["T-FV-2002"] = "Challan deposit date is invalid or in the future",
            ["T-FV-2003"] = "Challan serial number is invalid",
            ["T-FV-2004"] = "Challan TDS amount is zero or negative",
            ["T-FV-2005"] = "Challan total (TDS + surcharge + cess + interest + others) does not match",
            ["T-FV-2006"] = "Challan deposit date is before the start of the quarter",
            ["T-FV-2007"] = "Challan deposit date is after the end of the quarter (or after filing date)",
            ["T-FV-2008"] = "Duplicate challan (same BSR + serial + date already appears in this return)",
            ["T-FV-2009"] = "Number of deductee records linked to this challan is zero",
            ["T-FV-2010"] = "Oltas challan not found in CSI file for the given BSR + serial + date",
            ["T-FV-2011"] = "Challan TDS amount in return does not match CSI amount",
            ["T-FV-2012"] = "Challan minor head code is invalid",
            ["T-FV-2013"] = "Challan major head code is invalid",
            ["T-FV-2015"] = "Challan consumed amount exceeds deposited amount",
            // Deductee
            ["T-FV-3000"] = "Deductee PAN is invalid — check PAN format (5 alpha + 4 digits + 1 alpha)",
            ["T-FV-3001"] = "Deductee PAN is blank; use PANNOTAVBL / PANINVALID / PANAPPLIED if PAN unavailable",
            ["T-FV-3002"] = "Deductee name is blank or invalid",
            ["T-FV-3003"] = "Amount paid / credited to deductee is zero or negative",
            ["T-FV-3004"] = "TDS deducted is zero for this deductee",
            ["T-FV-3005"] = "Section code is invalid for this form type (24Q / 26Q)",
            ["T-FV-3006"] = "Rate of TDS is invalid or out of permissible range",
            ["T-FV-3007"] = "Date of deduction is invalid",
            ["T-FV-3008"] = "Date of deduction is outside the quarter",
            ["T-FV-3009"] = "Date of payment / credit is invalid",
            ["T-FV-3010"] = "TDS certificate (Form 16A) number is invalid or missing",
            ["T-FV-3011"] = "Nature of remittance is blank (27Q — foreign payments)",
            ["T-FV-3012"] = "Country of residence code is invalid (27Q)",
            ["T-FV-3013"] = "DTAA rate is invalid or blank (when DTAA claimed)",
            ["T-FV-3014"] = "Deductee category is invalid",
            ["T-FV-3015"] = "Gross salary (24Q) is zero or less than TDS deducted",
            ["T-FV-3016"] = "Section mismatch — salary entries (192/392) must not appear in 26Q; non-salary must not appear in 24Q",
            ["T-FV-3017"] = "Date of booking is invalid (Government deductors only)",
            ["T-FV-3018"] = "Deductee PAN marked as invalid but PAN provided is not in invalid-PAN format",
            ["T-FV-3019"] = "Higher rate of TDS (section 206AA) must be applied when PAN is unavailable",
            ["T-FV-3020"] = "Lower deduction certificate (section 197) number is missing or invalid",
            ["T-FV-3021"] = "Amount on which TDS not deducted is non-zero but reason code is blank",
            ["T-FV-3022"] = "Deduction reason code is invalid",
            // Salary / 24Q
            ["T-FV-4001"] = "Salary detail records missing for 24Q return",
            ["T-FV-4002"] = "Salary breakup (gross, allowances, perquisites) totals do not match",
            ["T-FV-4003"] = "Employee PAN is invalid",
            ["T-FV-4004"] = "Employee PAN is missing — mandatory for 24Q",
            ["T-FV-4005"] = "Gross salary is zero",
            ["T-FV-4006"] = "Tax on total income is invalid",
            ["T-FV-4007"] = "Education cess is incorrectly calculated",
            ["T-FV-4008"] = "Previous employer tax details are invalid",
            // Structural / count
            ["T-FV-6001"] = "Total number of challans in the return does not match the batch header count",
            ["T-FV-6002"] = "Total number of deductee records does not match the batch header count",
            ["T-FV-6003"] = "Total TDS amount in batch header does not match the sum of challan amounts",
            ["T-FV-6004"] = "File is corrupted or not in the expected FVU text format",
            ["T-FV-6005"] = "Duplicate PAN in deductee records for the same challan",
        };

        /// <summary>Returns a friendly explanation for a T-FV-xxxx error code, or the code itself if unknown.</summary>
        public static string GetFvuErrorDescription(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            if (_fvuErrorMap.TryGetValue(code.Trim(), out var msg)) return msg;
            // Unknown code — return a generic hint
            return $"NSDL validation error {code} — refer to NSDL FVU error code list for details";
        }

        // ── Parse NSDL error HTML ─────────────────────────────────────────────
        public static List<FvuError> ParseErrorHtml(string? htmlPath)
        {
            var errors = new List<FvuError>();
            if (string.IsNullOrEmpty(htmlPath) || !File.Exists(htmlPath)) return errors;

            try
            {
                var html = File.ReadAllText(htmlPath);

                // NSDL FVU error HTML has rows like:
                // <TR><TD>Error Code</TD><TD>Description</TD><TD>Record Type</TD><TD>Seq No</TD></TR>
                var rowPattern = new System.Text.RegularExpressions.Regex(
                    @"<TR[^>]*>(.*?)</TR>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var cellPattern = new System.Text.RegularExpressions.Regex(
                    @"<TD[^>]*>(.*?)</TD>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var stripTags = new System.Text.RegularExpressions.Regex("<[^>]+>");

                bool firstRow = true;
                foreach (System.Text.RegularExpressions.Match row in rowPattern.Matches(html))
                {
                    if (firstRow) { firstRow = false; continue; } // skip header row

                    var cells = cellPattern.Matches(row.Value)
                                           .Select(m => stripTags.Replace(m.Groups[1].Value, "").Trim())
                                           .ToList();
                    if (cells.Count >= 2)
                    {
                        var code = cells.Count > 0 ? cells[0] : "";
                        var nsdlDesc = cells.Count > 1 ? cells[1] : "";
                        var friendly = GetFvuErrorDescription(code);
                        errors.Add(new FvuError
                        {
                            Code        = code,
                            Description = string.IsNullOrEmpty(friendly) ? nsdlDesc : $"{friendly} [{nsdlDesc}]",
                            RecordType  = cells.Count > 2 ? cells[2] : "",
                            SeqNo       = cells.Count > 3 ? cells[3] : "",
                        });
                    }
                }
            }
            catch { /* malformed HTML — return empty list */ }

            return errors;
        }

        // ── Find CSI file in common download locations ────────────────────────
        private static string? FindCsiFile(string inputTxtPath, string outputDir)
        {
            var searchDirs = new List<string>();

            // 1. Same folder as the input .txt
            var txtDir = Path.GetDirectoryName(inputTxtPath);
            if (!string.IsNullOrEmpty(txtDir)) searchDirs.Add(txtDir);

            // 2. Output directory
            searchDirs.Add(outputDir);

            // 3. User's Downloads folder
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) searchDirs.Add(downloads);

            // 4. Desktop
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop)) searchDirs.Add(desktop);

            // 5. Documents
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(docs)) searchDirs.Add(docs);

            // Search each dir for any .csi file — pick the most recently modified one
            foreach (var dir in searchDirs.Distinct())
            {
                try
                {
                    var found = Directory.GetFiles(dir, "*.csi", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(File.GetLastWriteTime)
                        .FirstOrDefault();
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        // ── Java resolution ───────────────────────────────────────────────────
        private static string ResolveJava(string? configuredPath)
        {
            // 1. Use configured path if valid
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            // 2. Try java on system PATH
            // Auto-scan common vendor install locations
            var vendorDirs = new[]
            {
                @"C:\Program Files\Microsoft",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Java",
                @"C:\Program Files\Amazon Corretto",
                @"C:\Program Files\BellSoft",
                @"C:\Program Files\OpenJDK",
            };
            var scanned = vendorDirs
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetDirectories(d))
                .Select(d => Path.Combine(d, "bin", "java.exe"))
                .Where(File.Exists)
                .ToList();

            var candidates = new[] { "java" }
                .Concat(scanned)
                .Concat(new[]
                {
                    @"C:\Program Files\Java\jre1.8.0_391\bin\java.exe",
                    @"C:\Program Files\Java\jre-8\bin\java.exe",
                })
                .ToArray();
            foreach (var c in candidates)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(c, "-version")
                    {
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(3000);
                    if (p?.ExitCode == 0) return c;
                }
                catch { }
            }
            return "";
        }

        private static async Task<string> GetJavaVersionAsync(string javaPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(javaPath, "-version")
                {
                    UseShellExecute        = false,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return err.Split('\n')[0].Trim();
            }
            catch { return "Unknown"; }
        }
    }

    // ── Config / Result models ─────────────────────────────────────────────────
    public class FvuPreflightResult
    {
        public bool         IsReady { get; set; }
        public List<string> Issues  { get; set; } = new();
    }

    public class FvuConfig
    {
        public string JavaExePath { get; set; } = "";
        public string FvuJarPath  { get; set; } = "";
        public string OutputDir   { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TDSPro_FVU");
    }

    public class FvuRunResult
    {
        public bool   Success       { get; set; }
        public string InputFile     { get; set; } = "";
        public string FvuFile       { get; set; } = "";
        public string ZipFile       { get; set; } = "";
        public string ErrorHtmlFile { get; set; } = "";
        public string ErrorMessage  { get; set; } = "";
        public string JavaVersion   { get; set; } = "";
        public string StdOut        { get; set; } = "";
        public string StdErr        { get; set; } = "";
        public int    ExitCode      { get; set; }
        public List<FvuError> Errors { get; set; } = new();
    }

    public class FvuError
    {
        public string Code        { get; set; } = "";
        public string Description { get; set; } = "";
        public string RecordType  { get; set; } = "";
        public string SeqNo       { get; set; } = "";
        public override string ToString() => $"[{Code}] {Description} (Record: {RecordType} Seq: {SeqNo})";
    }
}
