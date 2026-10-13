// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/CustomerRepository.cs.
// Changed: the read-only methods are kept and CustomerExists, Add, Update, Delete are left
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
    public class CustomerRepository : ICustomerRepository
    {
        private readonly DbInfo _dbInfo;

        public CustomerRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<Customer>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var customers = await cn.QueryAsync<Customer>("Select * From Customer");
            return customers.ToList();
        }

        public async Task<Customer> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<Customer>("Select * From Customer WHERE Id = @Id", new { id });
        }

        public async Task<List<Customer>> GetBySupportRepId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var customers = await cn.QueryAsync<Customer>("Select * From Customer WHERE ArtistId = @Id", new { id });
            return customers.ToList();
        }
    }
}
