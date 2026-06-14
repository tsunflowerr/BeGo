using FluentValidation;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Validators;

public class FindMeetPointCommandValidator : AbstractValidator<FindMeetPointCommand>
{
    public FindMeetPointCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("ID phiên không được để trống");

        RuleFor(x => x.Category)
            .MaximumLength(500)
            .WithMessage("Yêu cầu tìm kiếm không được vượt quá 500 ký tự")
            .When(x => !string.IsNullOrEmpty(x.Category));
    }
}
