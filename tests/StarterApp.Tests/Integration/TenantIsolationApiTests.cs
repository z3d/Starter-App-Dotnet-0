using System.Globalization;

namespace StarterApp.Tests.Integration;

// Another tenant's row answers exactly as a row that does not exist, through every route, so a caller cannot tell which ids are taken.
[Collection("Integration Tests")]
public class TenantIsolationApiTests : IAsyncLifetime
{
    private const string OtherTenant = "other-tenant-02";
    private const int MissingId = 2_000_000_000;

    private readonly ApiTestFixture _fixture;

    public TenantIsolationApiTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string> Callers => new() { TestJwtIdentity.DefaultSubject, "other-user-02" };

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task AnotherTenantsCustomer_AnswersAsAMissingOne_AndIsNotChanged(string subject)
    {
        var customer = await CreateAsync<CustomerDto>("/api/v1/customers", new CreateCustomerCommand { Name = "Tenant A", Email = "a@example.test" });

        await AssertAnswersAsMissingAsync(HttpMethod.Get, "/api/v1/customers/{0}", customer.Id, MissingId, null, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Put, "/api/v1/customers/{0}", customer.Id, MissingId,
            id => new UpdateCustomerCommand { Id = (int)id, Name = "Taken", Email = "taken@example.test" }, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Delete, "/api/v1/customers/{0}", customer.Id, MissingId, null, subject);

        var list = await SendAsync(HttpMethod.Get, "/api/v1/customers", null, subject);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("a@example.test", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var unchanged = await _fixture.Client.GetFromJsonAsync<CustomerDto>($"/api/v1/customers/{customer.Id}");
        Assert.Equal("Tenant A", unchanged!.Name);
    }

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task AnotherTenantsProduct_AnswersAsAMissingOne_AndIsNotChanged(string subject)
    {
        var product = await CreateProductAsync();

        await AssertAnswersAsMissingAsync(HttpMethod.Get, "/api/v1/products/{0}", product.Id, MissingId, null, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Put, "/api/v1/products/{0}", product.Id, MissingId,
            id => new UpdateProductCommand { Id = (int)id, Name = "Taken", Description = "x", Price = 1m, Currency = "USD", Stock = 1 }, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Delete, "/api/v1/products/{0}", product.Id, MissingId, null, subject);

        var list = await SendAsync(HttpMethod.Get, "/api/v1/products", null, subject);
        Assert.DoesNotContain("Tenant A product", await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var unchanged = await _fixture.Client.GetFromJsonAsync<ProductDto>($"/api/v1/products/{product.Id}");
        Assert.Equal("Tenant A product", unchanged!.Name);
    }

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task AnotherTenantsOrder_AnswersAsAMissingOne_AndIsNotChanged(string subject)
    {
        var customer = await CreateAsync<CustomerDto>("/api/v1/customers", new CreateCustomerCommand { Name = "Tenant A", Email = "orders@example.test" });
        var product = await CreateProductAsync();
        var order = await CreateAsync<OrderDto>("/api/v1/orders", new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new CreateOrderItemCommand { ProductId = product.Id, Quantity = 1 }],
        });
        var missingOrder = Guid.CreateVersion7();

        await AssertAnswersAsMissingAsync(HttpMethod.Get, "/api/v1/orders/{0}", order.Id, missingOrder, null, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Put, "/api/v1/orders/{0}/status", order.Id, missingOrder,
            id => new UpdateOrderStatusCommand { OrderId = (Guid)id, Status = OrderStatus.Confirmed }, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Post, "/api/v1/orders/{0}/cancel", order.Id, missingOrder, null, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Get, "/api/v1/orders/customer/{0}", customer.Id, MissingId, null, subject);

        // Placing an order against another tenant's customer, or with its product, answers as for ones that do not exist.
        await AssertAnswersAsMissingAsync(HttpMethod.Post, "/api/v1/orders", customer.Id, MissingId,
            id => new CreateOrderCommand { CustomerId = (int)id, Items = [new CreateOrderItemCommand { ProductId = product.Id, Quantity = 1 }] }, subject);
        var ownCustomer = await CreateAsAsync<CustomerDto>("/api/v1/customers", new CreateCustomerCommand { Name = "Tenant B", Email = "b@example.test" }, subject);
        await AssertAnswersAsMissingAsync(HttpMethod.Post, "/api/v1/orders", product.Id, MissingId,
            id => new CreateOrderCommand { CustomerId = ownCustomer.Id, Items = [new CreateOrderItemCommand { ProductId = (int)id, Quantity = 1 }] }, subject);

        var byStatus = await SendAsync(HttpMethod.Get, "/api/v1/orders/status/Pending", null, subject);
        Assert.DoesNotContain(order.Id.ToString(), await byStatus.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var unchanged = await _fixture.Client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}");
        Assert.Equal("Pending", unchanged!.Status.ToString());
    }

