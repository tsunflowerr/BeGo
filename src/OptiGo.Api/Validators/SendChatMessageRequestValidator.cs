using FluentValidation;
using OptiGo.Api.Controllers;

namespace OptiGo.Api.Validators;

public class SendChatMessageRequestValidator : AbstractValidator<SendChatMessageRequest>
{
    public SendChatMessageRequestValidator()
    {
        RuleFor(x => x.MemberId)
            .NotEmpty()
            .WithMessage("ID thành viên không được để trống");

        RuleFor(x => x.Text)
            .NotEmpty()
            .WithMessage("Tin nhắn không được để trống")
            .MaximumLength(1000)
            .WithMessage("Tin nhắn không được vượt quá 1000 ký tự");
    }
}
