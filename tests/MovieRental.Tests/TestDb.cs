namespace MovieRental.Tests;

/// <summary>
/// Where the database tests create their throwaway databases. "(localdb)\MSSQLLocalDB" only works
/// in the logon session that started LocalDB; when the site's scheduled task started it, the
/// tests could not reach it from a terminal. Its named pipe works from any session.
/// TEST_SQL_SERVER overrides both.
/// </summary>
internal static class TestDb
{
    public static string Server { get; } = Resolve();

    public static string ConnectionString(string database) =>
        $"Server={Server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private static string Resolve()
    {
        if (Environment.GetEnvironmentVariable("TEST_SQL_SERVER") is { Length: > 0 } configured) return configured;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var pipe = Directory.GetFiles(@"\\.\pipe\").FirstOrDefault(p => p.Contains("LOCALDB", StringComparison.OrdinalIgnoreCase));
                if (pipe is not null) return "np:" + pipe;
            }
            catch (IOException) { /* fall back below */ }
        }
        return @"(localdb)\MSSQLLocalDB";
    }
}
