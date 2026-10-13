// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/InvoiceLineRepository.cs.
// Changed: the read-only methods are kept and InvoiceLineExists, Add, Update, Delete are left
// out, because they write through Dapper.Contrib or only serve writing; the using of
// Dapper.Contrib goes with them. See README.md in the folder above.
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
    public class InvoiceLineRepository : IInvoiceLineRepository
    {
        private readonly DbInfo _dbInfo;

        public InvoiceLineRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<InvoiceLine>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var invoiceLines = await cn.QueryAsync<InvoiceLine>("Select * From InvoiceLine");
            return invoiceLines.ToList();
        }

        public async Task<InvoiceLine> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<InvoiceLine>("Select * From InvoiceLine WHERE Id = @Id", new { id });
        }

        public async Task<List<InvoiceLine>> GetByInvoiceId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var invoiceLines =
                await cn.QueryAsync<InvoiceLine>("Select * From InvoiceLine WHERE InvoiceId = @Id", new { id });
            return invoiceLines.ToList();
        }

        public async Task<List<InvoiceLine>> GetByTrackId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var invoiceLines = cn.Query<InvoiceLine>("Select * From InvoiceLine WHERE TrackId = @Id", new { id });
            return invoiceLines.ToList();
        }
    }
}
