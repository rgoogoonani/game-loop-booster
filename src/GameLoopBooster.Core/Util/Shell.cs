using System.Diagnostics;
using System.Text;

namespace GameLoopBooster.Core.Util;

public sealed record ShellResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Runs Windows command-line tools (powercfg, bcdedit, powershell, schtasks) without a console window.</summary>
public static class Shell
{
    public static ShellResult Run(string fileName, string arguments, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { /* already gone */ }
            return new ShellResult(-1, stdout.IsCompleted ? stdout.Result : "", "timeout");
        }
        return new ShellResult(p.ExitCode, stdout.Result, stderr.Result);
    }

    public static ShellResult PowerShell(string script, int timeoutMs = 60000)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", timeoutMs);
    }

    public static string SystemTool(string exe) => Path.Combine(Environment.SystemDirectory, exe);
}
