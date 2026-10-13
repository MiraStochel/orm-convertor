// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/InvoiceRepository.cs.
// Changed: the read-only methods are kept and InvoiceExists, Add, Update, Delete are left out,
// because they write through Dapper.Contrib or only serve writing; the using of Dapper.Contrib
// goes with them. See README.md in the folder above.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Chinook.Domain.DbInfo;
using Chinook.Domain.Entities;
using Chinook.Domain.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Chinook.DataDapper.Repositories
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly DbInfo _dbInfo;

        public InvoiceRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<Invoice>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var invoices = Connection.Query<Invoice>("Select * From Invoice");
            return invoices.ToList();
        }

        public async Task<Invoice> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<Invoice>("Select * From Invoice WHERE Id = @Id", new { id });
        }

        public async Task<List<Invoice>> GetByCustomerId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var invoices = await cn.QueryAsync<Invoice>("Select * From Invoice WHERE CustomerId = @Id", new { id });
            return invoices.ToList();
        }

        public Task<List<Invoice>> GetByEmployeeId(int id)
        {
            throw new NotImplementedException();
        }
    }
}
