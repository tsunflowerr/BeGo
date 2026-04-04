namespace OptiGo.Application.Interfaces;

/// <summary>
/// Giao tiếp với AI để thực hiện các tác vụ ngôn ngữ tự nhiên.
/// Hiện tại: Phân tích query text → Google Places category.
/// Tương lai: Có thể mở rộng thêm các tác vụ AI khác (tóm tắt review, gợi ý, v.v.).
/// Implementation: GeminiAIService (Infrastructure layer).
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Phân tích chuỗi ngôn ngữ tự nhiên và trả về Google Places category type hợp lệ.
    /// Ví dụ: "quán cà phê yên tĩnh" → "coffee_shop"
    ///         "muốn nhậu vui"        → "bar"
    ///         "chỗ ăn trưa rẻ"      → "restaurant"
    /// </summary>
    /// <param name="query">Câu mô tả nhu cầu của người dùng (tiếng Việt hoặc Anh).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Google Places type string. Fallback: "cafe" nếu không xác định được.</returns>
    Task<string> ResolveCategoryAsync(string query, CancellationToken cancellationToken = default);

    // ──────────────────────────────────────────────────────────────────────
    // TODO (Tương lai - chỉ uncomment khi cần, không cần sửa implementation cũ):
    //
    // /// <summary>Tóm tắt reviews của một venue thành 1-2 câu.</summary>
    // Task<string> SummarizeReviewsAsync(IEnumerable<string> reviews, CancellationToken ct = default);
    //
    // /// <summary>Gợi ý caption khi người dùng check-in tại venue.</summary>
    // Task<string> GenerateCheckinCaptionAsync(string venueName, string category, CancellationToken ct = default);
    //
    // /// <summary>Phân tích sentiment voting của nhóm để tư vấn.</summary>
    // Task<string> AnalyzeGroupPreferenceAsync(IEnumerable<Vote> votes, CancellationToken ct = default);
    // ──────────────────────────────────────────────────────────────────────
}
