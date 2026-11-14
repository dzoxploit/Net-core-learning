/* =====================================================================
   RuangMeetingDb - schema
   Idempotent: safe to run more than once (the API runs it at startup).
   Batches are separated by GO.
   ===================================================================== */

/* ---------- RUANGAN : meeting rooms (master data) -------------------- */
IF OBJECT_ID(N'dbo.RUANGAN', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RUANGAN
    (
        RuanganId    INT IDENTITY(1,1) NOT NULL,
        KodeRuangan  VARCHAR(20)       NOT NULL,             -- e.g. MR-001
        NamaRuangan  NVARCHAR(100)     NOT NULL,
        Lokasi       NVARCHAR(100)     NOT NULL,
        Kapasitas    INT               NOT NULL,
        IsAktif      BIT               NOT NULL CONSTRAINT DF_RUANGAN_IsAktif   DEFAULT (1),
        CreatedAt    DATETIME2(0)      NOT NULL CONSTRAINT DF_RUANGAN_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt    DATETIME2(0)      NULL,
        RowVer       ROWVERSION        NOT NULL,             -- optimistic concurrency (ETag)

        CONSTRAINT PK_RUANGAN PRIMARY KEY CLUSTERED (RuanganId),
        CONSTRAINT UQ_RUANGAN_KodeRuangan UNIQUE (KodeRuangan),
        CONSTRAINT CK_RUANGAN_Kapasitas CHECK (Kapasitas BETWEEN 1 AND 10000)
    );
END
GO

/* ---------- Sequence for reservation numbers (never MAX()+1) --------- */
IF OBJECT_ID(N'dbo.SEQ_KodeReservasi', N'SO') IS NULL
    CREATE SEQUENCE dbo.SEQ_KodeReservasi AS BIGINT START WITH 1 INCREMENT BY 1 CACHE 50;
GO

/* ---------- RESERVASI : reservations (history is kept, never deleted) */
IF OBJECT_ID(N'dbo.RESERVASI', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RESERVASI
    (
        ReservasiId     BIGINT IDENTITY(1,1) NOT NULL,
        KodeReservasi   VARCHAR(20)       NOT NULL
            CONSTRAINT DF_RESERVASI_KodeReservasi
            DEFAULT ('RSV-' + RIGHT(REPLICATE('0', 9) + CAST(NEXT VALUE FOR dbo.SEQ_KodeReservasi AS VARCHAR(19)), 9)),
        RuanganId       INT               NOT NULL,
        NamaPemesan     NVARCHAR(100)     NOT NULL,
        JudulMeeting    NVARCHAR(200)     NOT NULL,
        Tanggal         DATE              NOT NULL,
        JamMulai        TIME(0)           NOT NULL,
        JamSelesai      TIME(0)           NOT NULL,
        JumlahPeserta   INT               NOT NULL,
        Status          VARCHAR(10)       NOT NULL CONSTRAINT DF_RESERVASI_Status DEFAULT ('BOOKED'),
        IdempotencyKey  UNIQUEIDENTIFIER  NOT NULL,              -- one key per form submit
        CreatedAt       DATETIME2(0)      NOT NULL CONSTRAINT DF_RESERVASI_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt       DATETIME2(0)      NULL,
        RowVer          ROWVERSION        NOT NULL,

        CONSTRAINT PK_RESERVASI PRIMARY KEY CLUSTERED (ReservasiId),
        CONSTRAINT UQ_RESERVASI_KodeReservasi  UNIQUE (KodeReservasi),
        CONSTRAINT UQ_RESERVASI_IdempotencyKey UNIQUE (IdempotencyKey),
        CONSTRAINT FK_RESERVASI_RUANGAN FOREIGN KEY (RuanganId) REFERENCES dbo.RUANGAN (RuanganId),
        CONSTRAINT CK_RESERVASI_Status  CHECK (Status IN ('BOOKED', 'CANCELLED', 'COMPLETED')),
        CONSTRAINT CK_RESERVASI_Peserta CHECK (JumlahPeserta >= 1),
        -- start before end, duration 30 minutes .. 8 hours
        CONSTRAINT CK_RESERVASI_Jam     CHECK (JamMulai < JamSelesai),
        CONSTRAINT CK_RESERVASI_Durasi  CHECK (DATEDIFF(MINUTE, JamMulai, JamSelesai) BETWEEN 30 AND 480)
    );

    CREATE INDEX IX_RESERVASI_Ruangan_Tanggal
        ON dbo.RESERVASI (RuanganId, Tanggal, Status) INCLUDE (JamMulai, JamSelesai);
    CREATE INDEX IX_RESERVASI_Tanggal ON dbo.RESERVASI (Tanggal DESC, JamMulai);
END
GO

/* ---------- Backstop: no overlapping BOOKED reservations per room ----
   SQL Server has no exclusion constraint, so a trigger enforces it.
   It first takes an update lock on the room row(s); the API takes the
   same lock BEFORE its own check, so in the normal path this is a no-op
   and concurrent writers to the same room are serialized.             */
CREATE OR ALTER TRIGGER dbo.TR_RESERVASI_NoOverlap
ON dbo.RESERVASI
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted WHERE Status = 'BOOKED') RETURN;

    DECLARE @lock INT;
    SELECT @lock = COUNT(*) FROM dbo.RUANGAN WITH (UPDLOCK, HOLDLOCK)
    WHERE RuanganId IN (SELECT RuanganId FROM inserted WHERE Status = 'BOOKED');

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN dbo.RESERVASI r
          ON  r.RuanganId   = i.RuanganId
          AND r.Tanggal     = i.Tanggal
          AND r.ReservasiId <> i.ReservasiId
          AND r.Status      = 'BOOKED'
          AND r.JamMulai    < i.JamSelesai
          AND r.JamSelesai  > i.JamMulai
        WHERE i.Status = 'BOOKED')
        THROW 50010, 'Reservasi bentrok dengan reservasi lain di ruangan yang sama.', 1;
END
GO

/* ---------- Seed rooms (from the mockup) ------------------------------ */
IF NOT EXISTS (SELECT 1 FROM dbo.RUANGAN)
    INSERT dbo.RUANGAN (KodeRuangan, NamaRuangan, Lokasi, Kapasitas, IsAktif) VALUES
        ('MR-001', N'Meeting Room 1', N'Lantai 1', 10, 1),
        ('MR-002', N'Meeting Room 2', N'Lantai 2', 20, 1),
        ('MR-003', N'Board Room',     N'Lantai 3', 12, 0);
GO
