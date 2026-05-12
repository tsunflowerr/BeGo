using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

public record GetChatMessagesQuery(Guid SessionId, int Take = 50) : IRequest<IReadOnlyList<ChatMessageDto>>;

public record SendChatMessageCommand(Guid SessionId, Guid MemberId, string Text) : IRequest<ChatMessageDto>;

public class ChatMessageDto
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public Guid MemberId { get; init; }
    public string SenderName { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public class GetChatMessagesHandler : IRequestHandler<GetChatMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IChatMessageRepository _chatMessageRepository;
    private readonly ICurrentUser _currentUser;

    public GetChatMessagesHandler(
        ISessionRepository sessionRepository,
        IChatMessageRepository chatMessageRepository,
        ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _chatMessageRepository = chatMessageRepository;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ChatMessageDto>> Handle(GetChatMessagesQuery request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken)
            ?? throw new DomainException($"Session {request.SessionId} not found.");

        SessionAuthorization.RequireCurrentMember(session, _currentUser);
        var messages = await _chatMessageRepository.GetRecentForSessionAsync(session.Id, request.Take, cancellationToken);
        return messages.Select(ToDto).ToList();
    }

    internal static ChatMessageDto ToDto(ChatMessage message) => new()
    {
        Id = message.Id,
        SessionId = message.SessionId,
        MemberId = message.MemberId,
        SenderName = message.SenderName,
        Text = message.Text,
        CreatedAt = message.CreatedAt
    };
}

public class SendChatMessageHandler : IRequestHandler<SendChatMessageCommand, ChatMessageDto>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IChatMessageRepository _chatMessageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ICurrentUser _currentUser;

    public SendChatMessageHandler(
        ISessionRepository sessionRepository,
        IChatMessageRepository chatMessageRepository,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _chatMessageRepository = chatMessageRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _currentUser = currentUser;
    }

    public async Task<ChatMessageDto> Handle(SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new DomainException("Message text is required.");

        if (request.Text.Trim().Length > 1000)
            throw new DomainException("Message text cannot exceed 1000 characters.");

        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken)
            ?? throw new DomainException($"Session {request.SessionId} not found.");

        SessionAuthorization.RequireMemberOwnerOrHost(session, request.MemberId, _currentUser);
        var member = session.Members.FirstOrDefault(m => m.Id == request.MemberId)
            ?? throw new DomainException("Member not found in the session.");

        var message = new ChatMessage(session.Id, member.Id, member.Name, request.Text);
        await _chatMessageRepository.AddAsync(message, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = GetChatMessagesHandler.ToDto(message);
        await _notifier.NotifyChatMessageSentAsync(session.Id, dto, cancellationToken);

        return dto;
    }
}
