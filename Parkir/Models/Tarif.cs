namespace Parkir.Models;

/// <summary>Row of dbo.TARIF: rate and fee policy for one vehicle type.</summary>
public class Tarif
{
    public int TarifId { get; set; }
    public string JenisKendaraan { get; set; } = "";
    public decimal Tarif1 { get; set; }
    public decimal Tarif2 { get; set; }
    public int JamTarif1 { get; set; } = 2;
    public int MenitGratis { get; set; } = 10;
    public int MenitToleransi { get; set; } = 5;
    public bool IsAktif { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVer { get; set; } = [];
}
