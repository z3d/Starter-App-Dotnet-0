using Npgsql;

namespace StarterApp.Api.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueConstraintViolation(this DbUpdateException exception, string? constraintName = null)
    {
        return HasPostgresSqlState(exception, PostgresErrorCodes.UniqueViolation, constraintName);
    }

    public static bool IsForeignKeyViolation(this DbUpdateException exception, string? constraintName = null)
    {
        return HasPostgresSqlState(exception, PostgresErrorCodes.ForeignKeyViolation, constraintName);
    }

    public static bool IsCheckConstraintViolation(this DbUpdateException exception, string? constraintName = null)
    {
        return HasPostgresSqlState(exception, PostgresErrorCodes.CheckViolation, constraintName);
    }

    public static bool IsNotNullViolation(this DbUpdateException exception)
    {
        var postgresException = FindPostgresException(exception);
        return postgresException?.SqlState == PostgresErrorCodes.NotNullViolation;
    }

    public static bool IsStringTruncationViolation(this DbUpdateException exception)
    {
        var postgresException = FindPostgresException(exception);
        return postgresException?.SqlState == PostgresErrorCodes.StringDataRightTruncation;
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
                return postgresException;
        }

        return null;
    }

    private static bool HasPostgresSqlState(DbUpdateException exception, string sqlState, string? constraintName)
    {
        var postgresException = FindPostgresException(exception);
        if (postgresException?.SqlState != sqlState)
            return false;

        return string.IsNullOrWhiteSpace(constraintName)
            || string.Equals(postgresException.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase)
            || postgresException.MessageText.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
    }
}
