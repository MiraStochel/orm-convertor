// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/AlbumRepository.cs.
// Changed: the read-only methods are kept and AlbumExists, Add, Update, Delete are left out,
// because they write or only serve writing; FindAsync(id) is written as Where(x => x.Id ==
// id).FirstOrDefaultAsync() (1x). See README.md in the folder above.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class AlbumRepository : IAlbumRepository
    {
        private readonly ChinookContext _context;

        public AlbumRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Album>> GetAll() => await _context.Albums.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Album> GetById(int? id)
        {
            var dbAlbum = await _context.Albums.Where(x => x.Id == id).FirstOrDefaultAsync();
            return dbAlbum;
        }

        public async Task<List<Album>> GetByArtistId(int id) =>
            await _context.Albums.Where(a => a.ArtistId == id).AsNoTrackingWithIdentityResolution().ToListAsync();
    }
}
