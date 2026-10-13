// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/MediaTypeRepository.cs.
// Changed: the read-only methods are kept and MediaTypeExists, Add, Update, Delete are left
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
    public class MediaTypeRepository : IMediaTypeRepository
    {
        private readonly DbInfo _dbInfo;

        public MediaTypeRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<MediaType>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var mediaTypes = await cn.QueryAsync<MediaType>("Select * From MediaType");
            return mediaTypes.ToList();
        }

        public async Task<MediaType> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<MediaType>("Select * From MediaType WHERE Id = @Id", new { id });
        }
    }
}
