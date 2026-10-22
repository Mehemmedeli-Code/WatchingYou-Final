-- Read-only. Modules keep no foreign keys between their schemas (each could move to a database
-- of its own), so this is what watches those links instead; tools\db-check.ps1 runs it daily.
-- For every column named *UserId / *MovieId / *ScreeningId / *HallId / *VenueId /
-- *FilmId, how many rows point at a row that does not exist (soft-deleted rows still exist, so
-- they count as present; the empty id means no account at all, as for a ticket sold at the counter). Columns that already have a foreign key are skipped: SQL enforces those.
SET NOCOUNT ON;
DECLARE @targets TABLE (suffix sysname, tbl nvarchar(300));
INSERT @targets VALUES ('UserId', '[identity].[Users]'), ('FollowerId', '[identity].[Users]'), ('FolloweeId', '[identity].[Users]'),
  ('MovieId', '[catalog].[Movies]'), ('ScreeningId', '[cinema].[Screenings]'), ('HallId', '[cinema].[Halls]'),
  ('VenueId', '[cinema].[Venues]'), ('FilmId', '[media].[ShortFilms]'), ('ShortFilmId', '[media].[ShortFilms]'), ('RequestId', '[cinema].[PartnerRequests]'), ('PaymentId', '[cinema].[SeatPayments]');
DECLARE @out TABLE (col nvarchar(400), refs int, orphans int, hasFk bit);
DECLARE @s sysname, @t sysname, @c sysname, @target nvarchar(300), @fk bit, @sql nvarchar(max);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
  SELECT s.name, t.name, c.name, x.tbl,
         CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_key_columns f WHERE f.parent_object_id = t.object_id AND f.parent_column_id = c.column_id) THEN 1 ELSE 0 END
  FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
  JOIN @targets x ON c.name = x.suffix OR (c.name LIKE '%' + x.suffix AND x.suffix = 'UserId')
  WHERE t.name NOT LIKE '__EF%' AND c.system_type_id = 36;   -- uniqueidentifier
OPEN cur;
FETCH NEXT FROM cur INTO @s, @t, @c, @target, @fk;
WHILE @@FETCH_STATUS = 0
BEGIN
  SET @sql = N'SELECT @r = COUNT(*), @o = SUM(CASE WHEN p.Id IS NULL THEN 1 ELSE 0 END)
               FROM ' + QUOTENAME(@s) + N'.' + QUOTENAME(@t) + N' x LEFT JOIN ' + @target + N' p ON p.Id = x.' + QUOTENAME(@c) + N'
               WHERE x.' + QUOTENAME(@c) + N' IS NOT NULL AND x.' + QUOTENAME(@c) + N' <> ''00000000-0000-0000-0000-000000000000''';
  DECLARE @r int, @o int;
  EXEC sp_executesql @sql, N'@r int OUTPUT, @o int OUTPUT', @r OUTPUT, @o OUTPUT;
  INSERT @out VALUES (@s + N'.' + @t + N'.' + @c + N' -> ' + @target, @r, ISNULL(@o, 0), @fk);
  FETCH NEXT FROM cur INTO @s, @t, @c, @target, @fk;
END
CLOSE cur; DEALLOCATE cur;
SELECT col, refs, orphans, CASE hasFk WHEN 1 THEN 'FK' ELSE 'no FK' END fk FROM @out ORDER BY orphans DESC, col;
