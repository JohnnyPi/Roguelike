// src/Game.Content/Validation/TileDefValidator.cs

using FluentValidation;
using Game.Core.Tiles;

namespace Game.Content.Validation;

public sealed class TileDefValidator : AbstractValidator<TileDef>
{
    public TileDefValidator()
    {
        RuleFor(t => t.Id)
            .NotEmpty()
            .WithMessage("Tile Id must not be empty.");

        RuleFor(t => t.Name)
            .NotEmpty()
            .WithMessage("Tile '{PropertyValue}' Name must not be empty.");

        RuleFor(t => t.Color)
            .NotEmpty()
            .Matches(@"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")
            .WithMessage("Tile '{PropertyValue}' Color must be a hex color (#RRGGBB or #RRGGBBAA).");
    }
}