using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptiGo.Infrastructure.ExternalServices.Groq;

namespace OptiGo.Tests.ExternalServices;

public class GroqAIServiceTests
{
    [Fact]
    public async Task ResolveCategoryAsyncSendsAllowedCategoriesAndSanitizesModelOutput()
    {
        var handler = new CapturingHandler("""
            {
              "choices": [
                {
                  "message": {
                    "content": "\"coffee_shop\""
                  }
                }
              ]
            }
            """);
        var service = new GroqAIService(
            new HttpClient(handler),
            Options.Create(new GroqOptions { ApiKey = "test-key" }),
            NullLogger<GroqAIService>.Instance);

        var category = await service.ResolveCategoryAsync("quán cà phê yên tĩnh");

        Assert.Equal("coffee_shop", category);
        var body = JsonNode.Parse(handler.RequestBodies[0])!;
        var prompt = body["messages"]![0]!["content"]!.GetValue<string>();
        Assert.Contains("coffee_shop", prompt);
        Assert.DoesNotContain("System.String[]", prompt);
    }

    [Fact]
    public async Task ResolveCategoryAsyncDefaultsToCafeWhenGroqReturnsUnknownCategory()
    {
        var handler = new CapturingHandler("""
            {
              "choices": [
                {
                  "message": {
                    "content": "made_up_place"
                  }
                }
              ]
            }
            """);
        var service = new GroqAIService(
            new HttpClient(handler),
            Options.Create(new GroqOptions { ApiKey = "test-key" }),
            NullLogger<GroqAIService>.Instance);

        var category = await service.ResolveCategoryAsync("địa điểm lạ");

        Assert.Equal("cafe", category);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
