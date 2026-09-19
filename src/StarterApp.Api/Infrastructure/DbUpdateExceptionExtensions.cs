using Npgsql;

namespace StarterApp.Api.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    // A C# 14 extension block: the receiver is named once, the parameterless checks are extension
    // properties, and the constraint-scoped checks stay methods because they take an argument.
    extension(DbUpdateException exception)
    {
        public bool IsUniqueConstraintViolation(string? constraintName = null) =>
            HasPostgresSqlState(exception, PostgresErrorCodes.UniqueViolation, constraintName);

        public bool IsForeignKeyViolation(string? constraintName = null) =>
            HasPostgresSqlState(exception, PostgresErrorCodes.ForeignKeyViolation, constraintName);

        public bool IsCheckConstraintViolation(string? constraintName = null) =>
            HasPostgresSqlState(exception, PostgresErrorCodes.CheckViolation, constraintName);

        public bool IsNotNullViolation =>
            FindPostgresException(exception)?.SqlState == PostgresErrorCodes.NotNullViolation;

        public bool IsStringTruncationViolation =>
            FindPostgresException(exception)?.SqlState == PostgresErrorCodes.StringDataRightTruncation;
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
