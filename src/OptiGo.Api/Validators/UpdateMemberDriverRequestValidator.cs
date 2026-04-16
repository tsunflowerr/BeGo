using FluentValidation;
using OptiGo.Api.Controllers;

namespace OptiGo.Api.Validators;

public class UpdateMemberDriverRequestValidator : AbstractValidator<UpdateMemberDriverRequest>
{
    public UpdateMemberDriverRequestValidator()
    {
        RuleFor(x => x.DriverId)
            .Must(driverId => !driverId.HasValue || driverId.Value != Guid.Empty)
            .WithMessage("DriverId không hợp lệ");
    }
}
