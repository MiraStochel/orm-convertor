// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/TrackRepository.cs.
// Changed: the read-only methods are kept and TrackExists, Add, Update, Delete are left out,
// because they write or only serve writing; the tracks of the playlist through a correlated Any
// over PlaylistTracks instead of projecting the navigation p.Track; the tracks of the albums
// through a correlated Any over Albums instead of SelectMany over the navigation t.Tracks. See
// README.md in the folder above.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class TrackRepository : ITrackRepository
    {
        private readonly ChinookContext _context;

        public TrackRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Track>> GetAll() =>
            await _context.Tracks.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Track> GetById(int id) =>
            await _context.Tracks.FindAsync(id);

        public async Task<List<Track>> GetByAlbumId(int id) =>
            await _context.Tracks.Where(a => a.AlbumId == id).AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<List<Track>> GetByGenreId(int id) =>
            await _context.Tracks.Where(a => a.GenreId == id).AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<List<Track>> GetByMediaTypeId(int id) =>
            await _context.Tracks.Where(a => a.MediaTypeId == id).AsNoTrackingWithIdentityResolution().ToListAsync();

        // ORMConvertor: rewritten - the tracks of the playlist through a correlated Any over PlaylistTracks instead of projecting the navigation p.Track.
        //   was: await _context.PlaylistTracks.Where(p => p.PlaylistId == id).Select(p => p.Track).AsNoTrackingWithIdentityResolution().ToListAsync();
        public async Task<List<Track>> GetByPlaylistId(int id) =>
            await _context.Tracks.Where(t => _context.PlaylistTracks.Any(p => p.TrackId == t.Id && p.PlaylistId == id)).AsNoTrackingWithIdentityResolution().ToListAsync();

        // ORMConvertor: rewritten - the tracks of the albums through a correlated Any over Albums instead of SelectMany over the navigation t.Tracks; the literal 5 is the original's.
        //   was: await _context.Albums.Where(a => a.ArtistId == 5).SelectMany(t => t.Tracks).AsNoTrackingWithIdentityResolution().ToListAsync();
        public async Task<List<Track>> GetByArtistId(int id) =>
            await _context.Tracks.Where(t => _context.Albums.Any(a => a.Id == t.AlbumId && a.ArtistId == 5)).AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<List<Track>> GetByInvoiceId(int id) => await _context.Tracks
            .Where(c => c.InvoiceLines.Any(o => o.InvoiceId == id))
            .AsNoTrackingWithIdentityResolution().ToListAsync();
    }
}