    [Fact]
    public async Task AnotherTenantsAppearance_IsNeitherReadNorOverwritten()
    {
        (await _fixture.Client.PutAsJsonAsync("/api/v1/settings/appearance", new UpdateTenantSettingsCommand { Palette = "sky" })).EnsureSuccessStatusCode();

        var other = await SendAsync(HttpMethod.Get, "/api/v1/settings/appearance", null, TestJwtIdentity.DefaultSubject);
        Assert.DoesNotContain("sky", await other.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        (await SendAsync(HttpMethod.Put, "/api/v1/settings/appearance", new UpdateTenantSettingsCommand { Palette = "coral" }, TestJwtIdentity.DefaultSubject)).EnsureSuccessStatusCode();
        var own = await _fixture.Client.GetFromJsonAsync<TenantSettingsDto>("/api/v1/settings/appearance");
        Assert.Equal("sky", own!.Palette);
    }

    private async Task AssertAnswersAsMissingAsync(HttpMethod method, string route, object takenId, object missingId, Func<object, object>? body, string subject)
    {
        var taken = await SendAsync(method, string.Format(CultureInfo.InvariantCulture, route, takenId), body?.Invoke(takenId), subject);
        var missing = await SendAsync(method, string.Format(CultureInfo.InvariantCulture, route, missingId), body?.Invoke(missingId), subject);

        Assert.Equal(missing.StatusCode, taken.StatusCode);
        Assert.Equal(
            Normalise(await missing.Content.ReadAsStringAsync(), missingId),
            Normalise(await taken.Content.ReadAsStringAsync(), takenId));
    }

    // The id the caller sent and the per-request trace id are the only differences a missing row may show.
    private static string Normalise(string body, object id)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        using var document = JsonDocument.Parse(body);
        var fields = document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.EnumerateObject()
                .Where(property => property.Name is not ("traceId" or "correlationId" or "instance"))
                .Select(property => $"{property.Name}={property.Value.GetRawText()}")
            : [document.RootElement.GetRawText()];
        return Regex.Replace(string.Join("|", fields), $@"(?<![\w.-]){Regex.Escape(Convert.ToString(id, CultureInfo.InvariantCulture)!)}(?![\w.-])", "{id}", RegexOptions.IgnoreCase);
    }

    private async Task<ProductDto> CreateProductAsync() =>
        await CreateAsync<ProductDto>("/api/v1/products", new CreateProductCommand { Name = "Tenant A product", Description = "a", Price = 10m, Currency = "USD", Stock = 10 });

    private async Task<T> CreateAsync<T>(string route, object body)
    {
        var response = await _fixture.Client.PostAsJsonAsync(route, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<T> CreateAsAsync<T>(string route, object body, string subject)
    {
        var response = await SendAsync(HttpMethod.Post, route, body, subject);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, object? body, string subject)
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var request = new HttpRequestMessage(method, route);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());
        TestJwtIdentity.SetBearer(request, subject: subject, tenantId: OtherTenant);
        return await client.SendAsync(request);
    }
}
