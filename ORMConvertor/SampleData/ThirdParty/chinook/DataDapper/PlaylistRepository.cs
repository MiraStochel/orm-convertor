// ORMConvertor: taken from cwoodruff/ChinookASPNETWebAPI at 82c89e0, ChinookASPNETWebAPI/Chinook.DataDapper/Repositories/PlaylistRepository.cs.
// Changed: the read-only methods are kept and PlaylistExists, Add, Update, Delete are left
// out, because they write through Dapper.Contrib or only serve writing; the using of
// Dapper.Contrib goes with them; in GetTrackByPlaylistId the column Playlist.PlaylistId, which
// the schema and the class call Id, is corrected. See README.md in the folder above.
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
    public class PlaylistRepository : IPlaylistRepository
    {
        private readonly DbInfo _dbInfo;

        public PlaylistRepository(DbInfo dbInfo)
        {
            _dbInfo = dbInfo;
        }

        private IDbConnection Connection => new SqlConnection(_dbInfo.ConnectionStrings);

        public void Dispose()
        {
        }

        public async Task<List<Playlist>> GetAll()
        {
            using IDbConnection cn = Connection;
            cn.Open();
            var playlists = await cn.QueryAsync<Playlist>("Select * From Playlist");
            return playlists.ToList();
        }

        public async Task<Playlist> GetById(int id)
        {
            using var cn = Connection;
            cn.Open();
            return cn.QueryFirstOrDefault<Playlist>("Select * From Playlist WHERE Id = @Id", new { id });
        }

        // ORMConvertor: corrected - the query named the column Playlist.PlaylistId, which the
        // schema of this project calls Id (the class Playlist has no PlaylistId either).
        //   was: "SELECT Track.* FROM Playlist INNER JOIN PlaylistTrack ON Playlist.PlaylistId = PlaylistTrack.PlaylistId INNER JOIN Track ON PlaylistTrack.TrackId = Track.Id WHERE Playlist.PlaylistId = @Id"
        public async Task<List<Track>> GetTrackByPlaylistId(int id)
        {
            using var cn = Connection;
            cn.Open();
            var tracks = await cn.QueryAsync<Track>(
                "SELECT Track.* FROM Playlist INNER JOIN PlaylistTrack ON Playlist.Id = PlaylistTrack.PlaylistId INNER JOIN Track ON PlaylistTrack.TrackId = Track.Id WHERE Playlist.Id = @Id",
                new { id });
            return tracks.ToList();
        }

        public async Task<List<Playlist>> GetByTrackId(int id)
        {
            //SELECT PL.PlaylistId, PL.Name FROM Playlist AS PL INNER JOIN PlaylistTrack PLT ON PL.PlaylistId = PLT.PlaylistId WHERE PLT.TrackID = 5
            using var cn = Connection;
            cn.Open();
            var playlists = await cn.QueryAsync<Playlist>(
                "SELECT PL.Id, PL.Name FROM Playlist AS PL INNER JOIN PlaylistTrack PLT ON PL.Id = PLT.PlaylistId WHERE PLT.TrackID = @Id",
                new { id });
            return playlists.ToList();
        }
    }
}
