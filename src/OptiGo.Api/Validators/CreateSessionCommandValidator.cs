using FluentValidation;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Validators;

public class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(x => x.HostName)
            .NotEmpty()
            .WithMessage("Tên người tạo phòng không được để trống")
            .MaximumLength(100)
            .WithMessage("Tên người tạo phòng không được vượt quá 100 ký tự")
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

        RuleFor(x => x.DefaultQuery)
            .MaximumLength(500)
            .WithMessage("Yêu cầu tìm kiếm không được vượt quá 500 ký tự")
            .When(x => !string.IsNullOrEmpty(x.DefaultQuery));
    }
}
