using System.ComponentModel.DataAnnotations;
using RuangMeetingWeb.Services;

namespace RuangMeetingWeb.Models;

public class RuanganForm
{
    public int? RuanganId { get; set; }
    public string? RowVersion { get; set; }

    [Required(ErrorMessage = "Kode ruangan wajib diisi"), StringLength(20), Display(Name = "Kode Ruangan")]
    public string KodeRuangan { get; set; } = "";

    [Required(ErrorMessage = "Nama ruangan wajib diisi"), StringLength(100), Display(Name = "Nama Ruangan")]
    public string NamaRuangan { get; set; } = "";

    [Required(ErrorMessage = "Lokasi wajib diisi"), StringLength(100)]
    public string Lokasi { get; set; } = "";

    [Range(1, 10000, ErrorMessage = "Kapasitas harus antara 1 dan 10000")]
    public int Kapasitas { get; set; } = 10;

    [Display(Name = "Status")]
    public bool IsAktif { get; set; } = true;

    public RuanganRequest ToRequest() => new(KodeRuangan, NamaRuangan, Lokasi, Kapasitas, IsAktif);
}

public class RuanganPageVm
{
    public RuanganForm Form { get; set; } = new();
    public List<RuanganDto> Daftar { get; set; } = [];
}

public class ReservasiForm
{
    public long? ReservasiId { get; set; }
    public string? KodeReservasi { get; set; }
    public string? RowVersion { get; set; }
    public Guid IdempotencyKey { get; set; }   // new GUID per rendered "new reservation" form

    [Range(1, int.MaxValue, ErrorMessage = "Ruangan wajib dipilih"), Display(Name = "Nama Ruangan")]
    public int RuanganId { get; set; }

    [Required(ErrorMessage = "Nama pemesan wajib diisi"), StringLength(100), Display(Name = "Nama Pemesan")]
    public string NamaPemesan { get; set; } = "";

    [Required(ErrorMessage = "Judul meeting wajib diisi"), StringLength(200), Display(Name = "Judul Meeting")]
    public string JudulMeeting { get; set; } = "";

    [Required] public DateOnly Tanggal { get; set; }
    [Required, Display(Name = "Jam Mulai")] public TimeOnly JamMulai { get; set; }
    [Required, Display(Name = "Jam Selesai")] public TimeOnly JamSelesai { get; set; }

    [Range(1, 10000, ErrorMessage = "Jumlah peserta minimal 1"), Display(Name = "Jumlah Peserta")]
    public int JumlahPeserta { get; set; } = 1;

    public string Status { get; set; } = "BOOKED";

    public ReservasiRequest ToRequest(bool baru) =>
        new(RuanganId, NamaPemesan, JudulMeeting, Tanggal, JamMulai, JamSelesai, JumlahPeserta, baru ? null : Status);
}

public class ReservasiPageVm
{
    public ReservasiForm Form { get; set; } = new();
    public List<RuanganDto> Ruangan { get; set; } = [];
    public List<ReservasiDto> Daftar { get; set; } = [];
    public DateOnly? Filter { get; set; }
    public static readonly string[] SemuaStatus = ["BOOKED", "CANCELLED", "COMPLETED"];
}
