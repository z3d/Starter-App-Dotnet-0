using Npgsql;

namespace StarterApp.Api.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueConstraintViolation(this DbUpdateException exception, string? constraintName = null)
    {
        var postgresException = FindPostgresException(exception);
        if (postgresException?.SqlState != PostgresErrorCodes.UniqueViolation)
            return false;

        return string.IsNullOrWhiteSpace(constraintName)
            || string.Equals(postgresException.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase)
            || postgresException.MessageText.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
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
}
