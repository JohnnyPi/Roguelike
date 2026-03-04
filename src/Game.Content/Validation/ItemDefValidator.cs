// src/Game.Content/Validation/ItemDefValidator.cs

using FluentValidation;
using Game.Core.Items;

namespace Game.Content.Validation;

public sealed class ItemDefValidator : AbstractValidator<ItemDef>
{
    public ItemDefValidator()
    {
        RuleFor(i => i.Id)
            .NotEmpty()
            .WithMessage("Item Id must not be empty.");

        RuleFor(i => i.Name)
            .NotEmpty()
            .WithMessage("Item '{PropertyValue}' Name must not be empty.");

        RuleFor(i => i.Color)
            .NotEmpty()
            .Matches(@"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")
            .WithMessage("Item '{PropertyValue}' Color must be a hex color (#RRGGBB or #RRGGBBAA).");

        RuleFor(i => i.MaxStack)
            .GreaterThan(0)
            .When(i => i.Stackable)
            .WithMessage("Stackable item must have MaxStack > 0.");
    }
}