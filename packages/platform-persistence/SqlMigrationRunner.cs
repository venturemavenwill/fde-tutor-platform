using Microsoft.EntityFrameworkCore;

namespace FdeTutor.Persistence;

public sealed class SqlMigrationRunner(FdeTutorDbContext dbContext)
{
    public async Task ApplyAsync(
        string migrationsRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(migrationsRoot))
        {
            throw new DirectoryNotFoundException(
                $"The migration directory '{migrationsRoot}' does not exist.");
        }

        var files = Directory
            .EnumerateFiles(migrationsRoot, "*.sql", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"No SQL migrations were found in '{migrationsRoot}'.");
        }

        foreach (var file in files)
        {
            var sql = await File.ReadAllTextAsync(file, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }
}
