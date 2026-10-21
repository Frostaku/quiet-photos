using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class PhotosChoiceProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool remove = args.Length == 1 && args[0] == "--remove-machine-confirmed";
        using (var window = new PhotosChoiceWindow(remove)) Application.Run(window);
        return PhotosChoiceWindow.Result;
    }
}

internal sealed class ScriptResult
{
    public int Code; public string Output, Error;
}

internal sealed class PhotosChoiceWindow : Form
{
    public static int Result;
    private readonly bool removalMode;
    private readonly Label heading = new Label(), description = new Label(), status = new Label();
    private readonly CheckBox consent = new CheckBox();
    private readonly Button finish = new Button();
    private readonly Button keep = new Button();
    private bool busy;
    public PhotosChoiceWindow(bool removalMode)
    {
        this.removalMode = removalMode; Text = "Quiet Photos Setup - Microsoft Photos";
        ClientSize = new Size(590, 360); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(32, 33, 36); ForeColor = Color.FromArgb(237, 238, 242);
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuietPhotos.Icon.ico")) Icon = new Icon(stream);
        heading.SetBounds(28, 25, 530, 40); heading.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        heading.Text = removalMode ? "Removing Microsoft Photos" : "Keep Microsoft Photos?";
        description.SetBounds(28, 78, 530, 104); description.ForeColor = Color.FromArgb(188, 193, 204);
        description.Text = removalMode ? "Checking all Windows accounts and the provisioned copy. Your picture files are not deleted." :
            "Quiet Photos has installed successfully. You can also uninstall Microsoft Photos from this PC. This removes it for all Windows accounts and removes its provisioned copy for future accounts. Administrator approval is required. Windows may remove Photos app settings; your picture files are not deleted.";
        status.SetBounds(28, 190, 530, 42); status.Text = "Scanning for Microsoft Photos…";
        consent.SetBounds(28, 244, 530, 42); consent.Text = "Uninstall Microsoft Photos from this PC (all accounts)";
        consent.Checked = false; consent.Enabled = false; consent.ForeColor = ForeColor;
        consent.CheckedChanged += delegate { finish.Text = consent.Checked ? "Uninstall Photos…" : "Keep Photos and finish"; };
        finish.SetBounds(340, 304, 220, 34); finish.Text = "Keep Photos and finish"; finish.Enabled = !removalMode;
        finish.FlatStyle = FlatStyle.Flat; finish.BackColor = Color.FromArgb(48, 67, 92); finish.ForeColor = ForeColor;
        finish.Click += async delegate { await FinishAsync(); };
        keep.SetBounds(28, 304, 160, 34); keep.Text = "Keep Microsoft Photos"; keep.FlatStyle = FlatStyle.Flat; keep.ForeColor = ForeColor;
        keep.Click += delegate { if (!busy) Close(); };
        keep.Visible = !removalMode;
        Controls.AddRange(new Control[] { heading, description, status, consent, finish, keep });
        AcceptButton = finish; CancelButton = removalMode ? finish : keep;
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        Shown += async delegate { if (removalMode) await RemoveAsync(); else await ScanAsync(); };
    }
    private static string Script()
    { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuietPhotos.PhotosPackages.ps1")) using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }
    private static async Task<ScriptResult> RunScriptAsync(string command, int timeout)
    {
        string imports = "Import-Module (Join-Path $PSHOME 'Modules\\Appx\\Appx.psd1') -ErrorAction Stop\n";
        if (command.Contains("Invoke-PhotosMachineRemoval")) imports += "Import-Module (Join-Path $PSHOME 'Modules\\Dism\\Dism.psd1') -ErrorAction Stop\n";
        string code = "$ErrorActionPreference='Stop'\n" + imports + Script() + "\ntry { " + command + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\WindowsPowerShell\\v1.0\\powershell.exe"),
            "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(code)))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            bool exited = await Task.Run(delegate { return process.WaitForExit(timeout); });
            if (!exited) { process.Kill(); throw new TimeoutException("Windows did not finish the Photos operation in time. Quiet Photos remains installed."); }
            return new ScriptResult { Code = process.ExitCode, Output = await output, Error = await error };
        }
    }
    private async Task ScanAsync()
    {
        try
        {
            var scan = await RunScriptAsync("if ((Get-PhotosAccountScan).Found) { [Console]::Write('FOUND') } else { [Console]::Write('ABSENT') }", 30000);
            if (IsDisposed) return;
            if (scan.Code != 0) throw new InvalidOperationException(scan.Error.Trim());
            if (scan.Output.Trim() == "FOUND") status.Text = "Microsoft Photos was found in your Windows account.";
            else if (scan.Output.Trim() == "ABSENT") { status.Text = "Microsoft Photos was not found in this account. Other accounts require an administrator scan."; consent.Text = "Scan all accounts and uninstall Microsoft Photos if found"; }
            else throw new InvalidOperationException("Windows returned an unexpected scan result.");
        }
        catch (Exception) { if (IsDisposed) return; status.Text = "Photos detection could not finish. You can keep it, or request an administrator scan and removal."; }
        if (!IsDisposed) consent.Enabled = true;
    }
    private async Task FinishAsync()
    {
        if (removalMode || !consent.Checked) { Close(); return; }
        busy = true; consent.Enabled = finish.Enabled = keep.Enabled = false;
        string directory = null;
        try
        {
            // MSI extracts Binary EXEs with a .tmp extension; use a real .exe for ShellExecute/UAC.
            directory = Path.Combine(Path.GetTempPath(), "QuietPhotos-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); string helper = Path.Combine(directory, "QuietPhotos.PhotosChoice.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, helper, false);
            var start = new ProcessStartInfo(helper, "--remove-machine-confirmed") { UseShellExecute = true, Verb = "runas" };
            using (var child = Process.Start(start))
            { status.Text = "Waiting for the administrator scan and removal…"; await Task.Run(delegate { child.WaitForExit(); }); Result = child.ExitCode; }
            busy = false; Close();
        }
        catch (Win32Exception e)
        {
            status.Text = e.NativeErrorCode == 1223 ? "Administrator approval was cancelled. Microsoft Photos was kept." : "Removal could not start: " + e.Message;
            consent.Checked = false; consent.Enabled = true; finish.Enabled = keep.Enabled = true; busy = false;
        }
        catch (Exception e) { status.Text = "Removal could not start: " + e.Message; consent.Checked = false; consent.Enabled = true; finish.Enabled = keep.Enabled = true; busy = false; }
        finally
        {
            if (directory != null)
            {
                try { File.Delete(Path.Combine(directory, "QuietPhotos.PhotosChoice.exe")); Directory.Delete(directory, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
    private async Task RemoveAsync()
    {
        consent.Visible = false; finish.Text = "Finish"; busy = true; finish.Enabled = false;
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Administrator approval was not granted. Microsoft Photos was kept.");
            var result = await RunScriptAsync("Invoke-PhotosMachineRemoval -Consent | Out-Null; [Console]::Write('REMOVED')", 600000);
            if (result.Code != 0) throw new InvalidOperationException(result.Error.Trim());
            heading.Text = "Microsoft Photos removed"; status.Text = "The all-account and provisioned-package checks are clear. Quiet Photos is ready.";
        }
        catch (Exception e)
        {
            Result = 1; heading.Text = "Quiet Photos is installed"; status.Text = "Microsoft Photos removal did not complete.";
            MessageBox.Show(this, e.Message, "Microsoft Photos removal", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { busy = false; finish.Enabled = true; }
    }
}
