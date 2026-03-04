// src/Game.Content/Validation/BiomeDefValidator.cs

using FluentValidation;
using Game.Core.Biomes;

namespace Game.Content.Validation;

public sealed class BiomeDefValidator : AbstractValidator<BiomeDef>
{
    public BiomeDefValidator()
    {
        RuleFor(b => b.Id)
            .NotEmpty()
            .WithMessage("Biome Id must not be empty.");

        RuleFor(b => b.Name)
            .NotEmpty()
            .WithMessage("Biome '{PropertyValue}' Name must not be empty.");

        RuleFor(b => b.TileId)
            .NotEmpty()
            .WithMessage("Biome '{PropertyValue}' must reference a TileId.");

        RuleFor(b => b.ElevationMax)
            .InclusiveBetween(0f, 1f)
            .WithMessage("Biome ElevationMax must be between 0 and 1.");
    }
}