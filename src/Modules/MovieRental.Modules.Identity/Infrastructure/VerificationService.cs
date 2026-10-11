using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Results;

namespace MovieRental.Modules.Identity.Infrastructure;

public interface IVerificationService
{
    Task<Result> IssueAsync(AppUser user, VerificationChannel channel, VerificationPurpose purpose, CancellationToken ct);

    /// <summary>Checks the code and consumes it, but applies no side effect of its own —
    /// the caller decides what confirming means.</summary>
    Task<Result> CheckAsync(AppUser user, VerificationChannel channel, VerificationPurpose purpose,
        string code, CancellationToken ct);
}

internal sealed class VerificationService(
    IdentityDbContext db, ITokenService tokens, IEmailSender email, ISmsSender sms, IOptions<JwtOptions> options)
    : IVerificationService
{
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public async Task<Result> IssueAsync(AppUser user, VerificationChannel channel,
        VerificationPurpose purpose, CancellationToken ct)
    {
        if (channel == VerificationChannel.Sms && string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Result.Failure(Error.Validation("Add a phone number to your profile first."));

        var last = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Channel == channel && c.Purpose == purpose)
            .OrderByDescending(c => c.SentAtUtc)
            .FirstOrDefaultAsync(ct);

        if (last is not null && DateTime.UtcNow - last.SentAtUtc < ResendCooldown)
        {
            var wait = (int)(ResendCooldown - (DateTime.UtcNow - last.SentAtUtc)).TotalSeconds + 1;
            return Result.Failure(Error.Conflict($"Wait {wait} more seconds before asking for another code."));
        }

        // Any earlier code for this channel stops working the moment a new one is issued.
        var live = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Channel == channel && c.Purpose == purpose
                     && c.ConsumedAtUtc == null)
            .ToListAsync(ct);
        foreach (var old in live) old.ConsumedAtUtc = DateTime.UtcNow;

        var code = tokens.CreateNumericCode();
        var (hash, salt) = VerificationCodeHasher.Create(code);
        var minutes = options.Value.VerificationCodeMinutes;

        db.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Channel = channel,
            Purpose = purpose,
            CodeHash = hash,
            Salt = salt,
            SentAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(minutes)
        });
        await db.SaveChangesAsync(ct);

        // Sent after the save: a code the user holds but the database has not seen is worse
        // than a code stored but not delivered, which they can simply request again.
        var resetting = purpose == VerificationPurpose.PasswordReset;

        if (channel == VerificationChannel.Sms)
        {
            await sms.SendAsync(new SmsRequest(user.PhoneNumber!,
                $"WatchingYou code: {code}. Valid for {minutes} minutes."), ct);
        }
        else
        {
            var subject = resetting ? "Reset your WatchingYou password" : "Your WatchingYou verification code";
            var lead = resetting
                ? "Use this code to set a new password:"
                : "Your verification code is:";
            var warning = resetting
                ? "<p>If you did not ask to reset your password, ignore this message — nothing has changed, and your current password still works.</p>"
                : "<p>If you did not ask for this, ignore the message.</p>";

            await email.SendAsync(new EmailRequest(user.Email, subject,
                $"""
                 <p>Hi {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>
                 <p>{lead}</p>
                 <p style="font-size:20px;letter-spacing:3px"><strong>{code}</strong></p>
                 <p>It expires in {minutes} minutes and can be used once.</p>
                 {warning}
                 """), ct);
        }

        return Result.Success();
    }

    public async Task<Result> CheckAsync(AppUser user, VerificationChannel channel,
        VerificationPurpose purpose, string code, CancellationToken ct)
    {
        var pending = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Channel == channel && c.Purpose == purpose
                     && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.SentAtUtc)
            .FirstOrDefaultAsync(ct);

        if (pending is null || !pending.IsUsable)
            return Result.Failure(Error.Validation("That code has expired or been used up. Ask for a new one."));

        // Spend an attempt before looking at the code, in one conditional UPDATE. Counting after
        // a wrong guess let parallel requests all read the same count and each get a guess.
        var spent = await db.VerificationCodes
            .Where(c => c.Id == pending.Id && c.ConsumedAtUtc == null && c.Attempts < VerificationCode.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (spent == 0)
            return Result.Failure(Error.Validation("Too many wrong attempts. Request a new code."));

        if (!VerificationCodeHasher.Verify(code.Trim(), pending.CodeHash, pending.Salt))
        {
            var left = Math.Max(0, VerificationCode.MaxAttempts - pending.Attempts - 1);
            return Result.Failure(left == 0
                ? Error.Validation("Too many wrong attempts. Request a new code.")
                : Error.Validation($"Incorrect code. {left} attempts left."));
        }

        // Conditional too, so one code cannot be used by two requests at once.
        var consumed = await db.VerificationCodes
            .Where(c => c.Id == pending.Id && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, DateTime.UtcNow), ct);
        return consumed == 1
            ? Result.Success()
            : Result.Failure(Error.Validation("That code has expired or been used up. Ask for a new one."));
    }
}
