// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/AlbumRepository.cs.
// Changed: the read-only methods are kept and AlbumExists, Add, Update, Delete are left out,
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
using Microsoft.Data.SqlClient;
using Dapper;

namespace Chinook.DataDapper.Repositories
{
    public class AlbumRepository : IAlbumRepository
    {
        private readonly DbInfo _dbInfo;

        public AlbumRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<Album>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var albums = await cn.QueryAsync<Album>("Select * From Album");
            return albums.ToList();
        }

        public async Task<Album> GetById(int? id)
        {
            using var cn = Connection;
            cn.Open();
            var album = await cn.QueryFirstOrDefaultAsync<Album>("Select * From Album WHERE Id = @id", new { id });
            return album;
        }

        public async Task<List<Album>> GetByArtistId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var albums = await cn.QueryAsync<Album>("Select * From Album WHERE ArtistId = @Id", new { id });
            return albums.ToList();
        }
    }
}
