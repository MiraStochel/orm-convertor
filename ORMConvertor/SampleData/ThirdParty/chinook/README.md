# Chinook Web API: one domain, a Dapper and an EF Core data layer

- **Project:** [cwoodruff/ChinookASPNETWebAPI](https://github.com/cwoodruff/ChinookASPNETWebAPI),
  an ASP.NET Core Web API over the Chinook sample database (a music store) whose repositories
  exist twice behind the same interfaces: once on Dapper, once on EF Core.
- **Commit:** `82c89e0e5f6d650b6d6ec30e3499bf895ab4dad8` (2026-07-06).
- **Licence:** MIT, [`LICENSE`](LICENSE).
- **Taken from:** `ChinookASPNETWebAPI/Chinook.Domain/Entities/` (11 entity classes shared by both
  layers), `Chinook.DataEF/ChinookContext.cs` (the fluent configuration) and the eleven
  repositories of `Chinook.DataDapper/Repositories/` and `Chinook.DataEF/Repositories/`.

The project runs against its author's variant of the Chinook schema, in which every primary key
column is called `Id`; `PlaylistTrack` keeps its composite key.

## Schema

`schema.sql` is `Scripts/Chinook_SqlServer.sql` of
[cwoodruff/ChinookDatabase](https://github.com/cwoodruff/ChinookDatabase) at `8065767`, the
database the project's README points at: the eleven tables and their foreign keys, without the
database creation, the indexes and the rows, every table qualified with `{{schema}}`. The
single-column primary keys are renamed from `<Table>Id` to `Id`, because that is the variant the
project runs against - every entity declares `[Key] public int Id`, the fluent configuration maps
it so and the Dapper statements say `Album.Id`; `PlaylistTrack` keeps its two foreign-key columns
(decision 176).

## What was changed

1. **Entities and the context are unchanged** apart from the provenance note.
2. **Both data layers keep their reads.** `Add`, `Update`, `Delete` and the `…Exists` helpers they
   use are left out of every repository: they write - the Dapper layer through Dapper.Contrib,
   which the tool does not read as Dapper - or ask a yes/no question (`AnyAsync`, `count(1)`)
   that the tool does not translate. The `using` of Dapper.Contrib goes with them.
   `EmployeeRepository.GetToReports` of the EF Core layer is left out as well: it hands
   `FindAsync` a query instead of a key, which EF Core rejects at run time.
3. **EF Core reads that the tool reads only in another form are rewritten**, each with a note and
   its original text above it: the tracks of a playlist, of an artist's albums and the invoices of
   a support representative's customers - written as a projected navigation (`Select(p => p.Track)`)
   or as `SelectMany` over a navigation - become a correlated `Any` over the other set; the tool
   reads both shapes as one side of a join. `FindAsync(id)` and `Any` over a collection navigation
   (`c.InvoiceLines.Any(…)`) stay as the project wrote them: the tool reads the first as the filter
   on the key with the one row taken and the second as the subquery correlated by the relation.
4. **Two Dapper queries are corrected.** `PlaylistRepository.GetTrackByPlaylistId` names
   `Playlist.PlaylistId`, a column the schema and the class call `Id`, so it does not run; it
   now says `Playlist.Id`, and its `Track.*` stays as written. `CustomerRepository.GetBySupportRepId`
   filters by `ArtistId`, a column `Customer` does not have, so it does not run either; it now
   says `SupportRepId`, which the method asks for.

Upstream mistakes that do not keep the code from running stay as they are: `GetByArtistId` of
the EF Core track repository and `GetByEmployeeId` of the invoice repository compare with the
literal `5` instead of `id`, and the Dapper `GetByArtistId` of the track repository compares
`A.Id`, another column than its method name promises.
