// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/ArtistRepository.cs.
// Changed: the read-only methods are kept and ArtistExists, Add, Update, Delete are left out,
// because they write or only serve writing. See README.md in the folder above.
using System.Collections.Generic;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class ArtistRepository : IArtistRepository
    {
        private readonly ChinookContext _context;

        public ArtistRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Artist>> GetAll() =>
            await _context.Artists.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Artist> GetById(int id) =>
            await _context.Artists.FindAsync(id);
    }
}
