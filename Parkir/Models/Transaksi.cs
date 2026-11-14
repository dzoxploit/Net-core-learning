namespace Parkir.Models;

/// <summary>Row of dbo.TRANSAKSI.</summary>
public class Transaksi
{
    public long TransaksiId { get; set; }
    // null until inserted: the DB default (sequence) generates it
    public string NoTransaksi { get; set; } = null!;
    public string NoPolisi { get; set; } = "";
    public int TarifId { get; set; }
    public Tarif? Tarif { get; set; }
    public DateTime JamMasuk { get; set; }
    public DateTime JamKeluar { get; set; }
    public int DurasiMenit { get; set; }
    public int JamDitagih { get; set; }
    public decimal Tarif1 { get; set; }
    public decimal Tarif2 { get; set; }
    public int JamTarif1 { get; set; }
    public decimal Bayar { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVer { get; set; } = [];
}
