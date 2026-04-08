using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;

namespace OptiGo.Infrastructure.ExternalServices.Groq;

/// <summary>
/// Triển khai IAIService dùng Groq Chat Completions API.
/// </summary>
public class GroqAIService : IAIService
{
    private readonly HttpClient _http;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqAIService> _logger;

    private static readonly string[] ValidCategories =
    [
        "car_dealer",
        "car_rental",
        "car_repair",
        "car_wash",
        "ebike_charging_station",
        "electric_vehicle_charging_station",
        "gas_station",
        "parking",
        "parking_garage",
        "parking_lot",
        "rest_stop",
        "tire_shop",
        "truck_dealer",
        "business_center",
        "corporate_office",
        "coworking_space",
        "farm",
        "manufacturer",
        "ranch",
        "supplier",
        "television_studio",
        "art_gallery",
        "art_museum",
        "art_studio",
        "auditorium",
        "castle",
        "cultural_landmark",
        "fountain",
        "historical_place",
        "history_museum",
        "monument",
        "museum",
        "performing_arts_theater",
        "sculpture",
        "academic_department",
        "educational_institution",
        "library",
        "preschool",
        "primary_school",
        "research_institute",
        "school",
        "secondary_school",
        "university",
        "adventure_sports_center",
        "amphitheatre",
        "amusement_center",
        "amusement_park",
        "aquarium",
        "banquet_hall",
        "barbecue_area",
        "botanical_garden",
        "bowling_alley",
        "casino",
        "childrens_camp",
        "city_park",
        "comedy_club",
        "community_center",
        "concert_hall",
        "convention_center",
        "cultural_center",
        "cycling_park",
        "dance_hall",
        "dog_park",
        "event_venue",
        "ferris_wheel",
        "garden",
        "go_karting_venue",
        "hiking_area",
        "historical_landmark",
        "indoor_playground",
        "internet_cafe",
        "karaoke",
        "live_music_venue",
        "marina",
        "miniature_golf_course",
        "movie_rental",
        "movie_theater",
        "national_park",
        "night_club",
        "observation_deck",
        "off_roading_area",
        "opera_house",
        "paintball_center",
        "park",
        "philharmonic_hall",
        "picnic_ground",
        "planetarium",
        "plaza",
        "roller_coaster",
        "skateboard_park",
        "state_park",
        "tourist_attraction",
        "video_arcade",
        "vineyard",
        "visitor_center",
        "water_park",
        "wedding_venue",
        "wildlife_park",
        "wildlife_refuge",
        "zoo",
        "public_bath",
        "public_bathroom",
        "stable",
        "accounting",
        "atm",
        "bank",
        "acai_shop",
        "afghani_restaurant",
        "african_restaurant",
        "american_restaurant",
        "argentinian_restaurant",
        "asian_fusion_restaurant",
        "asian_restaurant",
        "australian_restaurant",
        "austrian_restaurant",
        "bagel_shop",
        "bakery",
        "bangladeshi_restaurant",
        "bar",
        "bar_and_grill",
        "barbecue_restaurant",
        "basque_restaurant",
        "bavarian_restaurant",
        "beer_garden",
        "belgian_restaurant",
        "bistro",
        "brazilian_restaurant",
        "breakfast_restaurant",
        "brewery",
        "brewpub",
        "british_restaurant",
        "brunch_restaurant",
        "buffet_restaurant",
        "burmese_restaurant",
        "burrito_restaurant",
        "cafe",
        "cafeteria",
        "cajun_restaurant",
        "cake_shop",
        "californian_restaurant",
        "cambodian_restaurant",
        "candy_store",
        "cantonese_restaurant",
        "caribbean_restaurant",
        "cat_cafe",
        "chicken_restaurant",
        "chicken_wings_restaurant",
        "chilean_restaurant",
        "chinese_noodle_restaurant",
        "chinese_restaurant",
        "chocolate_factory",
        "chocolate_shop",
        "cocktail_bar",
        "coffee_roastery",
        "coffee_shop",
        "coffee_stand",
        "colombian_restaurant",
        "confectionery",
        "croatian_restaurant",
        "cuban_restaurant",
        "czech_restaurant",
        "danish_restaurant",
        "deli",
        "dessert_restaurant",
        "dessert_shop",
        "dim_sum_restaurant",
        "diner",
        "dog_cafe",
        "donut_shop",
        "dumpling_restaurant",
        "dutch_restaurant",
        "eastern_european_restaurant",
        "ethiopian_restaurant",
        "european_restaurant",
        "falafel_restaurant",
        "family_restaurant",
        "fast_food_restaurant",
        "filipino_restaurant",
        "fine_dining_restaurant",
        "fish_and_chips_restaurant",
        "fondue_restaurant",
        "food_court",
        "french_restaurant",
        "fusion_restaurant",
        "gastropub",
        "german_restaurant",
        "greek_restaurant",
        "gyro_restaurant",
        "halal_restaurant",
        "hamburger_restaurant",
        "hawaiian_restaurant",
        "hookah_bar",
        "hot_dog_restaurant",
        "hot_dog_stand",
        "hot_pot_restaurant",
        "hungarian_restaurant",
        "ice_cream_shop",
        "indian_restaurant",
        "indonesian_restaurant",
        "irish_pub",
        "irish_restaurant",
        "israeli_restaurant",
        "italian_restaurant",
        "japanese_curry_restaurant",
        "japanese_izakaya_restaurant",
        "japanese_restaurant",
        "juice_shop",
        "kebab_shop",
        "korean_barbecue_restaurant",
        "korean_restaurant",
        "latin_american_restaurant",
        "lebanese_restaurant",
        "lounge_bar",
        "malaysian_restaurant",
        "meal_delivery",
        "meal_takeaway",
        "mediterranean_restaurant",
        "mexican_restaurant",
        "middle_eastern_restaurant",
        "mongolian_barbecue_restaurant",
        "moroccan_restaurant",
        "noodle_shop",
        "north_indian_restaurant",
        "oyster_bar_restaurant",
        "pakistani_restaurant",
        "pastry_shop",
        "persian_restaurant",
        "peruvian_restaurant",
        "pizza_delivery",
        "pizza_restaurant",
        "polish_restaurant",
        "portuguese_restaurant",
        "pub",
        "ramen_restaurant",
        "restaurant",
        "romanian_restaurant",
        "russian_restaurant",
        "salad_shop",
        "sandwich_shop",
        "scandinavian_restaurant",
        "seafood_restaurant",
        "shawarma_restaurant",
        "snack_bar",
        "soul_food_restaurant",
        "soup_restaurant",
        "south_american_restaurant",
        "south_indian_restaurant",
        "southwestern_us_restaurant",
        "spanish_restaurant",
        "sports_bar",
        "sri_lankan_restaurant",
        "steak_house",
        "sushi_restaurant",
        "swiss_restaurant",
        "taco_restaurant",
        "taiwanese_restaurant",
        "tapas_restaurant",
        "tea_house",
        "tex_mex_restaurant",
        "thai_restaurant",
        "tibetan_restaurant",
        "tonkatsu_restaurant",
        "turkish_restaurant",
        "ukrainian_restaurant",
        "vegan_restaurant",
        "vegetarian_restaurant",
        "vietnamese_restaurant",
        "western_restaurant",
        "wine_bar",
        "winery",
        "yakiniku_restaurant",
        "yakitori_restaurant",
        "administrative_area_level_1",
        "administrative_area_level_2",
        "country",
        "locality",
        "postal_code",
        "school_district",
        "city_hall",
        "courthouse",
        "embassy",
        "fire_station",
        "government_office",
        "local_government_office",
        "neighborhood_police_station",
        "police",
        "post_office",
        "chiropractor",
        "dental_clinic",
        "dentist",
        "doctor",
        "drugstore",
        "general_hospital",
        "hospital",
        "massage",
        "massage_spa",
        "medical_center",
        "medical_clinic",
        "medical_lab",
        "pharmacy",
        "physiotherapist",
        "sauna",
        "skin_care_clinic",
        "spa",
        "tanning_studio",
        "wellness_center",
        "yoga_studio",
        "apartment_building",
        "apartment_complex",
        "condominium_complex",
        "housing_complex",
        "bed_and_breakfast",
        "budget_japanese_inn",
        "campground",
        "camping_cabin",
        "cottage",
        "extended_stay_hotel",
        "farmstay",
        "guest_house",
        "hostel",
        "hotel",
        "inn",
        "japanese_inn",
        "lodging",
        "mobile_home_park",
        "motel",
        "private_guest_room",
        "resort_hotel",
        "rv_park",
        "beach",
        "island",
        "lake",
        "mountain_peak",
        "nature_preserve",
        "river",
        "scenic_spot",
        "woods",
        "buddhist_temple",
        "church",
        "hindu_temple",
        "mosque",
        "shinto_shrine",
        "synagogue",
        "aircraft_rental_service",
        "association_or_organization",
        "astrologer",
        "barber_shop",
        "beautician",
        "beauty_salon",
        "body_art_service",
        "catering_service",
        "cemetery",
        "chauffeur_service",
        "child_care_agency",
        "consultant",
        "courier_service",
        "electrician",
        "employment_agency",
        "florist",
        "food_delivery",
        "foot_care",
        "funeral_home",
        "hair_care",
        "hair_salon",
        "insurance_agency",
        "laundry",
        "lawyer",
        "locksmith",
        "makeup_artist",
        "marketing_consultant",
        "moving_company",
        "nail_salon",
        "non_profit_organization",
        "painter",
        "pet_boarding_service",
        "pet_care",
        "plumber",
        "psychic",
        "real_estate_agency",
        "roofing_contractor",
        "service",
        "shipping_service",
        "storage",
        "summer_camp_organizer",
        "tailor",
        "telecommunications_service_provider",
        "tour_agency",
        "tourist_information_center",
        "travel_agency",
        "veterinary_care",
        "asian_grocery_store",
        "auto_parts_store",
        "bicycle_store",
        "book_store",
        "building_materials_store",
        "butcher_shop",
        "cell_phone_store",
        "clothing_store",
        "convenience_store",
        "cosmetics_store",
        "department_store",
        "discount_store",
        "discount_supermarket",
        "electronics_store",
        "farmers_market",
        "flea_market",
        "food_store",
        "furniture_store",
        "garden_center",
        "general_store",
        "gift_shop",
        "grocery_store",
        "hardware_store",
        "health_food_store",
        "home_goods_store",
        "home_improvement_store",
        "hypermarket",
        "jewelry_store",
        "liquor_store",
        "market",
        "pet_store",
        "shoe_store",
        "shopping_mall",
        "sporting_goods_store",
        "sportswear_store",
        "store",
        "supermarket",
        "tea_store",
        "thrift_store",
        "toy_store",
        "warehouse_store",
        "wholesaler",
        "womens_clothing_store",
        "arena",
        "athletic_field",
        "fishing_charter",
        "fishing_pier",
        "fishing_pond",
        "fitness_center",
        "golf_course",
        "gym",
        "ice_skating_rink",
        "indoor_golf_course",
        "playground",
        "race_course",
        "ski_resort",
        "sports_activity_location",
        "sports_club",
        "sports_coaching",
        "sports_complex",
        "sports_school",
        "stadium",
        "swimming_pool",
        "tennis_court",
        "airport",
        "airstrip",
        "bike_sharing_station",
        "bridge",
        "bus_station",
        "bus_stop",
        "ferry_service",
        "ferry_terminal",
        "heliport",
        "international_airport",
        "light_rail_station",
        "park_and_ride",
        "subway_station",
        "taxi_service",
        "taxi_stand",
        "toll_station",
        "train_station",
        "train_ticket_office",
        "tram_stop",
        "transit_depot",
        "transit_station",
        "transit_stop",
        "transportation_service",
        "truck_stop"
    ];

