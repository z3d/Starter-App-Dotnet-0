using StarterApp.Api.Infrastructure.ClientEvents;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class ClientEventIntakeTests
{
    private readonly ApiTestFixture _fixture;

    public ClientEventIntakeTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ABatch_IsAcceptedWithoutSigningIn()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(ClientEventIntake.Path, new
        {
            events = new object[]
            {
                new { kind = "error", name = "TypeError", message = "x is undefined", route = "/orders", build = "abc123", elapsedMs = 5120 },
                new { kind = "event", name = "settings-opened", route = "/settings", build = "abc123", elapsedMs = 9000 }
            }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"events":[]}""")]
    [InlineData("""{"events":[{"kind":"crash","name":"x"}]}""")]
    [InlineData("""{"events":[{"kind":"error"}]}""")]
    [InlineData("""{"events":[null]}""")]
    [InlineData("""not json""")]
    public async Task OnlyAMalformedBatch_IsRefused(string body)
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsync(ClientEventIntake.Path, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ABatchOverTheLimit_IsRefused()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var events = Enumerable.Range(0, ClientEventIntake.MaxEventsPerBatch + 1).Select(i => new { kind = "event", name = $"e{i}" }).ToArray();

        var response = await client.PostAsJsonAsync(ClientEventIntake.Path, new { events });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("could not save ada.lovelace@example.com", "could not save [email]")]
    [InlineData("card 4111 1111 1111 1111 declined", "card ***SECRET*** declined")]
    [InlineData("line one\nline two", "line one line two")]
    public void Clean_BlanksWhatLooksPersonal(string text, string expected)
    {
        Assert.Equal(expected, ClientEventIntake.Clean(text, 1000));
    }
}
