// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataEF/Repositories/EmployeeRepository.cs.
// Changed: the read-only methods are kept and EmployeeExists, Add, Update, Delete,
// GetToReports are left out, because they write or only serve writing (GetToReports hands
// FindAsync a query instead of a key, which EF Core rejects at run time); FindAsync(id) is
// written as Where(x => x.Id == id).FirstOrDefaultAsync() (2x). See README.md in the folder
// above.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chinook.DataEF;
using Chinook.Domain.Repositories;
using Chinook.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chinook.DataEFCore.Repositories
{
    public class EmployeeRepository : IEmployeeRepository
    {
        private readonly ChinookContext _context;

        public EmployeeRepository(ChinookContext context)
        {
            _context = context;
        }

        public void Dispose() => _context.Dispose();

        public async Task<List<Employee>> GetAll() =>
            await _context.Employees.AsNoTrackingWithIdentityResolution().ToListAsync();

        public async Task<Employee> GetById(int id) =>
            await _context.Employees.Where(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<Employee> GetReportsTo(int id) =>
            await _context.Employees.Where(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<List<Employee>> GetDirectReports(int id) =>
            await _context.Employees.Where(e => e.ReportsTo == id).AsNoTrackingWithIdentityResolution().ToListAsync();
    }
}