    private static readonly string CategoriesForPrompt = string.Join(", ", ValidCategories);

    public GroqAIService(HttpClient http, IOptions<GroqOptions> options, ILogger<GroqAIService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ResolveCategoryAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "cafe";

        var prompt = $"""
            You are a classifier that maps a user query to a Google Places API place type.

            Task:
            Given a Vietnamese query describing a place, return the single most appropriate Google Places type.

            User query:
            "{query}"

            Rules:
            - Return ONLY ONE value.
            - The value MUST be exactly one item from the allowed list.
            - Do NOT create new types.
            - Do NOT explain.
            - Do NOT output anything except the type string.
            - If multiple types match, choose the most specific one.
            - If no type clearly matches, return: point_of_interest.

            Examples:
            Query: quán cà phê
            Output: cafe

            Query: trà sữa
            Output: cafe

            Query: nhà hàng hải sản
            Output: seafood_restaurant

            Query: quán bar
            Output: bar

            Query: nhà nghỉ
            Output: guest_house

            Query: khách sạn
            Output: hotel

            Query: rạp chiếu phim
            Output: movie_theater

            Allowed place types:
            {ValidCategories}

            Output:
            """;

        try
        {
            var content = await CreateChatCompletionAsync(prompt, cancellationToken);
            var category = (content ?? string.Empty).Trim().ToLowerInvariant();

            if (ValidCategories.Contains(category))
            {
                _logger.LogInformation("Groq resolved '{Query}' -> '{Category}'", query, category);
                return category;
            }

            _logger.LogWarning("Groq returned unknown category '{Category}' for query '{Query}'. Falling back to 'cafe'.", category, query);
            return "cafe";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Groq resolve category failed for query '{Query}'. Falling back to 'cafe'.", query);
            return "cafe";
        }
    }

