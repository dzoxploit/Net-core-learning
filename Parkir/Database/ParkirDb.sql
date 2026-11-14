/* =====================================================================
   ParkirDb - schema
   Idempotent: safe to run more than once (the app runs it at startup).
   Batches are separated by GO.
   ===================================================================== */

/* ---------- TARIF : rates and policy per vehicle type ---------------- */
IF OBJECT_ID(N'dbo.TARIF', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TARIF
    (
        TarifId         INT IDENTITY(1,1) NOT NULL,
        JenisKendaraan  NVARCHAR(30)      NOT NULL,
        Tarif1          DECIMAL(18,2)     NOT NULL,              -- flat rate for the first JamTarif1 hours
        Tarif2          DECIMAL(18,2)     NOT NULL,              -- rate per extra hour
        JamTarif1       INT               NOT NULL CONSTRAINT DF_TARIF_JamTarif1      DEFAULT (2),
        MenitGratis     INT               NOT NULL CONSTRAINT DF_TARIF_MenitGratis    DEFAULT (10), -- duration < this is free
        MenitToleransi  INT               NOT NULL CONSTRAINT DF_TARIF_MenitToleransi DEFAULT (5),  -- extra minutes > this = 1 more hour
        IsAktif         BIT               NOT NULL CONSTRAINT DF_TARIF_IsAktif        DEFAULT (1),
        UpdatedAt       DATETIME2(0)      NOT NULL CONSTRAINT DF_TARIF_UpdatedAt      DEFAULT (SYSDATETIME()),
        RowVer          ROWVERSION        NOT NULL,              -- optimistic concurrency

        CONSTRAINT PK_TARIF PRIMARY KEY CLUSTERED (TarifId),
        CONSTRAINT UQ_TARIF_JenisKendaraan UNIQUE (JenisKendaraan),
        CONSTRAINT CK_TARIF_Tarif1      CHECK (Tarif1 >= 0),
        CONSTRAINT CK_TARIF_Tarif2      CHECK (Tarif2 >= 0),
        CONSTRAINT CK_TARIF_JamTarif1   CHECK (JamTarif1 >= 1),
        CONSTRAINT CK_TARIF_MenitGratis CHECK (MenitGratis BETWEEN 0 AND 59),
        CONSTRAINT CK_TARIF_MenitToleransi CHECK (MenitToleransi BETWEEN 0 AND 59)
    );
END
GO

/* ---------- Sequence for human-readable receipt numbers --------------
   A SEQUENCE never hands out the same value twice, unlike MAX(id)+1,
   which is racy under concurrent inserts.                              */
IF OBJECT_ID(N'dbo.SEQ_NoTransaksi', N'SO') IS NULL
    CREATE SEQUENCE dbo.SEQ_NoTransaksi AS BIGINT START WITH 1 INCREMENT BY 1 CACHE 50;
GO

/* ---------- TRANSAKSI : parking transactions ------------------------- */
IF OBJECT_ID(N'dbo.TRANSAKSI', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRANSAKSI
    (
        TransaksiId     BIGINT IDENTITY(1,1) NOT NULL,
        NoTransaksi     VARCHAR(20)       NOT NULL
            CONSTRAINT DF_TRANSAKSI_NoTransaksi
            DEFAULT ('PRK-' + RIGHT(REPLICATE('0', 9) + CAST(NEXT VALUE FOR dbo.SEQ_NoTransaksi AS VARCHAR(19)), 9)),
        NoPolisi        NVARCHAR(15)      NOT NULL,
        TarifId         INT               NOT NULL,
        JamMasuk        DATETIME2(0)      NOT NULL,
        JamKeluar       DATETIME2(0)      NOT NULL,
        DurasiMenit     INT               NOT NULL,
        JamDitagih      INT               NOT NULL,
        -- snapshot of the rate in force when the fee was calculated,
        -- so later rate changes don't rewrite history
        Tarif1          DECIMAL(18,2)     NOT NULL,
        Tarif2          DECIMAL(18,2)     NOT NULL,
        JamTarif1       INT               NOT NULL,
        Bayar           DECIMAL(18,2)     NOT NULL,
        IdempotencyKey  UNIQUEIDENTIFIER  NOT NULL,              -- one key per form submit
        CreatedAt       DATETIME2(0)      NOT NULL CONSTRAINT DF_TRANSAKSI_CreatedAt DEFAULT (SYSDATETIME()),
        UpdatedAt       DATETIME2(0)      NULL,
        RowVer          ROWVERSION        NOT NULL,              -- optimistic concurrency

        CONSTRAINT PK_TRANSAKSI PRIMARY KEY CLUSTERED (TransaksiId),
        CONSTRAINT UQ_TRANSAKSI_NoTransaksi    UNIQUE (NoTransaksi),
        CONSTRAINT UQ_TRANSAKSI_IdempotencyKey UNIQUE (IdempotencyKey),
        -- backstop: the same vehicle can't be logged twice with the same entry time
        CONSTRAINT UQ_TRANSAKSI_NoPolisi_JamMasuk UNIQUE (NoPolisi, JamMasuk),
        CONSTRAINT FK_TRANSAKSI_TARIF FOREIGN KEY (TarifId) REFERENCES dbo.TARIF (TarifId),
        CONSTRAINT CK_TRANSAKSI_Jam   CHECK (JamKeluar >= JamMasuk),
        CONSTRAINT CK_TRANSAKSI_Bayar CHECK (Bayar >= 0)
    );

    CREATE INDEX IX_TRANSAKSI_JamMasuk ON dbo.TRANSAKSI (JamMasuk DESC);
END
GO

/* ---------- Seed rates ----------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.TARIF WHERE JenisKendaraan = N'Mobil')
    INSERT dbo.TARIF (JenisKendaraan, Tarif1, Tarif2) VALUES (N'Mobil', 2000, 500);
IF NOT EXISTS (SELECT 1 FROM dbo.TARIF WHERE JenisKendaraan = N'Motor')
    INSERT dbo.TARIF (JenisKendaraan, Tarif1, Tarif2) VALUES (N'Motor', 1000, 500);
GO
