namespace StarterApp.Tests.Application.Commands;

[Collection("Integration Tests")]
public class CreateCustomerCommandHandlerTests : PostgresCommandHandlerTestBase
{
    public CreateCustomerCommandHandlerTests(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldCreateCustomerAndReturnDto()
    {
        // Arrange
        await using var context = CreateContext();
        var handler = new CreateCustomerCommandHandler(context, TestOwnerOnlyPolicy.Instance);

        var command = new CreateCustomerCommand
        {
            Name = "John Doe",
            Email = "john@example.com"
        };

        // Act
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(command.Name, result.Name);
        Assert.Equal(command.Email, result.Email);
        Assert.True(result.Id > 0);

        // Verify the customer was actually saved to the database
        var savedCustomer = await context.Customers.FirstOrDefaultAsync(c => c.Name == command.Name);
        Assert.NotNull(savedCustomer);
        Assert.Equal(command.Name, savedCustomer.Name);
        Assert.Equal(command.Email, savedCustomer.Email.Value);
    }

    [Fact]
    public async Task Handle_WhenConcurrentCreatesRaceOnTheSameEmail_OnlyOneSucceedsAndEachResultIsItsOwn()
    {
        // The natural-key recovery lookup used to run on the first attempt too, so a request that
        // lost the race after its pre-check could return the winner's row (a different name) as a
        // successful create. Now the first attempt goes through the unique constraint (409) and
        // only a genuine retry may recover a row, and only when it matches the request.
        const string email = "race@example.com";
        var attempts = Enumerable.Range(0, 6).Select(i => $"Racer {i}").ToList();

        var results = await Task.WhenAll(attempts.Select(async name =>
        {
            await using var context = CreateContext();
            var handler = new CreateCustomerCommandHandler(context, TestOwnerOnlyPolicy.Instance);
            try
            {
                var dto = await handler.HandleAsync(new CreateCustomerCommand { Name = name, Email = email }, CancellationToken.None);
                return (Name: name, Dto: (CustomerDto?)dto, Conflict: false);
            }
            catch (DomainRuleException)
            {
                return (Name: name, Dto: null, Conflict: true);
            }
        }));

        var winners = results.Where(r => r.Dto is not null).ToList();
        Assert.Single(winners);
        Assert.Equal(winners[0].Name, winners[0].Dto!.Name);
        Assert.Equal(attempts.Count - 1, results.Count(r => r.Conflict));

        await using var verifyContext = CreateContext();
        Assert.Equal(1, await verifyContext.Customers.CountAsync(c => c.Email.Value == email));
    }
}