    public async Task<string> SummarizeReviewsAsync(IEnumerable<string> reviews, CancellationToken ct = default)
    {
        var reviewsList = reviews.ToList();
        if (!reviewsList.Any())
            return "Địa điểm này khá ổn, phù hợp để gặp gỡ.";

        var combinedReviews = string.Join("\n- ", reviewsList);
        var prompt = "Bạn là trợ lý ảo OptiGo AI. Hãy phân tích kỹ tất cả các đánh giá sau đây (tối đa 20 review) về một địa điểm và viết một câu tóm tắt cực kỳ súc tích nhưng đầy đủ ý (dưới 45 từ) bằng tiếng Việt. Hãy làm nổi bật những đặc trưng được nhiều người khen nhất hoặc những điểm cần lưu ý. Ngôn ngữ thân thiện, sành điệu, trẻ trung. " +
                     "\n\nDanh sách đánh giá:\n- " + combinedReviews;

        try
        {
            var content = await CreateChatCompletionAsync(prompt, ct);
            if (string.IsNullOrWhiteSpace(content))
                return "Địa điểm tuyệt vời, rất đáng để bạn trải nghiệm.";

            return content.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Groq summarize reviews failed.");
            return "Địa điểm được đánh giá cao, phù hợp cho nhóm bạn.";
        }
    }

    private async Task<string?> CreateChatCompletionAsync(string userPrompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Groq API key is missing. Set Groq:ApiKey in configuration.");

        var endpoint = BuildChatCompletionsEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(_options.Model) ? "llama-3.1-8b-instant" : _options.Model,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            },
            temperature = 0.1
        };

        request.Content = JsonContent.Create(requestBody);
        var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Groq API returned error: {StatusCode} - {Message}", response.StatusCode, error);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (json.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return content.GetString();
        }

        return null;
    }

    private string BuildChatCompletionsEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.groq.com/openai/v1"
            : _options.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/chat/completions";
    }
}

public class GroqOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = "llama-3.1-8b-instant";
}
