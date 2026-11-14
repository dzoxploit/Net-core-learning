using System.ComponentModel.DataAnnotations;

namespace Parkir.Models;

public class TransaksiForm
{
    public long? TransaksiId { get; set; }
    public string? NoTransaksi { get; set; }
    public string? RowVer { get; set; }            // base64 ROWVERSION of the loaded row
    public Guid IdempotencyKey { get; set; }       // new GUID on every fresh form

    [Required(ErrorMessage = "No. Polisi wajib diisi"), StringLength(20)]
    [Display(Name = "No. Polisi")]
    public string NoPolisi { get; set; } = "";

    [Required, Display(Name = "Jenis Kendaraan")]
    public int TarifId { get; set; }

    [Required] public DateOnly TanggalMasuk { get; set; }
    [Required] public TimeOnly JamMasuk { get; set; }
    [Required] public DateOnly TanggalKeluar { get; set; }
    [Required] public TimeOnly JamKeluar { get; set; }

    public DateTime Masuk => TanggalMasuk.ToDateTime(JamMasuk);
    public DateTime Keluar => TanggalKeluar.ToDateTime(JamKeluar);
}

public class ParkirPageVm
{
    public TransaksiForm Form { get; set; } = new();
    public List<Tarif> DaftarTarif { get; set; } = [];
    public List<Transaksi> DaftarTransaksi { get; set; } = [];
    public string? Durasi { get; set; }
    public decimal? Bayar { get; set; }
}

public class TarifForm
{
    public int? TarifId { get; set; }
    public string? RowVer { get; set; }

    [Required(ErrorMessage = "Jenis kendaraan wajib diisi"), StringLength(30)]
    [Display(Name = "Jenis Kendaraan")]
    public string JenisKendaraan { get; set; } = "";

    [Range(0, 100_000_000), Display(Name = "Tarif 1")] public decimal Tarif1 { get; set; }
    [Range(0, 100_000_000), Display(Name = "Tarif 2")] public decimal Tarif2 { get; set; }
    [Range(1, 24), Display(Name = "Jam Tarif 1")] public int JamTarif1 { get; set; } = 2;
    [Range(0, 59), Display(Name = "Menit Gratis")] public int MenitGratis { get; set; } = 10;
    [Range(0, 59), Display(Name = "Menit Toleransi")] public int MenitToleransi { get; set; } = 5;
    [Display(Name = "Aktif")] public bool IsAktif { get; set; } = true;
}

public class TarifPageVm
{
    public TarifForm Form { get; set; } = new();
    public List<Tarif> DaftarTarif { get; set; } = [];
}
