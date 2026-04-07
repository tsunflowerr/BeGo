using FluentValidation;
using OptiGo.Api.Controllers;

namespace OptiGo.Api.Validators;

/// <summary>
/// Validator cho SubmitVoteRequest.
/// </summary>
public class SubmitVoteRequestValidator : AbstractValidator<SubmitVoteRequest>
{
    public SubmitVoteRequestValidator()
    {
        RuleFor(x => x.MemberId)
            .NotEmpty()
            .WithMessage("ID thành viên không được để trống");

        RuleFor(x => x.VenueId)
            .NotEmpty()
            .WithMessage("ID địa điểm không được để trống")
            .MaximumLength(100)
            .WithMessage("ID địa điểm không hợp lệ");
    }
}
