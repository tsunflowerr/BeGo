using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;

namespace OptiGo.Infrastructure.ExternalServices.Gemini;

/// <summary>
/// Triển khai IAIService dùng Google Gemini API (Flash model - nhanh + rẻ).
/// Gọi qua HTTP thuần, không cần SDK nặng.
/// </summary>
public class GeminiAIService : IAIService
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiAIService> _logger;

    // Danh sách category hợp lệ của Google Places API — được nhóm theo ngữ nghĩa
    // để Gemini hiểu context tốt hơn khi phân tích intent của người dùng.
    private static readonly string[] ValidCategories =
    [
        // ── Nhóm 1: Cà phê & Tráng miệng ──
        "cafe", "coffee_shop", "tea_house", "bakery",
        "dessert_shop", "ice_cream_shop", "juice_shop",

        // ── Nhóm 2: Nhà hàng chung & Đồ ăn nhanh ──
        "restaurant", "fast_food_restaurant", "diner",
        "food_court", "pizza_restaurant", "sandwich_shop",

        // ── Nhóm 3: Nhà hàng đặc thù ──
        "hot_pot_restaurant", "seafood_restaurant", "steak_house",
        "vegetarian_restaurant",

        // ── Nhóm 4: Nhà hàng theo quốc gia ──
        "vietnamese_restaurant", "japanese_restaurant",
        "korean_barbecue_restaurant", "chinese_restaurant", "italian_restaurant",

        // ── Nhóm 5: Có cồn & Tụ tập buổi tối ──
        "bar", "pub", "gastropub", "beer_garden", "wine_bar",

        // ── Nhóm 6: Vui chơi trong nhà & Giải trí ──
        "movie_theater", "karaoke", "amusement_center",
        "bowling_alley", "video_arcade", "night_club",

        // ── Nhóm 7: Vui chơi ngoài trời & Thiên nhiên ──
        "park", "city_park", "amusement_park", "tourist_attraction",
        "picnic_ground", "zoo", "botanical_garden",

        // ── Nhóm 8: Sự kiện & Biểu diễn ──
        "event_venue", "live_music_venue", "comedy_club", "performing_arts_theater",

        // ── Nhóm 9: Mua sắm ──
        "shopping_mall", "department_store", "clothing_store",
        "book_store", "supermarket", "convenience_store",

        // ── Nhóm 10: Văn hoá & Lịch sử ──
        "museum", "art_gallery", "historical_landmark", "cultural_center",

        // ── Nhóm 11: Giao thông & Trạm trung chuyển (Phase 2 Hub-and-Spoke) ──
        "bus_station", "transit_station", "subway_station", "light_rail_station",

        // ── Nhóm 12: Đỗ xe & Không gian công cộng ──
        "parking", "parking_lot", "parking_garage", "town_square",
    ];

    public GeminiAIService(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiAIService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ResolveCategoryAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "cafe"; // fallback mặc định

        var prompt = $"""
            Bạn là hệ thống phân loại địa điểm cho ứng dụng tìm điểm gặp nhau.
            Nhiệm vụ: Chuyển yêu cầu của người dùng thành MỘT Google Places category type.

            Yêu cầu: "{query}"

            Danh sách category hợp lệ (phân nhóm để dễ chọn):
            - Cà phê/Tráng miệng: cafe, coffee_shop, tea_house, bakery, dessert_shop, ice_cream_shop, juice_shop
            - Nhà hàng/Ăn uống: restaurant, fast_food_restaurant, diner, food_court, pizza_restaurant, sandwich_shop
            - Nhà hàng đặc thù: hot_pot_restaurant, seafood_restaurant, steak_house, vegetarian_restaurant
            - Nhà hàng theo quốc gia: vietnamese_restaurant, japanese_restaurant, korean_barbecue_restaurant, chinese_restaurant, italian_restaurant
            - Có cồn/Tụ tập tối: bar, pub, gastropub, beer_garden, wine_bar
            - Giải trí trong nhà: movie_theater, karaoke, amusement_center, bowling_alley, video_arcade, night_club
            - Ngoài trời/Thiên nhiên: park, city_park, amusement_park, tourist_attraction, picnic_ground, zoo, botanical_garden
            - Sự kiện/Biểu diễn: event_venue, live_music_venue, comedy_club, performing_arts_theater
            - Mua sắm: shopping_mall, department_store, clothing_store, book_store, supermarket, convenience_store
            - Văn hoá/Lịch sử: museum, art_gallery, historical_landmark, cultural_center
            - Giao thông: bus_station, transit_station, subway_station, light_rail_station
            - Đỗ xe/Tập kết: parking, parking_lot, parking_garage, town_square

            Quy tắc BẮT BUỘC:
            - Trả về DUY NHẤT một giá trị từ danh sách trên (viết thường, có dấu gạch dưới).
            - KHÔNG giải thích, KHÔNG dấu câu, KHÔNG markdown.
            - Nếu không xác định được → trả về: cafe

            Trả lời:
            """;

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent?key={_options.ApiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 20
                }
            };

            var response = await _http.PostAsJsonAsync(url, requestBody, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Gemini API returned error: {StatusCode} - {Message}. Falling back to 'cafe'.", response.StatusCode, errorMsg);
                return "cafe";
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            
            // Parsing an toàn để tránh NullReferenceException hoặc KeyNotFoundException
            if (json.TryGetProperty("candidates", out var candidates) && 
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var text))
            {
                var rawText = text.GetString()?.Trim().ToLowerInvariant() ?? "cafe";

                // Validate: nếu kết quả không nằm trong danh sách hợp lệ → fallback
                if (ValidCategories.Contains(rawText))
                {
                    _logger.LogInformation("Gemini resolved '{Query}' → '{Category}'", query, rawText);
                    return rawText;
                }
                
                _logger.LogWarning("Gemini returned unknown category '{Raw}' for query '{Query}'. Falling back to 'cafe'.", rawText, query);
            }
            
            return "cafe";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini API failed for query '{Query}'. Falling back to 'cafe'.", query);
            return "cafe"; // Luôn có fallback, không crash hệ thống
        }
    }
}

/// <summary>Cấu hình để inject từ appsettings / .env</summary>
public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
