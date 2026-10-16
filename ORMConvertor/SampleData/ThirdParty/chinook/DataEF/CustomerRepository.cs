// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/CustomerRepository.cs.
// Changed: the read-only methods are kept and CustomerExists, Add, Update, Delete are left
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
    public class CustomerRepository : ICustomerRepository
    {
        private readonly ChinookContext _context;

        public CustomerRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Customer>> GetAll() =>
            await _context.Customers.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Customer> GetById(int id) =>
            await _context.Customers.FindAsync(id);

        public async Task<List<Customer>> GetBySupportRepId(int id) =>
            await _context.Customers.Where(a => a.SupportRepId == id).AsNoTrackingWithIdentityResolution().ToListAsync();
    }
}
