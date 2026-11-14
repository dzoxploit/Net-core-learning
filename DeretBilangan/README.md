# Deret Bilangan

`2 + 3 + 4 + 4 + 6 + 8 + 6 + 9 + 12 ... n = ∑Bilangan`

## Pattern

The numbers come in groups of three. Group *k* = `2k, 3k, 4k`:

| k | numbers |
|---|---|
| 1 | 2, 3, 4 |
| 2 | 4, 6, 8 |
| 3 | 6, 9, 12 |
| 4 | 8, 12, 16 |

The number at position *i* (counting from 1) = `((i-1) div 3 + 1) × ((i-1) mod 3 + 2)`, in [`Services/Deret.cs`](Services/Deret.cs).

## Example (from the spec)

Jumlah Deret = 10, Angka yang Dicari = 6 gives:

| Output | Value |
|---|---|
| Deret | `2 + 3 + 4 + 4 + 6 + 8 + 6 + 9 + 12 + 8` |
| Posisi Ditemukan | `5, 7` |
| Hasil Penjumlahan | `62` |

## Run

```bash
dotnet run --launch-profile http          # http://localhost:5014
dotnet test tests/DeretBilangan.Tests     # 12 tests
```

- **Inputs:** Jumlah Deret must be 1 to 10,000. The upper limit only keeps the page readable. Angka yang Dicari is optional.
- **Form:** uses GET because it only calculates, so a result can be shared by URL, e.g. `/?JumlahDeret=10&AngkaDicari=6`.
- **Output:** matching numbers are highlighted in the series.
