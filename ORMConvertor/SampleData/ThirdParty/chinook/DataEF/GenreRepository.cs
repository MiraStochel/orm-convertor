// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/GenreRepository.cs.
// Changed: the read-only methods are kept and GenreExists, Add, Update, Delete are left out,
// because they write or only serve writing; FindAsync(id) is written as Where(x => x.Id ==
// id).FirstOrDefaultAsync() (1x). See README.md in the folder above.
using System.Collections.Generic;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class GenreRepository : IGenreRepository
    {
        private readonly ChinookContext _context;

        public GenreRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Genre>> GetAll() =>
            await _context.Genres.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Genre> GetById(int id)
        {
            var dbGenre = await _context.Genres.Where(x => x.Id == id).FirstOrDefaultAsync();
            return dbGenre;
        }
    }
}
