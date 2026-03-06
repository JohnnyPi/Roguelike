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
            .InclusiveBetween(0f, 10f)
            .WithMessage("Biome ElevationMax must be between 0 and 10.");

        RuleFor(b => b.ElevationMin)
            .InclusiveBetween(0f, 10f)
            .WithMessage("Biome ElevationMin must be between 0 and 10.");

        RuleFor(b => b.MoistureMin)
            .InclusiveBetween(0f, 1f)
            .WithMessage("Biome MoistureMin must be between 0 and 1.");

        RuleFor(b => b.MoistureMax)
            .InclusiveBetween(0f, 1f)
            .WithMessage("Biome MoistureMax must be between 0 and 1.");

        RuleFor(b => b)
            .Must(b => b.MoistureMin <= b.MoistureMax)
            .WithMessage(b => $"Biome '{b.Id}' MoistureMin ({b.MoistureMin}) must be <= MoistureMax ({b.MoistureMax}).");

        RuleFor(b => b)
            .Must(b => b.ElevationMin <= b.ElevationMax)
            .WithMessage(b => $"Biome '{b.Id}' ElevationMin ({b.ElevationMin}) must be <= ElevationMax ({b.ElevationMax}).");
    }
}