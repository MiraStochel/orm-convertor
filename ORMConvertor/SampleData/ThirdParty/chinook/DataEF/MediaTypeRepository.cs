// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/MediaTypeRepository.cs.
// Changed: the read-only methods are kept and MediaTypeExists, Add, Update, Delete are left
// out, because they write or only serve writing. See README.md in the folder above.
using System.Collections.Generic;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class MediaTypeRepository : IMediaTypeRepository
    {
        private readonly ChinookContext _context;

        public MediaTypeRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<MediaType>> GetAll() =>
            await _context.MediaTypes.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<MediaType> GetById(int id) =>
            await _context.MediaTypes.FindAsync(id);
    }
}
