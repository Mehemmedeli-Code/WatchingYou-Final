using FluentValidation;
using MovieRental.SharedKernel.Cqrs.Behaviors;

namespace MovieRental.Tests;

/// <summary>
/// The validation behaviour is what lets every handler in the project assume well-formed
/// input. If it silently let a bad message through, the checks would have to be repeated in
/// forty handlers — and one of them would be forgotten.
/// </summary>
public class ValidationBehaviorTests
{
    private sealed record Command(string Email, int Seats);

    private sealed class CommandValidator : AbstractValidator<Command>
    {
        public CommandValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Seats).InclusiveBetween(1, 8);
        }
    }

    [Fact]
    public async Task A_valid_message_reaches_the_handler()
    {
        var behaviour = new ValidationBehavior<Command, string>([new CommandValidator()]);
        var result = await behaviour.Handle(new Command("a@b.com", 2), () => Task.FromResult("handled"), default);
        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task An_invalid_message_never_reaches_the_handler()
    {
        var behaviour = new ValidationBehavior<Command, string>([new CommandValidator()]);
        var reached = false;

        await Assert.ThrowsAsync<ValidationException>(() =>
            behaviour.Handle(new Command("not-an-email", 99), () =>
            {
                reached = true;
                return Task.FromResult("handled");
            }, default));

        Assert.False(reached, "the handler must not run when validation fails");
    }

    [Fact]
    public async Task Every_failure_is_reported_at_once()
    {
        // Fixing one field at a time and resubmitting is a miserable way to fill in a form.
        var behaviour = new ValidationBehavior<Command, string>([new CommandValidator()]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            behaviour.Handle(new Command("", 0), () => Task.FromResult("handled"), default));

        Assert.True(exception.Errors.Count() >= 2);
    }

    [Fact]
    public async Task A_message_with_no_validator_passes_straight_through()
    {
        // Most queries have no rules. They must not pay for a validation pass that has
        // nothing to check.
        var behaviour = new ValidationBehavior<Command, string>([]);
        Assert.Equal("handled", await behaviour.Handle(new Command("", -5), () => Task.FromResult("handled"), default));
    }
}
