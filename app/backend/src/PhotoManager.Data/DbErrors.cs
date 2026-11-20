using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PhotoManager.Data;

/// <summary>Recognizes specific PostgreSQL errors behind EF Core exceptions.</summary>
public static class DbErrors
{
    /// <summary>Name of the unique index that allows only one ready photo per content hash.</summary>
    public const string UniqueReadyContentIndex = "ux_photos_sha256_ready";

    /// <summary>Name of the unique index that links a Google account to at most one current family member.</summary>
    public const string UniqueGoogleAccountIndex = "ix_family_members_google_subject";

    /// <summary>True when <paramref name="exception"/> is a unique-constraint violation of <paramref name="indexName"/>.</summary>
    /// <param name="exception">The exception thrown by <c>SaveChangesAsync</c>.</param>
    /// <param name="indexName">The unique index or constraint name.</param>
    /// <returns>Whether the save failed because of that unique index.</returns>
    public static bool IsUniqueViolation(DbUpdateException exception, string indexName) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && string.Equals(postgres.ConstraintName, indexName, StringComparison.Ordinal);
}
