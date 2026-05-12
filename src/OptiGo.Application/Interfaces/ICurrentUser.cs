namespace OptiGo.Application.Interfaces;

public interface ICurrentUser
{
    string Subject { get; }
    string? Email { get; }
    string? Name { get; }
    string? PictureUrl { get; }
    bool IsAuthenticated { get; }
}
