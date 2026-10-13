// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/InvoiceRepository.cs.
// Changed: the read-only methods are kept and InvoiceExists, Add, Update, Delete are left out,
// because they write or only serve writing; FindAsync(id) is written as Where(x => x.Id ==
// id).FirstOrDefaultAsync() (1x); the invoices of the customers through a correlated Any over
// Customers instead of SelectMany over the navigation t.Invoices. See README.md in the folder
// above.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Entities;
using Chinook.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    /// <summary>
    /// The invoice repository.
    /// </summary>
    public class InvoiceRepository : IInvoiceRepository
    {
        /// <summary>
        /// The _context.
        /// </summary>
        private readonly ChinookContext _context;

        public InvoiceRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        // public List<Invoice> GetAll() =>
        //     _context.Invoices.ToListAsync();

        public async Task<List<Invoice>> GetAll()
        {
            var invoices = _context.Invoices;
            return await invoices.AsNoTrackingWithIdentityResolution().ToListAsync();
        }

        public async Task<Invoice> GetById(int id) =>
            await _context.Invoices.Where(x => x.Id == id).FirstOrDefaultAsync();

        // ORMConvertor: rewritten - the invoices of the customers through a correlated Any over Customers instead of SelectMany over the navigation t.Invoices; the literal 5 is the original's.
        //   was: await _context.Customers.Where(a => a.SupportRepId == 5).SelectMany(t => t.Invoices).AsNoTrackingWithIdentityResolution().ToListAsync();
        public async Task<List<Invoice>> GetByEmployeeId(int id) =>
            await _context.Invoices.Where(i => _context.Customers.Any(c => c.Id == i.CustomerId && c.SupportRepId == 5)).AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<List<Invoice>> GetByCustomerId(int id) =>
            await _context.Invoices.Where(i => i.CustomerId == id).AsNoTrackingWithIdentityResolution().ToListAsync();
    }
}
