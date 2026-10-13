// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/EmployeeRepository.cs.
// Changed: the read-only methods are kept and EmployeeExists, Add, Update, Delete are left
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
    public class EmployeeRepository : IEmployeeRepository
    {
        private readonly DbInfo _dbInfo;

        public EmployeeRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<Employee>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var employees = await cn.QueryAsync<Employee>("Select * From Employee");
            return employees.ToList();
        }

        public async Task<Employee> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<Employee>("Select * From Employee WHERE Id = @Id", new { id });
        }

        public async Task<Employee> GetReportsTo(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<Employee>("Select * From Employee WHERE ReportsTo = @Id", new { id });
        }

        public async Task<List<Employee>> GetDirectReports(int id)
        {
            using var cn = Connection;
            cn.Open();
            var employees = await cn.QueryAsync<Employee>("Select * From Employee WHERE ReportsTo = @Id", new { id });
            return employees.ToList();
        }
    }
}
