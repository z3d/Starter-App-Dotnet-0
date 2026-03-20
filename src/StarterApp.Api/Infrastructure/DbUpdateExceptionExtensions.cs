namespace StarterApp.Api.Infrastructure;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueConstraintViolation(this DbUpdateException exception, string? constraintName = null)
    {
        var sqlException = FindSqlException(exception);
        if (sqlException == null || (sqlException.Number != 2601 && sqlException.Number != 2627))
            return false;

        return string.IsNullOrWhiteSpace(constraintName)
            || sqlException.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStringTruncationViolation(this DbUpdateException exception)
    {
        var sqlException = FindSqlException(exception);
        return sqlException is { Number: 2628 or 8152 };
    }

    private static Microsoft.Data.SqlClient.SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is Microsoft.Data.SqlClient.SqlException sqlException)
                return sqlException;
        }

        return null;
    }
}
