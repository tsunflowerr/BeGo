using FluentValidation;
using OptiGo.Api.Controllers;

namespace OptiGo.Api.Validators;

/// <summary>
/// Validator cho UpdateQueryRequest.
/// </summary>
public class UpdateQueryRequestValidator : AbstractValidator<UpdateQueryRequest>
{
    public UpdateQueryRequestValidator()
    {
        RuleFor(x => x.QueryText)
            .NotEmpty()
            .WithMessage("Yêu cầu tìm kiếm không được để trống")
            .MinimumLength(2)
            .WithMessage("Yêu cầu tìm kiếm phải có ít nhất 2 ký tự")
            .MaximumLength(500)
            .WithMessage("Yêu cầu tìm kiếm không được vượt quá 500 ký tự");
    }
}
