using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using StarterApp.Api.Application.Queries;
using StarterApp.Api.Infrastructure.Assistant;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class AssistantApiTests : IAsyncLifetime
{
    // AssistantRecordings/README.md says what these are and what they are not.
    private static readonly string FindsResponse = Recording("finds");
    private static readonly string ReadOrdersResponse = Recording("read-orders");
    private static readonly string ProposalResponse = Recording("proposal");
    private static readonly string ThinkingThenFindResponse = Recording("thinking-then-find");
    private static readonly string QuestionResponse = Recording("question");
    private static readonly string RequestResponse = Recording("request");

    private const string TurnsPath = "/api/v1/assistant/turns";
    private const string WithoutAssistantScope = "customers:read customers:write orders:read orders:write products:read products:write";

    private readonly ApiTestFixture _fixture;
    private readonly List<WebApplicationFactory<IApiMarker>> _factories = [];

    public AssistantApiTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
            await factory.DisposeAsync();
    }

    [Fact]
    public async Task NextTurn_WithNoModelConfigured_Returns503TurnedOff_AndTheStatusSaysOff()
    {
        var response = await _fixture.Client.PostAsJsonAsync(TurnsPath, Ask("Cancel Ada's last order"));
        var status = await _fixture.Client.GetFromJsonAsync<AssistantStatusDto>("/api/v1/assistant");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("The assistant is turned off", problem.GetProperty("title").GetString());
        Assert.Contains("Nothing was changed", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal((false, false), (status!.Enabled, status.Primary));
    }

    [Fact]
    public async Task NextTurn_WithAKeyButNotEnabled_Returns503_AndNeverCallsTheModel()
    {
        var model = new RecordedModel(FindsResponse);
        using var client = ClientWith(model, ("Assistant:Enabled", "false"));

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Cancel Ada's last order"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task NextTurn_SendsTheConfiguredModelThePromptTheToolsAndToday_AndReturnsTheFindItAsksFor()
    {
        var model = new RecordedModel(FindsResponse);
        using var client = ClientWith(model);

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Cancel Ada's last order"));

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var turn = (await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!;
        Assert.Equal("tool_use", turn.StopReason);
        var find = Assert.Single(turn.Blocks);
        Assert.Equal((AssistantPrompt.FindCustomers, "Ada"), (find.Name, find.Input!["search"].GetString()));

        var sent = Assert.Single(model.Requests);
        Assert.Equal("test-model-key", sent.ApiKey);
        var body = sent.Body.RootElement;
        Assert.Equal("test-model-id", body.GetProperty("model").GetString());
        Assert.Equal("low", body.GetProperty("output_config").GetProperty("effort").GetString());
        var system = body.GetProperty("system").EnumerateArray().Select(block => block.GetProperty("text").GetString()!).ToList();
        Assert.Equal(AssistantPrompt.Instructions, system[0]);
        Assert.EndsWith(AssistantPrompt.Guiding, system[0], StringComparison.Ordinal);
        Assert.Equal(AssistantPrompt.Notes, system[1]);
        Assert.Contains("POST /api/v1/orders/{id}/cancel : Cancel an order [orders:write]", system[2], StringComparison.Ordinal);
        Assert.Equal("ephemeral", body.GetProperty("system")[2].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.StartsWith("Today is ", system[3], StringComparison.Ordinal);
        Assert.Equal(
            [AssistantPrompt.FindCustomers, AssistantPrompt.ProposeCancelOrder, AssistantPrompt.Read, AssistantPrompt.Propose, AssistantPrompt.ShowTable, AssistantPrompt.ShowGraphic, AssistantPrompt.AskPerson],
            body.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!).ToList());
        var message = Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
    }

    [Fact]
    public async Task NextTurn_ForARead_NamesTheListedRouteTheBrowserIsToCall()
    {
        using var client = ClientWith(new RecordedModel(ReadOrdersResponse));

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Cancel Ada's last order"));

        var read = Assert.Single((await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!.Blocks);
        Assert.Equal(AssistantPrompt.Read, read.Name);
        Assert.Equal(("GET", "/api/v1/orders/customer/{customerId}", "orders:read"), (read.Route!.Method, read.Route.Path, read.Route.Scope));
    }

    [Fact]
    public async Task NextTurn_ReplaysTheAssistantsBlocksAndTheToolResults_AndReturnsTheProposal()
    {
        using var firstClient = ClientWith(new RecordedModel(ThinkingThenFindResponse));
        var query = Ask("Cancel Ada's last order");
        var earlier = (await (await firstClient.PostAsJsonAsync(TurnsPath, query)).Content.ReadFromJsonAsync<AssistantTurnDto>())!;
        var thinking = earlier.Blocks[0];
        var find = earlier.Blocks[1];
        Assert.Equal(("thinking", "tool_use"), (thinking.Type, find.Type));
        Assert.False(string.IsNullOrEmpty(thinking.Signature));

        var model = new RecordedModel(ProposalResponse);
        using var client = ClientWith(model);
        query.Messages.Add(new AssistantMessageDto { Role = "assistant", Blocks = earlier.Blocks });
        query.Messages.Add(new AssistantMessageDto
        {
            Role = "tool",
            Results = [new() { ToolUseId = find.Id!, Content = """[{"id":7,"name":"Ada Lovelace"}]""" }],
        });

        var response = await client.PostAsJsonAsync(TurnsPath, query);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var proposal = Assert.Single((await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!.Blocks);
        Assert.Equal(AssistantPrompt.ProposeCancelOrder, proposal.Name);
        Assert.Equal("0193a000-0000-7000-8000-0000000000a1", proposal.Input!["orderId"].GetString());

        var messages = Assert.Single(model.Requests).Body.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => m.GetProperty("role").GetString()!).ToList());
        var replayed = messages[1].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal(("thinking", thinking.Signature), (replayed[0].GetProperty("type").GetString(), replayed[0].GetProperty("signature").GetString()));
        Assert.Equal(("tool_use", find.Id), (replayed[1].GetProperty("type").GetString(), replayed[1].GetProperty("id").GetString()));
        var result = Assert.Single(messages[2].GetProperty("content").EnumerateArray());
        Assert.Equal(("tool_result", find.Id), (result.GetProperty("type").GetString(), result.GetProperty("tool_use_id").GetString()));
    }

    [Fact]
    public async Task NextTurn_WhenTheModelAsksWhichOfTwoCustomers_ReturnsItsQuestionAndNoAction()
    {
        using var client = ClientWith(new RecordedModel(QuestionResponse));

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Cancel Ada's last order"));

        var turn = (await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!;
        Assert.Equal("end_turn", turn.StopReason);
        var question = Assert.Single(turn.Blocks);
        Assert.Equal("text", question.Type);
        Assert.EndsWith("?", question.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextTurn_WhenTheModelProposesAGeneralRequest_NamesTheRouteItsSummaryItsScopeAndItsFields()
    {
        using var client = ClientWith(new RecordedModel(RequestResponse));

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Add Grace Hopper as a customer"));

        var proposal = Assert.Single((await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!.Blocks);
        Assert.Equal(AssistantPrompt.Propose, proposal.Name);
        Assert.Equal(("POST", "/api/v1/customers", "customers:write"), (proposal.Route!.Method, proposal.Route.Path, proposal.Route.Scope));
        Assert.Equal("Create a new customer", proposal.Route.Summary);
        Assert.Equal(["name", "email"], proposal.Route.Fields.Select(field => field.Name));
    }

    [Fact]
    public async Task NextTurn_WhenTheModelNamesARouteTheApiDoesNotList_LeavesTheBlockWithoutARoute()
    {
        using var client = ClientWith(new RecordedModel(RequestResponse.Replace("/api/v1/customers", TurnsPath, StringComparison.Ordinal)));

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Add Grace Hopper as a customer"));

        var proposal = Assert.Single((await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!.Blocks);
        Assert.Null(proposal.Route);
    }

    [Fact]
    public async Task NextTurn_ForARouteWhoseScopeTheTokenLacks_LeavesTheBlockWithoutARoute()
    {
        using var client = ClientWith(new RecordedModel(RequestResponse));
        using var request = new HttpRequestMessage(HttpMethod.Post, TurnsPath) { Content = JsonContent.Create(Ask("Add Grace Hopper as a customer")) };
        TestJwtIdentity.SetBearer(request, scopes: "customers:read assistant:use");

        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Null(Assert.Single((await response.Content.ReadFromJsonAsync<AssistantTurnDto>())!.Blocks).Route);
    }

    [Fact]
    public async Task NextTurn_WhenTheModelFails_Returns502AndSaysNothingChanged()
    {
        using var client = ClientWith(new RecordedModel("""{"type":"error","error":{"type":"invalid_request_error","message":"bad request"}}""", HttpStatusCode.BadRequest));

        var response = await client.PostAsJsonAsync(TurnsPath, Ask("Cancel Ada's last order"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Nothing was changed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextTurn_WithAnEmptyMisorderedOrOverlongConversation_OrAnUnknownArea_Returns400WithoutCallingTheModel()
    {
        var model = new RecordedModel(FindsResponse);
        using var client = ClientWith(model);
        var endsWithTheAssistant = Ask("Cancel Ada's order");
        endsWithTheAssistant.Messages.Add(new AssistantMessageDto { Role = "assistant", Blocks = [new() { Type = "text", Text = "Which Ada?" }] });
        var unknownArea = Ask("Cancel Ada's order");
        unknownArea.Area = "board";

        var responses = new[]
        {
            await client.PostAsJsonAsync(TurnsPath, new NextAssistantTurnQuery()),
            await client.PostAsJsonAsync(TurnsPath, Ask("  ")),
            await client.PostAsJsonAsync(TurnsPath, endsWithTheAssistant),
            await client.PostAsJsonAsync(TurnsPath, Ask(new string('a', NextAssistantTurnQuery.MaxTextLength + 1))),
            await client.PostAsJsonAsync(TurnsPath, unknownArea),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task EveryAssistantRoute_ForATokenWithoutTheAssistantScope_Returns403_AndNeverCallsTheModel()
    {
        var model = new RecordedModel(FindsResponse);
        using var client = ClientWith(model);

        var statuses = new List<HttpStatusCode>();
        foreach (var (method, path) in new[] { (HttpMethod.Get, "/api/v1/assistant"), (HttpMethod.Get, "/api/v1/assistant/routes"), (HttpMethod.Post, TurnsPath) })
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
                request.Content = JsonContent.Create(Ask("Cancel Ada's last order"));
            TestJwtIdentity.SetBearer(request, scopes: WithoutAssistantScope);
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden], statuses);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Routes_GiveEachChangeTheTokenMayMakeItsFieldsInOrderWithTheirChoices_AndNeedNoModel()
    {
        var model = new RecordedModel(QuestionResponse);
        using var client = ClientWith(model);
        using var readOnly = new HttpRequestMessage(HttpMethod.Get, "/api/v1/assistant/routes");
        TestJwtIdentity.SetBearer(readOnly, scopes: "customers:read orders:read assistant:use");

        var all = await client.GetFromJsonAsync<AssistantRoutesDto>("/api/v1/assistant/routes");
        var none = await (await client.SendAsync(readOnly)).Content.ReadFromJsonAsync<AssistantRoutesDto>();
        var unknown = await client.GetAsync("/api/v1/assistant/routes?area=board");

        var add = Assert.Single(all!.Routes, route => route is { Method: "POST", Path: "/api/v1/customers" });
        Assert.Equal(["name", "email"], add.Fields.Select(field => field.Name));
        Assert.Equal(["text", "text"], add.Fields.Select(field => field.Kind));
        var status = Assert.Single(all.Routes, route => route is { Method: "PUT", Path: "/api/v1/orders/{id}/status" });
        var choice = Assert.Single(status.Fields, field => field.Name == "status");
        Assert.Equal("choice", choice.Kind);
        Assert.Equal(Enum.GetNames<OrderStatus>(), choice.Options);
        Assert.Contains(all.Routes, route => route is { Method: "POST", Path: "/api/v1/orders/{id}/cancel", Scope: "orders:write" });
        Assert.DoesNotContain(all.Routes, route => route.Method == "GET" || route.Path.StartsWith("/api/v1/assistant", StringComparison.Ordinal));
        Assert.Empty(none!.Routes);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task Status_SaysOnOnlyWhereEnabledWithAKeyAndAModel_AndPrimaryOnlyWhereTheDeploymentChoseIt()
    {
        using var button = ClientWith(new RecordedModel(QuestionResponse));
        using var primary = ClientWith(new RecordedModel(QuestionResponse), ("Assistant:PrimaryInterface", "true"));
        using var noModel = ClientWith(new RecordedModel(QuestionResponse), ("Assistant:Model", ""));

        var asButton = await button.GetFromJsonAsync<AssistantStatusDto>("/api/v1/assistant");
        var asPrimary = await primary.GetFromJsonAsync<AssistantStatusDto>("/api/v1/assistant");
        var withoutModel = await noModel.GetFromJsonAsync<AssistantStatusDto>("/api/v1/assistant");

        Assert.Equal((true, false), (asButton!.Enabled, asButton.Primary));
        Assert.Equal((true, true), (asPrimary!.Enabled, asPrimary.Primary));
        Assert.Equal((false, false), (withoutModel!.Enabled, withoutModel.Primary));
    }

    [Fact]
    public async Task TheCatalogue_ListsReadsWithTheirQueryParameters_AndWritesWithTheirScope_ButNotItsOwnRoutes()
    {
        var catalogue = _fixture.Services.GetRequiredService<AssistantCatalogue>();
        var everything = new HashSet<string>(TestJwtIdentity.DefaultScopes.Split(' '), StringComparer.Ordinal);

        var routes = await catalogue.RoutesForAsync(everything, CancellationToken.None);
        var text = await catalogue.TextAsync(everything, CancellationToken.None);

        Assert.Contains(routes, route => route is { Method: "GET", Path: "/api/v1/orders/customer/{customerId}", Scope: "orders:read" });
        Assert.Contains("GET /api/v1/customers : Get all customers", text, StringComparison.Ordinal);
        Assert.Contains("POST /api/v1/customers { name, email } : Create a new customer [customers:write]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(routes, route => route.Path.StartsWith("/api/v1/assistant", StringComparison.Ordinal));
        Assert.Null(await catalogue.FindAsync(new HashSet<string>(["customers:read"], StringComparer.Ordinal), "POST", "/api/v1/customers", CancellationToken.None));
    }

    private static NextAssistantTurnQuery Ask(string text) => new() { Messages = [new() { Role = "user", Text = text }] };

    private static string Recording(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "StarterApp.Tests", "Integration", "AssistantRecordings", $"{name}.json"));

    private HttpClient ClientWith(RecordedModel model, params (string Key, string? Value)[] settings)
    {
        var factory = _fixture.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["Assistant:Enabled"] = "true",
                    ["Assistant:ApiKey"] = "test-model-key",
                    ["Assistant:Model"] = "test-model-id",
                };
                foreach (var (key, value) in settings)
                    values[key] = value;
                configuration.AddInMemoryCollection(values);
            });
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(ClaudeAssistantModel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => model));
        });
        _factories.Add(factory);
        return factory.CreateDefaultClient(new JwtSigningHandler());
    }

    private sealed record ModelRequest(string? ApiKey, JsonDocument Body);

    private sealed class RecordedModel : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _status;

        public RecordedModel(string response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        public List<ModelRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues("x-api-key", out var keys) ? keys.FirstOrDefault() : request.Headers.Authorization?.Parameter;
            Requests.Add(new ModelRequest(key, JsonDocument.Parse(body)));
            return new HttpResponseMessage(_status) { Content = new StringContent(_response, Encoding.UTF8, "application/json") };
        }
    }
}
