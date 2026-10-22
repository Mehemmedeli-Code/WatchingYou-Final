-- Read-only business rules. Every line should say 0.
SET NOCOUNT ON;
DECLARE @now datetime2 = SYSUTCDATETIME();
SELECT check_name, n FROM (
  SELECT 'films: available copies < 0 or > total' AS check_name,
         (SELECT COUNT(*) FROM catalog.Movies WHERE AvailableCopies < 0 OR AvailableCopies > TotalCopies) AS n
  UNION ALL SELECT 'films: taken copies <> rentals still out',
         (SELECT COUNT(*) FROM catalog.Movies m
          WHERE m.IsDeleted = 0 AND m.TotalCopies - m.AvailableCopies <>
                (SELECT COUNT(*) FROM rentals.Rentals r WHERE r.MovieId = m.Id AND r.ReturnedAtUtc IS NULL AND r.IsDeleted = 0))
  UNION ALL SELECT 'seats: one seat sold twice in a screening',
         (SELECT COUNT(*) FROM (SELECT ScreeningId, [Row], Number FROM cinema.SeatBookings WHERE IsDeleted = 0
                                GROUP BY ScreeningId, [Row], Number HAVING COUNT(*) > 1) d)
  UNION ALL SELECT 'seats: outside the hall''s rows/seats',
         (SELECT COUNT(*) FROM cinema.SeatBookings b JOIN cinema.Screenings s ON s.Id = b.ScreeningId
          WHERE b.IsDeleted = 0 AND (b.[Row] < 1 OR b.[Row] > s.Rows OR b.Number < 1 OR b.Number > s.SeatsPerRow))
  UNION ALL SELECT 'seats: still booked although the payment was refunded',
         (SELECT COUNT(*) FROM cinema.SeatBookings b JOIN cinema.SeatPayments p ON p.Id = b.PaymentId
          WHERE b.IsDeleted = 0 AND p.RefundedAtUtc IS NOT NULL)
  UNION ALL SELECT 'payments: refunded more than paid, or negative',
         (SELECT COUNT(*) FROM cinema.SeatPayments WHERE Amount < 0 OR ISNULL(RefundedAmount, 0) > Amount)
  UNION ALL SELECT 'screenings: two films overlap in one hall',
         (SELECT COUNT(*) FROM cinema.Screenings a JOIN cinema.Screenings b
            ON a.HallId = b.HallId AND a.Id < b.Id AND a.IsDeleted = 0 AND b.IsDeleted = 0 AND a.IsCancelled = 0 AND b.IsCancelled = 0
          JOIN catalog.Movies ma ON ma.Id = a.MovieId JOIN catalog.Movies mb ON mb.Id = b.MovieId
          WHERE a.StartsAtUtc < DATEADD(minute, mb.DurationMinutes, b.StartsAtUtc)
            AND b.StartsAtUtc < DATEADD(minute, ma.DurationMinutes, a.StartsAtUtc))
  UNION ALL SELECT 'users: the same e-mail twice (any case)',
         (SELECT COUNT(*) FROM (SELECT LOWER(Email) e FROM [identity].Users WHERE IsDeleted = 0 GROUP BY LOWER(Email) HAVING COUNT(*) > 1) d)
  UNION ALL SELECT 'users: the same @username twice (any case)',
         (SELECT COUNT(*) FROM (SELECT LOWER(Username) u FROM [identity].Users WHERE IsDeleted = 0 AND Username IS NOT NULL GROUP BY LOWER(Username) HAVING COUNT(*) > 1) d)
  UNION ALL SELECT 'users: closed or suspended, with a live session',
         (SELECT COUNT(*) FROM [identity].RefreshTokens t JOIN [identity].Users u ON u.Id = t.UserId
          WHERE (u.IsDeleted = 1 OR u.IsSuspended = 1) AND t.RevokedAtUtc IS NULL AND t.ExpiresAtUtc > @now)
  UNION ALL SELECT 'users: SuperAdmin count is not exactly 1',
         (SELECT CASE WHEN COUNT(*) = 1 THEN 0 ELSE 1 END FROM [identity].Users WHERE IsDeleted = 0 AND Roles LIKE '%SuperAdmin%')
  UNION ALL SELECT 'users: a staff PIN on an account with no staff role',
         (SELECT COUNT(*) FROM [identity].Users WHERE StaffPinHash IS NOT NULL
          AND Roles NOT LIKE '%Admin%' AND Roles NOT LIKE '%Security%' AND Roles NOT LIKE '%Cashier%' AND Roles NOT LIKE '%Moderator%')
  UNION ALL SELECT 'rentals: returned or due before they were rented',
         (SELECT COUNT(*) FROM rentals.Rentals WHERE ReturnedAtUtc < RentedAtUtc OR DueAtUtc < RentedAtUtc)
  UNION ALL SELECT 'PRO: ends before it starts',
         (SELECT COUNT(*) FROM rentals.Subscriptions WHERE EndsAtUtc < StartsAtUtc)
  UNION ALL SELECT 'PRO: two plans overlapping for one person',
         (SELECT COUNT(*) FROM rentals.Subscriptions a JOIN rentals.Subscriptions b ON a.UserId = b.UserId AND a.Id < b.Id
          WHERE a.StartsAtUtc < b.EndsAtUtc AND b.StartsAtUtc < a.EndsAtUtc)
  UNION ALL SELECT 'users: gender other than female, male or empty',
         (SELECT COUNT(*) FROM [identity].Users WHERE Gender IS NOT NULL AND Gender NOT IN ('female', 'male'))
  UNION ALL SELECT 'SearchingYou: more people joined than wanted',
         (SELECT COUNT(*) FROM cinema.PartnerRequests r
          WHERE (SELECT COUNT(*) FROM cinema.PartnerRequestMembers m WHERE m.RequestId = r.Id) > r.PeopleWanted)
  UNION ALL SELECT 'SearchingYou: the owner joined their own search',
         (SELECT COUNT(*) FROM cinema.PartnerRequestMembers m JOIN cinema.PartnerRequests r ON r.Id = m.RequestId
          WHERE m.UserId = r.UserId)
  UNION ALL SELECT 'SearchingYou: a man in a search for women, or a woman in one for men (films still to come)',
         (SELECT COUNT(*) FROM cinema.PartnerRequestMembers m JOIN cinema.PartnerRequests r ON r.Id = m.RequestId
          JOIN cinema.Screenings s ON s.Id = r.ScreeningId AND s.StartsAtUtc > @now
          JOIN [identity].Users u ON u.Id = m.UserId
          WHERE ((r.Partner = 1 AND u.Gender = 'male') OR (r.Partner = 2 AND u.Gender = 'female')))
) checks;
