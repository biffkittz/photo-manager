using System.Text.RegularExpressions;
using Npgsql;

namespace PhotoManager.Migrator;

/// <summary>
/// Creates PostgreSQL roles for the app's managed identities and grants least privilege. Neither tier may
/// DELETE anything or alter the schema; the web tier can only append to the audit trail and cannot read it back;
/// the workers cannot touch it. Only the migrator identity (the server's Entra administrator) owns the tables.
/// </summary>
internal static partial class RoleProvisioner
{
    private static readonly string[] DataTables = ["photos", "maintenance_jobs", "prepared_downloads"];

    /// <summary>Ensures each role exists (via the <c>postgres</c> database) and applies grants in the app database.</summary>
    public static async Task ProvisionAsync(NpgsqlDataSource adminDataSource, NpgsqlDataSource appDataSource, string databaseName, IReadOnlyList<AppRole> roles, CancellationToken cancellationToken)
    {
        // Identifiers cannot be bound as parameters, so names are validated strictly and then quoted.
        if (!RoleName().IsMatch(databaseName))
        {
            throw new InvalidOperationException($"Invalid database name '{databaseName}'.");
        }

        foreach (var role in roles)
        {
            if (!RoleName().IsMatch(role.Name) || role.ObjectId == Guid.Empty)
            {
                throw new InvalidOperationException($"Invalid application role definition '{role.Name}'.");
            }

            await using (var connection = await adminDataSource.OpenConnectionAsync(cancellationToken))
            {
                await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @name", connection);
                exists.Parameters.AddWithValue("name", role.Name);
                if (await exists.ExecuteScalarAsync(cancellationToken) is null)
                {
                    // Azure Database for PostgreSQL function that maps an Entra service principal to a login role.
                    await using var create = new NpgsqlCommand("SELECT * FROM pgaadauth_create_principal_with_oid(@name, @oid, 'service', false, false)", connection);
                    create.Parameters.AddWithValue("name", role.Name);
                    create.Parameters.AddWithValue("oid", role.ObjectId.ToString());
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            var quotedRole = $"\"{role.Name}\"";

            // Revoke, then grant exactly what the tier needs, in one transaction: grants are additive, so this is
            // what makes a narrowed privilege set actually take effect on databases provisioned earlier.
            var statements = new List<string>
            {
                $"REVOKE ALL ON {string.Join(", ", DataTables)}, audit_events FROM {quotedRole}",
                $"GRANT CONNECT ON DATABASE \"{databaseName}\" TO {quotedRole}",
                $"GRANT USAGE ON SCHEMA public TO {quotedRole}",
                $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {quotedRole}",
            };

            if (role.Kind == AppRoleKind.Web)
            {
                statements.AddRange(
                [
                    $"GRANT SELECT, INSERT, UPDATE ON photos, maintenance_jobs TO {quotedRole}",

                    // Downloads: create them, read them, and record a failed start; preparing them is worker work.
                    $"GRANT SELECT, INSERT ON prepared_downloads TO {quotedRole}",
                    $"GRANT UPDATE (status, error, updated_at) ON prepared_downloads TO {quotedRole}",

                    // The audit trail is append-only for the web tier. SELECT on the id column only: EF Core reads
                    // the generated id back (INSERT ... RETURNING id), but past events stay unreadable.
                    $"GRANT INSERT ON audit_events TO {quotedRole}",
                    $"GRANT SELECT (id) ON audit_events TO {quotedRole}",
                ]);
            }
            else
            {
                statements.Add($"GRANT SELECT, INSERT, UPDATE ON {string.Join(", ", DataTables)} TO {quotedRole}");
            }

            await using var appConnection = await appDataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await appConnection.BeginTransactionAsync(cancellationToken);
            foreach (var sql in statements)
            {
                await using var command = new NpgsqlCommand(sql, appConnection, transaction);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,62}$")]
    private static partial Regex RoleName();
}
