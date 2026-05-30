namespace StarterApp.Tests.Infrastructure.Persistence;

public class DbUpdateExceptionExtensionsTests
{
    [Theory]
    [InlineData("23505", StatusCodes.Status409Conflict)] // unique_violation
    [InlineData("23503", StatusCodes.Status409Conflict)] // foreign_key_violation
    [InlineData("22001", StatusCodes.Status400BadRequest)] // string_data_right_truncation
    [InlineData("23514", StatusCodes.Status400BadRequest)] // check_violation
    [InlineData("23502", StatusCodes.Status400BadRequest)] // not_null_violation
    public void ResolveExceptionStatusCode_WithPostgresIntegrityViolation_ShouldReturnExpectedStatusCode(
        string sqlState,
        int expectedStatusCode)
    {
        var exception = CreateDbUpdateException(sqlState);

        var statusCode = WebApplicationExtensions.ResolveExceptionStatusCode(exception);

        Assert.Equal(expectedStatusCode, statusCode);
    }

    [Fact]
    public void IsForeignKeyViolation_WithConstraintName_ShouldMatchCaseInsensitively()
    {
        var exception = CreateDbUpdateException(PostgresErrorCodes.ForeignKeyViolation, "fk_orders_customer_id");

        Assert.True(exception.IsForeignKeyViolation("FK_ORDERS_CUSTOMER_ID"));
        Assert.False(exception.IsForeignKeyViolation("fk_other_constraint"));
    }

    [Fact]
    public void IsCheckConstraintViolation_WithConstraintName_ShouldMatchCaseInsensitively()
    {
        var exception = CreateDbUpdateException(PostgresErrorCodes.CheckViolation, "ck_products_stock_non_negative");

        Assert.True(exception.IsCheckConstraintViolation("CK_PRODUCTS_STOCK_NON_NEGATIVE"));
        Assert.False(exception.IsCheckConstraintViolation("ck_other_constraint"));
    }

    private static DbUpdateException CreateDbUpdateException(string sqlState, string? constraintName = null)
    {
        return new DbUpdateException("Database update failed", CreatePostgresException(sqlState, constraintName));
    }

    private static PostgresException CreatePostgresException(string sqlState, string? constraintName)
    {
        return new PostgresException(
            "constraint failed",
            "ERROR",
            "ERROR",
            sqlState,
            constraintName: constraintName);
    }
}
