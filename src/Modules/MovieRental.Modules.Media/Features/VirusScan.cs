using System.Diagnostics;

namespace MovieRental.Modules.Media.Features;

public enum ScanVerdict { Clean, Infected, NotScanned }

/// <summary>
/// An uploaded film is scanned with Microsoft Defender's command-line scanner before anybody
/// is shown it: a reviewer opens every submission, and a file that is not what it claims to be
/// would land on their machine first.
///
/// Where the server has no Defender (Linux, a container) or the scan cannot finish, the answer
/// is NotScanned and the caller lets the file through with a warning in the log: a broken
/// scanner must not close uploads for everyone.
/// ponytail: Defender only; a Linux host would need ClamAV (clamdscan) or a storage-side scan.
/// </summary>
internal static class VirusScan
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>Windows' own "this file contains a virus" (ERROR_VIRUS_INFECTED, _DELETED):
    /// real-time protection refusing the write before the scan below ever runs.</summary>
    public static bool IsBlockedWrite(Exception ex) =>
        ex is IOException { HResult: unchecked((int)0x800700E1) or unchecked((int)0x800700E2) };

    public static async Task<ScanVerdict> ScanAsync(string path, CancellationToken ct)
    {
        var scanner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(scanner)) return ScanVerdict.NotScanned;

        // -DisableRemediation: report, do not quarantine; the caller deletes the file and says why.
        using var process = Process.Start(new ProcessStartInfo(scanner)
        {
            ArgumentList = { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" },
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        });
        if (process is null) return ScanVerdict.NotScanned;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            _ = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return ScanVerdict.NotScanned;
        }

        // 0: nothing found. 2: a threat found. Anything else: Defender switched off, another
        // antivirus in charge, a scan error — not an answer about this file.
        return process.ExitCode switch { 0 => ScanVerdict.Clean, 2 => ScanVerdict.Infected, _ => ScanVerdict.NotScanned };
    }
}
