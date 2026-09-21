using StarterApp.Api.Application.Queries;
using StarterApp.Api.Application.Validators;

namespace StarterApp.Tests.Application;

public class PagedQueryValidatorTests
{
    // page * pageSize must not overflow int32 into a negative SQL OFFSET (which Postgres rejects -> 500).
    // The upper bound keeps the offset arithmetic safe on every list endpoint.

    [Fact]
    public void GetOrdersByStatusQueryValidator_WithExcessivePage_ShouldReturnPageError()
    {
        var errors = new GetOrdersByStatusQueryValidator()
            .Validate(new GetOrdersByStatusQuery { Status = OrderStatus.Pending, Page = 200_000, PageSize = 100 })
            .ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(GetOrdersByStatusQuery.Page));
    }

    [Fact]
    public void GetOrdersByCustomerQueryValidator_WithExcessivePage_ShouldReturnPageError()
    {
        var errors = new GetOrdersByCustomerQueryValidator()
            .Validate(new GetOrdersByCustomerQuery { CustomerId = 1, Page = 200_000, PageSize = 100 })
            .ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(GetOrdersByCustomerQuery.Page));
    }

    [Fact]
    public void GetAllProductsQueryValidator_WithExcessivePage_ShouldReturnPageError()
    {
        var errors = new GetAllProductsQueryValidator()
            .Validate(new GetAllProductsQuery { Page = 200_000, PageSize = 100 })
            .ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(GetAllProductsQuery.Page));
    }

    [Fact]
    public void GetCustomersQueryValidator_WithExcessivePage_ShouldReturnPageError()
    {
        var errors = new GetCustomersQueryValidator()
            .Validate(new GetCustomersQuery { Page = 200_000, PageSize = 100 })
            .ToList();

        Assert.Contains(errors, error => error.PropertyName == nameof(GetCustomersQuery.Page));
    }

    [Fact]
    public void GetOrdersByStatusQueryValidator_WithinPageBound_ShouldNotReturnPageError()
    {
        var errors = new GetOrdersByStatusQueryValidator()
            .Validate(new GetOrdersByStatusQuery { Status = OrderStatus.Pending, Page = 100_000, PageSize = 100 })
            .ToList();

        Assert.DoesNotContain(errors, error => error.PropertyName == nameof(GetOrdersByStatusQuery.Page));
    }
}
