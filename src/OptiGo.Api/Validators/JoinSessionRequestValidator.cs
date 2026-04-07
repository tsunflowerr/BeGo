using FluentValidation;
using OptiGo.Api.Controllers;
using OptiGo.Domain.Enums;

namespace OptiGo.Api.Validators;

/// <summary>
/// Validator cho JoinSessionRequest.
/// </summary>
public class JoinSessionRequestValidator : AbstractValidator<JoinSessionRequest>
{
    public JoinSessionRequestValidator()
    {
        RuleFor(x => x.MemberName)
            .NotEmpty()
            .WithMessage("Tên thành viên không được để trống")
            .MinimumLength(2)
            .WithMessage("Tên thành viên phải có ít nhất 2 ký tự")
            .MaximumLength(50)
            .WithMessage("Tên thành viên không được vượt quá 50 ký tự")
            .Matches(@"^[\p{L}\p{N}\s\-_\.]+$")
            .WithMessage("Tên chỉ được chứa chữ cái, số, dấu cách, gạch ngang, gạch dưới và dấu chấm");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90)
            .WithMessage("Vĩ độ phải nằm trong khoảng -90 đến 90");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180)
            .WithMessage("Kinh độ phải nằm trong khoảng -180 đến 180");

        RuleFor(x => x.TransportMode)
            .IsInEnum()
            .WithMessage("Phương tiện di chuyển không hợp lệ");
    }
}
