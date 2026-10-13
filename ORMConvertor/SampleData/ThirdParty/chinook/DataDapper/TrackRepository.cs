// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/TrackRepository.cs.
// Changed: the read-only methods are kept and TrackExists, Add, Update, Delete are left out,
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
    public class TrackRepository : ITrackRepository
    {
        private readonly DbInfo _dbInfo;

        public TrackRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<Track>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>("Select * From Track");
            return tracks.ToList();
        }

        public async Task<Track> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return await cn.QueryFirstOrDefaultAsync<Track>("Select * From Track WHERE Id = @Id", new { id });
        }

        public async Task<List<Track>> GetByInvoiceId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>(
                "SELECT T.Id, T.Name, T.AlbumId, T.MediaTypeId, T.GenreId, T.Composer, T.Milliseconds, T.Bytes, T.UnitPrice FROM Track AS T INNER JOIN InvoiceLine AS IL ON T.Id = IL.TrackId WHERE IL.InvoiceID = @Id",
                new { id });
            return tracks.ToList();
        }

        public async Task<List<Track>> GetByPlaylistId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>(
                "SELECT T.Id, T.Name, T.AlbumId, T.MediaTypeId, T.GenreId, T.Composer, T.Milliseconds, T.Bytes, T.UnitPrice FROM Track AS T INNER JOIN PlaylistTrack AS PLT ON T.Id = PLT.TrackId WHERE PLT.PlayListId = @Id",
                new { id });
            return tracks.ToList();
        }

        public async Task<List<Track>> GetByArtistId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>(
                "SELECT T.Id, T.Name, T.AlbumId, T.MediaTypeId, T.GenreId, T.Composer, T.Milliseconds, T.Bytes, T.UnitPrice FROM Track AS T INNER JOIN Album AS A ON T.AlbumId = A.Id WHERE A.Id = @Id",
                new { id });
            return tracks.ToList();
        }

        public async Task<List<Track>> GetByAlbumId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>("Select * From Track WHERE AlbumId = @Id", new { id });
            return tracks.ToList();
        }

        public async Task<List<Track>> GetByGenreId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>("Select * From Track WHERE GenreId = @Id", new { id });
            return tracks.ToList();
        }

        public async Task<List<Track>> GetByMediaTypeId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>("Select * From Track WHERE MediaTypeId = @Id", new { id });
            return tracks.ToList();
        }
    }
}
