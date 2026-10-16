// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/InvoiceLineRepository.cs.
// Changed: the read-only methods are kept and InvoiceLineExists, Add, Update, Delete are left
// out, because they write or only serve writing. See README.md in the folder above.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class InvoiceLineRepository : IInvoiceLineRepository
    {
        private readonly ChinookContext _context;

        public InvoiceLineRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<InvoiceLine>> GetAll() =>
            await _context.InvoiceLines.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<InvoiceLine> GetById(int id) =>
            await _context.InvoiceLines.FindAsync(id);

        public async Task<List<InvoiceLine>> GetByInvoiceId(int id) =>
            await _context.InvoiceLines.Where(a => a.InvoiceId == id).AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<List<InvoiceLine>> GetByTrackId(int id) =>
            await _context.InvoiceLines.Where(a => a.TrackId == id).AsNoTrackingWithIdentityResolution().ToListAsync();
    }
}
