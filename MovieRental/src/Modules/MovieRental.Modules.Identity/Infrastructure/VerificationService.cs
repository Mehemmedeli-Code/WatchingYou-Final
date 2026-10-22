using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Identity.Infrastructure;

public interface IVerificationService
{
    Task<Result> IssueAsync(AppUser user, VerificationChannel channel, CancellationToken ct);
    Task<Result> ConfirmAsync(AppUser user, VerificationChannel channel, string code, CancellationToken ct);
}

internal sealed class VerificationService(
    IdentityDbContext db, ITokenService tokens, IEmailSender email, ISmsSender sms, IOptions<JwtOptions> options)
    : IVerificationService
{
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public async Task<Result> IssueAsync(AppUser user, VerificationChannel channel, CancellationToken ct)
    {
        if (channel == VerificationChannel.Sms && string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Result.Failure(Error.Validation("Add a phone number to your profile first."));

        var last = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Channel == channel)
            .OrderByDescending(c => c.SentAtUtc)
            .FirstOrDefaultAsync(ct);

        if (last is not null && DateTime.UtcNow - last.SentAtUtc < ResendCooldown)
        {
            var wait = (int)(ResendCooldown - (DateTime.UtcNow - last.SentAtUtc)).TotalSeconds + 1;
            return Result.Failure(Error.Conflict($"Wait {wait} more seconds before asking for another code."));
        }

        // Any earlier code for this channel stops working the moment a new one is issued.
        var live = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Channel == channel && c.ConsumedAtUtc == null)
            .ToListAsync(ct);
        foreach (var old in live) old.ConsumedAtUtc = DateTime.UtcNow;

        var code = tokens.CreateNumericCode();
        var (hash, salt) = VerificationCodeHasher.Create(code);
        var minutes = options.Value.VerificationCodeMinutes;

        db.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Channel = channel,
            CodeHash = hash,
            Salt = salt,
            SentAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(minutes)
        });
        await db.SaveChangesAsync(ct);

        // Sent after the save: a code the user holds but the database has not seen is worse
        // than a code stored but not delivered, which they can simply request again.
        if (channel == VerificationChannel.Sms)
        {
            await sms.SendAsync(new SmsRequest(user.PhoneNumber!,
                $"Reel & Row code: {code}. Valid for {minutes} minutes."), ct);
        }
        else
        {
            await email.SendAsync(new EmailRequest(user.Email, "Your Reel & Row verification code",
                $"""
                 <p>Hi {user.FullName},</p>
                 <p>Your verification code is <strong style="font-size:20px;letter-spacing:3px">{code}</strong></p>
                 <p>It expires in {minutes} minutes and can be used once.</p>
                 <p>If you did not ask for this, ignore the message.</p>
                 """), ct);
        }

        return Result.Success();
    }

    public async Task<Result> ConfirmAsync(AppUser user, VerificationChannel channel, string code, CancellationToken ct)
    {
        var pending = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Channel == channel && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.SentAtUtc)
            .FirstOrDefaultAsync(ct);

        if (pending is null || !pending.IsUsable)
            return Result.Failure(Error.Validation("That code has expired or been used up. Ask for a new one."));

        if (!VerificationCodeHasher.Verify(code.Trim(), pending.CodeHash, pending.Salt))
        {
            pending.Attempts++;
            await db.SaveChangesAsync(ct);

            return Result.Failure(pending.AttemptsLeft == 0
                ? Error.Validation("Too many wrong attempts. Request a new code.")
                : Error.Validation($"Incorrect code. {pending.AttemptsLeft} attempts left."));
        }

        if (channel == VerificationChannel.Email) user.IsEmailConfirmed = true;
        else user.IsPhoneConfirmed = true;

        pending.ConsumedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
