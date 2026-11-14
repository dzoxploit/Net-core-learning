using RuangMeetingApi.Contracts;
using RuangMeetingApi.Services;

namespace RuangMeetingApi.Tests;

public class AturanReservasiTests
{
    private static ReservasiRequest Req(string mulai, string selesai, int peserta = 5, string? status = null) =>
        new(1, "Budi", "Sprint Planning", new DateOnly(2030, 1, 15), TimeOnly.Parse(mulai), TimeOnly.Parse(selesai), peserta, status);

    [Theory]
    [InlineData("08:00", "08:30", true)]   // exactly 30 minutes
    [InlineData("08:00", "16:00", true)]   // exactly 8 hours
    [InlineData("08:00", "08:29", false)]  // too short
    [InlineData("08:00", "16:01", false)]  // too long
    [InlineData("10:00", "10:00", false)]  // start == end
    [InlineData("10:00", "09:00", false)]  // start after end
    public void Durasi_dan_urutan_jam(string mulai, string selesai, bool valid) =>
        Assert.Equal(valid, AturanReservasi.Validasi(Req(mulai, selesai)).Count == 0);

    [Fact]
    public void Field_wajib_dan_status()
    {
        var e = AturanReservasi.Validasi(new ReservasiRequest(0, " ", "", new DateOnly(2030, 1, 1), new TimeOnly(8, 0), new TimeOnly(9, 0), 0, "PENDING"));
        Assert.DoesNotContain("JamSelesai", e.Keys); // 08:00-09:00 is fine
        Assert.Contains("RuanganId", e.Keys);
        Assert.Contains("NamaPemesan", e.Keys);
        Assert.Contains("JudulMeeting", e.Keys);
        Assert.Contains("JumlahPeserta", e.Keys);
        Assert.Contains("Status", e.Keys);
    }

    [Theory]
    [InlineData("08:00", "10:00", "09:00", "11:00", true)]
    [InlineData("08:00", "10:00", "07:00", "08:30", true)]
    [InlineData("08:00", "10:00", "08:30", "09:30", true)]  // inside
    [InlineData("08:00", "10:00", "07:00", "11:00", true)]  // surrounding
    [InlineData("08:00", "10:00", "10:00", "11:00", false)] // back-to-back is fine
    [InlineData("08:00", "10:00", "06:00", "08:00", false)]
    public void Bentrok_interval(string a1, string a2, string b1, string b2, bool bentrok) =>
        Assert.Equal(bentrok, AturanReservasi.Bentrok(TimeOnly.Parse(a1), TimeOnly.Parse(a2), TimeOnly.Parse(b1), TimeOnly.Parse(b2)));
}
