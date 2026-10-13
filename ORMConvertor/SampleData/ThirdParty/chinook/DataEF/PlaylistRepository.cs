// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/PlaylistRepository.cs.
// Changed: the read-only methods are kept and PlaylistExists, Add, Update, Delete are left
// out, because they write or only serve writing; FindAsync(id) is written as Where(x => x.Id
// == id).FirstOrDefaultAsync() (1x). See README.md in the folder above.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class PlaylistRepository : IPlaylistRepository
    {
        private readonly ChinookContext _context;

        public PlaylistRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Playlist>> GetAll() =>
            await _context.Playlists.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Playlist> GetById(int id) =>
            await _context.Playlists.Where(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<List<Playlist>> GetByTrackId(int id)
        {
            return await _context.Playlists
                .Where(c => c.PlaylistTracks.Any(o => o.TrackId == id))
                .AsNoTrackingWithIdentityResolution().ToListAsync();
        }
    }
}
