using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RuangMeetingApi.Data;
using RuangMeetingApi.Models;

namespace RuangMeetingApi.Services;

internal static class DbKunci
{
    /// <summary>
    /// Reads a room row with an update lock held until the current transaction ends.
    /// Every write that depends on a room's state (create/edit reservation, deactivate room,
    /// change capacity, delete room) takes this lock first, so they are serialized per room
    /// and the "check then write" sequence is atomic. The overlap trigger takes the same lock.
    /// </summary>
    public static Task<Ruangan?> KunciRuanganAsync(this AppDbContext db, int ruanganId, bool tracking, CancellationToken ct)
    {
        var q = db.Ruangan.FromSql($"SELECT * FROM dbo.RUANGAN WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE RuanganId = {ruanganId}");
        return (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    public static int? SqlNumber(Exception ex) => ex switch
    {
        SqlException s => s.Number,
        { InnerException: SqlException s } => s.Number,
        _ => null,
    };

    public const int SqlUniqueIndex = 2601, SqlUniqueConstraint = 2627, Deadlock = 1205, TriggerBentrok = 50010;
}
