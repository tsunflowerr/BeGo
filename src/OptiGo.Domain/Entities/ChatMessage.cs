namespace OptiGo.Domain.Entities;

public class ChatMessage
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid MemberId { get; private set; }
    public string SenderName { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    public Session? Session { get; private set; }
    public Member? Member { get; private set; }

    private ChatMessage()
    {
    }

    public ChatMessage(Guid sessionId, Guid memberId, string senderName, string text)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        MemberId = memberId;
        SenderName = string.IsNullOrWhiteSpace(senderName) ? "Member" : senderName.Trim();
        Text = string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("Message text is required.", nameof(text)) : text.Trim();
        CreatedAt = DateTime.UtcNow;
    }
}
