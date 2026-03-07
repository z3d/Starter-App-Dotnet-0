namespace StarterApp.Api.Application;

public class PagedResponse<T>
{
    public List<T> Data { get; set; } = [];
    public bool HasMore { get; set; }
}
