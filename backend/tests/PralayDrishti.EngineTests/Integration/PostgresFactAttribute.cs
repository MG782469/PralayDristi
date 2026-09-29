using System.Net.Sockets;

namespace PralayDrishti.EngineTests.Integration;

/// <summary>Runs only when the dev Postgres from docker-compose.dev.yml is reachable.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionString =
        "Host=localhost;Port=5432;Database=pralaydrishti_test;Username=pralay;Password=pralay;Include Error Detail=true";

    public PostgresFactAttribute()
    {
        if (!Reachable("localhost", 5432))
            Skip = "Postgres not reachable on localhost:5432 (start docker-compose.dev.yml)";
    }

    internal static bool Reachable(string host, int port)
    {
        try
        {
            using var c = new TcpClient();
            return c.ConnectAsync(host, port).Wait(TimeSpan.FromMilliseconds(400)) && c.Connected;
        }
        catch
        {
            return false;
        }
    }
}
