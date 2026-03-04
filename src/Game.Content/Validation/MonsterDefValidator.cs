// src/Game.Content/Validation/MonsterDefValidator.cs

using FluentValidation;
using Game.Core.Monsters;

namespace Game.Content.Validation;

public sealed class MonsterDefValidator : AbstractValidator<MonsterDef>
{
    private static readonly string[] ValidAiBehaviors = { "chase", "wander", "guard" };

    public MonsterDefValidator()
    {
        RuleFor(m => m.Id)
            .NotEmpty()
            .WithMessage("Monster Id must not be empty.");

        RuleFor(m => m.Name)
            .NotEmpty()
            .WithMessage("Monster Name must not be empty.");

        RuleFor(m => m.MaxHp)
            .GreaterThan(0)
            .WithMessage("Monster MaxHp must be greater than 0.");

        RuleFor(m => m.Attack)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Monster Attack must not be negative.");

        RuleFor(m => m.Defense)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Monster Defense must not be negative.");

        RuleFor(m => m.ThreatCost)
            .GreaterThan(0)
            .WithMessage("Monster ThreatCost must be greater than 0.");

        RuleFor(m => m.AiBehavior)
            .Must(b => ValidAiBehaviors.Contains(b))
            .WithMessage($"Monster AiBehavior must be one of: {string.Join(", ", ValidAiBehaviors)}.");

        RuleFor(m => m.SightRange)
            .GreaterThan(0)
            .WithMessage("Monster SightRange must be greater than 0.");

        RuleFor(m => m.Color)
            .NotEmpty()
            .Matches(@"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")
            .WithMessage("Monster Color must be a hex color (#RRGGBB or #RRGGBBAA).");
    }
}