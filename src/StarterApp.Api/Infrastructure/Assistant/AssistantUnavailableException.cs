namespace StarterApp.Api.Infrastructure.Assistant;

public sealed class AssistantUnavailableException : Exception
{
    public AssistantUnavailableException(int statusCode, string title, string detail, Exception? innerException = null)
        : base(detail, innerException)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }

    public string Title { get; }

    public static AssistantUnavailableException TurnedOff() =>
        new(StatusCodes.Status503ServiceUnavailable, "The assistant is turned off", "No model is configured for the assistant in this environment. Nothing was changed.");

    public static AssistantUnavailableException NoAnswer(Exception innerException) =>
        new(StatusCodes.Status502BadGateway, "The assistant did not answer", "The assistant's model could not be reached or refused the request. Nothing was changed; try again, or do it by hand.", innerException);
}
