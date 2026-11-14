namespace RuangMeetingApi.Services;

public enum JenisGagal
{
    Validasi,          // 422 - business rule / input invalid
    TidakDitemukan,    // 404
    Konflik,           // 409 - clashes with other data (overlap, duplicate code, in use)
    VersiUsang,        // 412 - row changed by someone else since it was read (If-Match)
}

/// <summary>Outcome of a service call: either a value or a typed failure.</summary>
public record Hasil<T>
{
    public T? Data { get; init; }
    public JenisGagal? Gagal { get; init; }
    public string? Pesan { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
    public bool Replay { get; init; }   // idempotent replay of an earlier request
    public bool Ok => Gagal is null;

    public static Hasil<T> Sukses(T data, bool replay = false) => new() { Data = data, Replay = replay };
    public static Hasil<T> Error(JenisGagal jenis, string pesan, Dictionary<string, string[]>? errors = null) =>
        new() { Gagal = jenis, Pesan = pesan, Errors = errors };
}
