using System.Net;
using System.Text;

namespace PublicData.Tests.Support;

/// <summary>Answers HTTP requests from a script and records every request URI.</summary>
public sealed class FakeHttpHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _calls;

    public List<Uri> Requests { get; } = [];

    public int Calls => _calls;

    public static FakeHttpHandler Json(string json) =>
        new((_, _, _) => Task.FromResult(JsonResponse(json)));

    public static FakeHttpHandler Fixture(string fileName) => Json(Fixtures.Read(fileName));

    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        var attempt = Interlocked.Increment(ref _calls);
        return respond(request, attempt, cancellationToken);
    }
}

public static class Fixtures
{
    public static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName), Encoding.UTF8);
}
