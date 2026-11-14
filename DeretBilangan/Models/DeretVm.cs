using System.ComponentModel.DataAnnotations;
using DeretBilangan.Services;

namespace DeretBilangan.Models;

public class DeretInput
{
    [Required(ErrorMessage = "Jumlah Deret wajib diisi")]
    [Range(Deret.Minimal, Deret.Maksimal, ErrorMessage = "Jumlah Deret harus antara {1} dan {2}")]
    [Display(Name = "Jumlah Deret")]
    public int? JumlahDeret { get; set; }

    [Display(Name = "Angka yang Dicari")]
    public long? AngkaDicari { get; set; }
}

public class DeretVm
{
    public DeretInput Input { get; set; } = new();
    public HasilDeret? Hasil { get; set; }
}
