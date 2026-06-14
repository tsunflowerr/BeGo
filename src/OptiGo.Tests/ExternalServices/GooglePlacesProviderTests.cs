using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptiGo.Infrastructure.ExternalServices.Google;

namespace OptiGo.Tests.ExternalServices;

public class GooglePlacesProviderTests
{
    [Fact]
    public async Task SearchTextAsyncUsesNaturalQueryWithinCircle()
    {
        var handler = new CapturingHandler("""
            {
              "places": [
                {
                  "id": "frog-1",
                  "displayName": { "text": "Quán Ếch Đồng Xanh" },
                  "primaryType": "vietnamese_restaurant",
                  "location": { "latitude": 21.01, "longitude": 105.80 },
                  "rating": 4.4,
                  "userRatingCount": 128,
                  "formattedAddress": "Hà Nội"
                },
                {
                  "id": "frog-2",
                  "displayName": { "text": "Ếch Ngon" },
                  "primaryType": "restaurant",
                  "location": { "latitude": 21.02, "longitude": 105.81 },
                  "rating": 4.2,
                  "userRatingCount": 90,
                  "formattedAddress": "Hà Nội"
                },
                {
                  "id": "frog-3",
                  "displayName": { "text": "Lẩu Ếch" },
                  "primaryType": "restaurant",
                  "location": { "latitude": 21.03, "longitude": 105.82 },
                  "rating": 4.1,
                  "userRatingCount": 72,
                  "formattedAddress": "Hà Nội"
                }
              ]
            }
            """);
        var provider = new GooglePlacesProvider(
            new HttpClient(handler),
            Options.Create(new GoogleOptions { ApiKey = "test-key" }),
            NullLogger<GooglePlacesProvider>.Instance);

        var venues = await provider.SearchTextAsync(21.0, 105.8, "quán ếch", radiusMeters: 5000, limit: 3);

        Assert.Equal(3, venues.Count);
        Assert.Equal("frog-1", venues[0].Id);
        Assert.Single(handler.RequestUris);
        Assert.Equal("https://places.googleapis.com/v1/places:searchText", handler.RequestUris[0].ToString());

        var body = JsonNode.Parse(handler.RequestBodies[0])!;
        Assert.Equal("quán ếch", body["textQuery"]!.GetValue<string>());
        Assert.Equal("DISTANCE", body["rankPreference"]!.GetValue<string>());
        Assert.Null(body["includedTypes"]);
        Assert.Equal(5000d, body["locationBias"]!["circle"]!["radius"]!.GetValue<double>());
        Assert.Equal(21.0, body["locationBias"]!["circle"]!["center"]!["latitude"]!.GetValue<double>());
        Assert.Equal(105.8, body["locationBias"]!["circle"]!["center"]!["longitude"]!.GetValue<double>());
    }

    [Fact]
    public async Task SearchNearbyAsyncExpandsCafeToRelatedCoffeeTypes()
    {
        var handler = new CapturingHandler("""
            {
              "places": [
                {
                  "id": "cafe-1",
                  "displayName": { "text": "Cafe Một" },
                  "primaryType": "coffee_shop",
                  "location": { "latitude": 21.0, "longitude": 105.8 },
                  "rating": 4.5,
                  "userRatingCount": 100,
                  "formattedAddress": "Hà Nội"
                },
                {
                  "id": "cafe-2",
                  "displayName": { "text": "Cafe Hai" },
                  "primaryType": "cafe",
                  "location": { "latitude": 21.001, "longitude": 105.801 },
                  "rating": 4.3,
                  "userRatingCount": 80,
                  "formattedAddress": "Hà Nội"
                }
              ]
            }
            """);
        var provider = new GooglePlacesProvider(
            new HttpClient(handler),
            Options.Create(new GoogleOptions { ApiKey = "test-key" }),
            NullLogger<GooglePlacesProvider>.Instance);

        var venues = await provider.SearchNearbyAsync(21.0, 105.8, "cafe", radiusMeters: 500, limit: 2);

        Assert.Equal(2, venues.Count);
        Assert.Single(handler.RequestUris);
        Assert.Equal("https://places.googleapis.com/v1/places:searchNearby", handler.RequestUris[0].ToString());

        var body = JsonNode.Parse(handler.RequestBodies[0])!;
        var includedTypes = body["includedTypes"]!.AsArray().Select(t => t!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "cafe", "coffee_shop", "coffee_stand", "coffee_roastery" }, includedTypes);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public List<Uri> RequestUris { get; } = new();
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
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
