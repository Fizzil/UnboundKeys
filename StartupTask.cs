using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace UnboundKeys;

// "Start with Windows" for an app that runs as administrator. The Startup
// folder and the Run registry key are out: Windows quietly skips elevated
// programs there (and would prompt at every sign-in if it didn't). A
// scheduled task that fires at this user's sign-in with the highest run
// level starts it elevated and silent, which is what every admin-level
// tray app does. The app is already elevated, so it can create and remove
// the task itself through schtasks.exe. Program.cs registers it afresh on
// every launch while the setting is on, so the task always points at the
// copy that last ran (Check for updates changes the exe's folder).
public static class StartupTask
{
    public const string TaskName = "UnboundKeys";

    // Passed by the task so Program.cs can tell an automatic start from a
    // click on the exe (the "start paused" preference applies only here).
    public const string AutoStartArgument = "--autostart";

    public static bool IsRegistered() => Run("/Query", "/TN", TaskName).ExitCode == 0;

    public static void Register()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Couldn't find this exe's path.");
        string dir = Path.GetDirectoryName(exe) ?? throw new InvalidOperationException("Couldn't find this exe's folder.");
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Couldn't find this user's id.");

        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var task = new XElement(ns + "Task", new XAttribute("version", "1.4"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Description", "Starts UnboundKeys when you sign in (Settings > Start with Windows).")),
            new XElement(ns + "Triggers",
                new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "UserId", sid),
                    new XElement(ns + "Delay", "PT5S"))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", sid),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "AllowHardTerminate", "false"),
                new XElement(ns + "StartWhenAvailable", "true"),
                new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(ns + "IdleSettings",
                    new XElement(ns + "StopOnIdleEnd", "false"),
                    new XElement(ns + "RestartOnIdle", "false")),
                new XElement(ns + "AllowStartOnDemand", "true"),
                new XElement(ns + "Enabled", "true"),
                new XElement(ns + "Hidden", "false"),
                new XElement(ns + "RunOnlyIfIdle", "false"),
                new XElement(ns + "WakeToRun", "false"),
                // Task Scheduler's default is three days, after which it
                // would kill the app mid-game. PT0S means no limit.
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                // 5 is normal priority; the default (7) is below normal,
                // which is no place for an input hook.
                new XElement(ns + "Priority", "5")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", exe),
                    new XElement(ns + "Arguments", AutoStartArgument),
                    new XElement(ns + "WorkingDirectory", dir))));

        string xmlPath = Path.Combine(Path.GetTempPath(), "UnboundKeys-startup-task.xml");
        var doc = new XDocument(new XDeclaration("1.0", "UTF-16", null), task);
        using (var writer = XmlWriter.Create(xmlPath, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true }))
            doc.Save(writer);
        try
        {
            var result = Run("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
            if (result.ExitCode != 0)
                throw new InvalidOperationException(Clean(result));
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* a leftover temp file is harmless */ }
        }
    }

    public static void Unregister()
    {
        var result = Run("/Delete", "/TN", TaskName, "/F");
        // Deleting a task that isn't there is the outcome we wanted anyway.
        if (result.ExitCode != 0 && IsRegistered())
            throw new InvalidOperationException(Clean(result));
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException("schtasks.exe didn't start.");
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, error);
    }

    private static string Clean((int ExitCode, string Output, string Error) result)
    {
        string text = (result.Error.Length > 0 ? result.Error : result.Output).Trim();
        return text.Length == 0 ? "Task Scheduler reported an error." : text.Replace("ERROR: ", "");
    }
}
