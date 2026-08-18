namespace SchoolPOS.Data;

/// <summary>
/// Nombre del ensamblado que contiene las migraciones de SQLite. Los hosts lo pasan a
/// <c>UseSqlite(..., o =&gt; o.MigrationsAssembly(...))</c> para que EF busque ahí y no en
/// <c>SchoolPOS.Data</c>, donde viven las de SQL Server.
/// </summary>
public static class SqliteMigrations
{
    public const string AssemblyName = "SchoolPOS.Data.Migrations.Sqlite";
}
