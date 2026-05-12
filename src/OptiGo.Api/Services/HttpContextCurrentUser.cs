using System.Security.Claims;
using OptiGo.Application.Interfaces;

namespace OptiGo.Api.Services;

public class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string Subject =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        throw new UnauthorizedAccessException("Authenticated user subject is missing.");

    public string? Email => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public string? Name => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public string? PictureUrl => _httpContextAccessor.HttpContext?.User.FindFirstValue("picture");

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
