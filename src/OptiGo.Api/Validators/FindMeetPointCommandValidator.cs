using FluentValidation;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Validators;

/// <summary>
/// Validator cho FindMeetPointCommand.
/// </summary>
public class FindMeetPointCommandValidator : AbstractValidator<FindMeetPointCommand>
{
    public FindMeetPointCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("ID phiên không được để trống");

        RuleFor(x => x.Category)
            .MaximumLength(100)
            .WithMessage("Danh mục không được vượt quá 100 ký tự")
            .When(x => !string.IsNullOrEmpty(x.Category));
    }
}
