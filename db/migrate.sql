IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [AnesthesiaTypeMasters] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [DefaultCost] decimal(12,2) NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_AnesthesiaTypeMasters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Counters] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Prefix] nvarchar(max) NOT NULL,
        [LastNumber] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Counters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalPackageMasters] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NULL,
        [PackagePrice] decimal(12,2) NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalPackageMasters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalReplacementMasters] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [UnitCost] decimal(12,2) NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalReplacementMasters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DiagnosticTests] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DiagnosticTests] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Doctors] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [RegistrationNo] nvarchar(max) NULL,
        [Speciality] nvarchar(max) NULL,
        [Phone] nvarchar(max) NULL,
        [ConsultationFee] decimal(12,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Doctors] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [H1Register] (
        [Id] uniqueidentifier NOT NULL,
        [SoldOn] datetime2 NOT NULL,
        [BillNo] nvarchar(max) NOT NULL,
        [ProductName] nvarchar(max) NOT NULL,
        [BatchNo] nvarchar(max) NOT NULL,
        [Quantity] int NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [DoctorName] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_H1Register] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [ImportProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NULL,
        [ColumnMap] nvarchar(max) NOT NULL,
        [DateFormats] nvarchar(max) NOT NULL,
        [ExpiryFormats] nvarchar(max) NOT NULL,
        [DefaultGstRate] decimal(12,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ImportProfiles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabAnalytes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [Units] nvarchar(max) NOT NULL,
        [ResultType] int NOT NULL,
        [DecimalPlaces] int NOT NULL,
        [SequenceOrder] int NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabAnalytes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabPackageMasters] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [PackagePrice] decimal(12,2) NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabPackageMasters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabReports] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [SequenceOrder] int NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabReports] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Patients] (
        [Id] uniqueidentifier NOT NULL,
        [PatientNo] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Phone] nvarchar(450) NOT NULL,
        [Gender] int NOT NULL,
        [Age] int NOT NULL,
        [DateOfBirth] datetime2 NULL,
        [BloodGroup] nvarchar(max) NULL,
        [GuardianName] nvarchar(max) NULL,
        [Address] nvarchar(max) NULL,
        [Allergies] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Patients] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Procedures] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [Department] int NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Procedures] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [GenericName] nvarchar(max) NULL,
        [Manufacturer] nvarchar(max) NULL,
        [Composition] nvarchar(max) NULL,
        [Storage] nvarchar(max) NULL,
        [Strength] nvarchar(max) NULL,
        [StrengthValue] decimal(12,2) NULL,
        [PackSize] nvarchar(max) NULL,
        [DispensingUnit] int NOT NULL,
        [UnitsPerPack] int NOT NULL,
        [AllowLooseSale] bit NOT NULL,
        [HsnCode] nvarchar(max) NOT NULL,
        [GstRate] decimal(12,2) NOT NULL,
        [Schedule] int NOT NULL,
        [RackLocation] nvarchar(max) NULL,
        [ReorderLevel] int NOT NULL,
        [IsActive] bit NOT NULL,
        [SearchKey] nvarchar(450) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Settings] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Settings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [StockEntries] (
        [Id] uniqueidentifier NOT NULL,
        [EntryNo] nvarchar(450) NOT NULL,
        [EntryDate] datetime2 NOT NULL,
        [SupplierName] nvarchar(max) NULL,
        [SupplierInvoiceNo] nvarchar(450) NULL,
        [TotalAmount] decimal(12,2) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [ImportedFile] nvarchar(max) NULL,
        [ImportProfile] nvarchar(max) NULL,
        [NetAmount] decimal(12,2) NOT NULL,
        [DiscountPercent] decimal(12,2) NOT NULL,
        [IsProvisional] bit NOT NULL,
        [EnteredBy] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_StockEntries] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [Slug] nvarchar(450) NOT NULL,
        [ClinicName] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [IsActive] bit NOT NULL,
        [LicenseExpiresOn] datetime2 NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Username] nvarchar(450) NOT NULL,
        [DisplayName] nvarchar(max) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [PasswordSalt] nvarchar(max) NOT NULL,
        [Role] int NOT NULL,
        [IsActive] bit NOT NULL,
        [MustChangePassword] bit NOT NULL,
        [LastLoginOn] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [VaccineMasters] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [DoseNumber] int NOT NULL,
        [RecommendedAgeDays] int NOT NULL,
        [Category] nvarchar(max) NOT NULL,
        [SequenceOrder] int NOT NULL,
        [Active] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_VaccineMasters] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabAnalyteReferenceRanges] (
        [Id] uniqueidentifier NOT NULL,
        [AnalyteId] uniqueidentifier NOT NULL,
        [Gender] int NULL,
        [MinAgeYears] decimal(12,2) NULL,
        [MaxAgeYears] decimal(12,2) NULL,
        [LowValue] decimal(12,2) NULL,
        [HighValue] decimal(12,2) NULL,
        [TextRange] nvarchar(max) NULL,
        [Label] nvarchar(max) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabAnalyteReferenceRanges] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LabAnalyteReferenceRanges_LabAnalytes_AnalyteId] FOREIGN KEY ([AnalyteId]) REFERENCES [LabAnalytes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabPackageReports] (
        [Id] uniqueidentifier NOT NULL,
        [PackageId] uniqueidentifier NOT NULL,
        [ReportId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabPackageReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LabPackageReports_LabPackageMasters_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [LabPackageMasters] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_LabPackageReports_LabReports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [LabReports] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabReportAnalytes] (
        [Id] uniqueidentifier NOT NULL,
        [ReportId] uniqueidentifier NOT NULL,
        [AnalyteId] uniqueidentifier NOT NULL,
        [SequenceOrder] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabReportAnalytes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LabReportAnalytes_LabAnalytes_AnalyteId] FOREIGN KEY ([AnalyteId]) REFERENCES [LabAnalytes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LabReportAnalytes_LabReports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [LabReports] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Appointments] (
        [Id] uniqueidentifier NOT NULL,
        [AppointmentNo] nvarchar(450) NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [PatientPhone] nvarchar(max) NOT NULL,
        [DoctorId] uniqueidentifier NOT NULL,
        [DoctorName] nvarchar(max) NOT NULL,
        [ScheduledOn] datetime2 NOT NULL,
        [DurationMinutes] int NOT NULL,
        [ModuleContext] int NOT NULL,
        [Status] int NOT NULL,
        [Reason] nvarchar(max) NULL,
        [Notes] nvarchar(max) NULL,
        [RescheduledFromId] uniqueidentifier NULL,
        [LinkedRecordId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Appointments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Appointments_Appointments_RescheduledFromId] FOREIGN KEY ([RescheduledFromId]) REFERENCES [Appointments] ([Id]),
        CONSTRAINT [FK_Appointments_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Appointments_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [PediatricProfiles] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [FatherName] nvarchar(max) NULL,
        [MotherName] nvarchar(max) NULL,
        [ParentPhone] nvarchar(max) NULL,
        [ParentOccupation] nvarchar(max) NULL,
        [BirthWeightKg] decimal(12,2) NULL,
        [Notes] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_PediatricProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PediatricProfiles_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [ReminderLogs] (
        [Id] uniqueidentifier NOT NULL,
        [SourceKind] int NOT NULL,
        [SourceId] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [DueOn] datetime2 NOT NULL,
        [Channel] int NOT NULL,
        [ActionedOn] datetime2 NULL,
        [ActionedBy] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ReminderLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReminderLogs_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalCases] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [ProcedureId] uniqueidentifier NULL,
        [ProcedureName] nvarchar(max) NULL,
        [PackageId] uniqueidentifier NULL,
        [PackageName] nvarchar(max) NULL,
        [ToothNumber] nvarchar(max) NULL,
        [DoctorId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [StartedOn] datetime2 NOT NULL,
        [CompletedOn] datetime2 NULL,
        [Notes] nvarchar(max) NULL,
        [BaseCost] decimal(12,2) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalCases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DentalCases_DentalPackageMasters_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [DentalPackageMasters] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_DentalCases_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DentalCases_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DentalCases_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [Procedures] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalPackageItems] (
        [Id] uniqueidentifier NOT NULL,
        [PackageId] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NULL,
        [ProcedureName] nvarchar(max) NOT NULL,
        [Quantity] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalPackageItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DentalPackageItems_DentalPackageMasters_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [DentalPackageMasters] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DentalPackageItems_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [Procedures] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Batches] (
        [Id] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [BatchNo] nvarchar(450) NOT NULL,
        [ExpiryDate] datetime2 NOT NULL,
        [Mrp] decimal(12,2) NOT NULL,
        [PurchaseRate] decimal(12,2) NOT NULL,
        [QtyOnHand] int NOT NULL,
        [UnitsPerPack] int NOT NULL,
        [SupplierName] nvarchar(max) NULL,
        [SupplierInvoiceNo] nvarchar(max) NULL,
        [FreePacks] int NOT NULL,
        [PacksReceived] int NOT NULL,
        [ReceivedOn] datetime2 NOT NULL,
        [IsProvisional] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Batches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Batches_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [VendorProductCodes] (
        [Id] uniqueidentifier NOT NULL,
        [VendorProfile] nvarchar(450) NOT NULL,
        [Code] nvarchar(450) NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_VendorProductCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VendorProductCodes_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [StockEntryItems] (
        [Id] uniqueidentifier NOT NULL,
        [StockEntryId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [BatchNo] nvarchar(max) NOT NULL,
        [ExpiryDate] datetime2 NOT NULL,
        [Quantity] int NOT NULL,
        [FreeQuantity] int NOT NULL,
        [UnitsPerPack] int NOT NULL,
        [PurchaseRate] decimal(12,2) NOT NULL,
        [Mrp] decimal(12,2) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_StockEntryItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockEntryItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_StockEntryItems_StockEntries_StockEntryId] FOREIGN KEY ([StockEntryId]) REFERENCES [StockEntries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Visits] (
        [Id] uniqueidentifier NOT NULL,
        [VisitNo] nvarchar(450) NOT NULL,
        [TokenNo] int NOT NULL,
        [ScheduledOn] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [DoctorId] uniqueidentifier NOT NULL,
        [Complaint] nvarchar(max) NULL,
        [Diagnosis] nvarchar(max) NULL,
        [Notes] nvarchar(max) NULL,
        [WeightKg] decimal(12,2) NULL,
        [BloodPressure] nvarchar(max) NULL,
        [TemperatureF] decimal(12,2) NULL,
        [HeightCm] decimal(12,2) NULL,
        [HeartRateBpm] int NULL,
        [Spo2Percent] int NULL,
        [Fee] decimal(12,2) NOT NULL,
        [FeePaid] bit NOT NULL,
        [FeeReceiptNo] nvarchar(max) NULL,
        [FeePaidOn] datetime2 NULL,
        [FeePaymentMode] int NULL,
        [FeeTransactionNo] nvarchar(max) NULL,
        [FollowUpOn] datetime2 NULL,
        [AppointmentId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Visits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Visits_Appointments_AppointmentId] FOREIGN KEY ([AppointmentId]) REFERENCES [Appointments] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Visits_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Visits_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalCaseReplacements] (
        [Id] uniqueidentifier NOT NULL,
        [DentalCaseId] uniqueidentifier NOT NULL,
        [ReplacementId] uniqueidentifier NULL,
        [Name] nvarchar(max) NOT NULL,
        [UnitCost] decimal(12,2) NOT NULL,
        [Quantity] int NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalCaseReplacements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DentalCaseReplacements_DentalCases_DentalCaseId] FOREIGN KEY ([DentalCaseId]) REFERENCES [DentalCases] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DentalCaseReplacements_DentalReplacementMasters_ReplacementId] FOREIGN KEY ([ReplacementId]) REFERENCES [DentalReplacementMasters] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalPayments] (
        [Id] uniqueidentifier NOT NULL,
        [DentalCaseId] uniqueidentifier NOT NULL,
        [ReceiptNo] nvarchar(450) NOT NULL,
        [PaidOn] datetime2 NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [PaymentMode] int NOT NULL,
        [TransactionNo] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DentalPayments_DentalCases_DentalCaseId] FOREIGN KEY ([DentalCaseId]) REFERENCES [DentalCases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DentalSittings] (
        [Id] uniqueidentifier NOT NULL,
        [DentalCaseId] uniqueidentifier NOT NULL,
        [SittingNumber] int NOT NULL,
        [SittingDate] datetime2 NOT NULL,
        [DoctorId] uniqueidentifier NOT NULL,
        [WorkDone] nvarchar(max) NULL,
        [AnesthesiaTypeId] uniqueidentifier NULL,
        [AnesthesiaTypeName] nvarchar(max) NULL,
        [AnesthesiaCost] decimal(12,2) NULL,
        [NextSittingOn] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DentalSittings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DentalSittings_AnesthesiaTypeMasters_AnesthesiaTypeId] FOREIGN KEY ([AnesthesiaTypeId]) REFERENCES [AnesthesiaTypeMasters] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_DentalSittings_DentalCases_DentalCaseId] FOREIGN KEY ([DentalCaseId]) REFERENCES [DentalCases] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DentalSittings_Doctors_DoctorId] FOREIGN KEY ([DoctorId]) REFERENCES [Doctors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [StockAdjustments] (
        [Id] uniqueidentifier NOT NULL,
        [AdjustedOn] datetime2 NOT NULL,
        [BatchId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [ProductName] nvarchar(max) NOT NULL,
        [BatchNo] nvarchar(max) NOT NULL,
        [QuantityBefore] int NOT NULL,
        [QuantityAfter] int NOT NULL,
        [Reason] int NOT NULL,
        [Notes] nvarchar(max) NULL,
        [AdjustedBy] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_StockAdjustments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockAdjustments_Batches_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [Batches] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockAdjustments_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [VaccinationRecords] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [VaccineId] uniqueidentifier NULL,
        [VaccineName] nvarchar(max) NOT NULL,
        [DoseNumber] int NOT NULL,
        [GivenOn] datetime2 NOT NULL,
        [BatchNo] nvarchar(max) NULL,
        [SiteOfInjection] nvarchar(max) NULL,
        [AdministeredBy] nvarchar(max) NULL,
        [ProductId] uniqueidentifier NULL,
        [ProductName] nvarchar(max) NULL,
        [Manufacturer] nvarchar(max) NULL,
        [BatchId] uniqueidentifier NULL,
        [NextDueOn] datetime2 NULL,
        [ProcedureBillItemId] uniqueidentifier NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_VaccinationRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VaccinationRecords_Batches_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [Batches] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_VaccinationRecords_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VaccinationRecords_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_VaccinationRecords_VaccineMasters_VaccineId] FOREIGN KEY ([VaccineId]) REFERENCES [VaccineMasters] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DiagnosticBills] (
        [Id] uniqueidentifier NOT NULL,
        [BillNo] nvarchar(450) NOT NULL,
        [BillDate] datetime2 NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [PatientNo] nvarchar(max) NOT NULL,
        [TotalAmount] decimal(12,2) NOT NULL,
        [Discount] decimal(12,2) NOT NULL,
        [FinalAmount] decimal(12,2) NOT NULL,
        [PaymentMode] int NOT NULL,
        [TransactionNo] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [Remarks] nvarchar(max) NULL,
        [VisitId] uniqueidentifier NULL,
        [ReferredBy] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DiagnosticBills] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiagnosticBills_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DiagnosticBills_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [GrowthMeasurements] (
        [Id] uniqueidentifier NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [VisitId] uniqueidentifier NULL,
        [MeasuredOn] datetime2 NOT NULL,
        [AgeDays] int NOT NULL,
        [WeightKg] decimal(12,2) NULL,
        [HeightCm] decimal(12,2) NULL,
        [HeadCircumferenceCm] decimal(12,2) NULL,
        [BmiValue] decimal(12,2) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_GrowthMeasurements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GrowthMeasurements_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_GrowthMeasurements_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabOrders] (
        [Id] uniqueidentifier NOT NULL,
        [OrderNo] nvarchar(450) NOT NULL,
        [OrderDate] datetime2 NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [PatientNo] nvarchar(max) NOT NULL,
        [PackageId] uniqueidentifier NULL,
        [VisitId] uniqueidentifier NULL,
        [ReferredBy] nvarchar(max) NULL,
        [SpecimenId] nvarchar(max) NULL,
        [CollectedOn] datetime2 NULL,
        [ReceivedOn] datetime2 NULL,
        [TotalAmount] decimal(12,2) NOT NULL,
        [Discount] decimal(12,2) NOT NULL,
        [FinalAmount] decimal(12,2) NOT NULL,
        [PaymentMode] int NOT NULL,
        [TransactionNo] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [Remarks] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LabOrders_LabPackageMasters_PackageId] FOREIGN KEY ([PackageId]) REFERENCES [LabPackageMasters] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_LabOrders_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LabOrders_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [PrescriptionItems] (
        [Id] uniqueidentifier NOT NULL,
        [VisitId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NULL,
        [MedicineName] nvarchar(max) NOT NULL,
        [Dosage] nvarchar(max) NULL,
        [Frequency] nvarchar(max) NULL,
        [Days] int NOT NULL,
        [Quantity] int NOT NULL,
        [Instructions] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_PrescriptionItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PrescriptionItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]),
        CONSTRAINT [FK_PrescriptionItems_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [ProcedureBills] (
        [Id] uniqueidentifier NOT NULL,
        [BillNo] nvarchar(450) NOT NULL,
        [BillDate] datetime2 NOT NULL,
        [PatientId] uniqueidentifier NOT NULL,
        [PatientName] nvarchar(max) NOT NULL,
        [PatientNo] nvarchar(max) NOT NULL,
        [TotalAmount] decimal(12,2) NOT NULL,
        [Discount] decimal(12,2) NOT NULL,
        [FinalAmount] decimal(12,2) NOT NULL,
        [PaymentMode] int NOT NULL,
        [TransactionNo] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [VisitId] uniqueidentifier NULL,
        [ReferredBy] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ProcedureBills] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcedureBills_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProcedureBills_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [Sales] (
        [Id] uniqueidentifier NOT NULL,
        [BillNo] nvarchar(450) NOT NULL,
        [BillDate] datetime2 NOT NULL,
        [PatientId] uniqueidentifier NULL,
        [VisitId] uniqueidentifier NULL,
        [CustomerName] nvarchar(max) NOT NULL,
        [DoctorName] nvarchar(max) NULL,
        [GrossAmount] decimal(12,2) NOT NULL,
        [DiscountAmount] decimal(12,2) NOT NULL,
        [TaxableAmount] decimal(12,2) NOT NULL,
        [CgstAmount] decimal(12,2) NOT NULL,
        [SgstAmount] decimal(12,2) NOT NULL,
        [RoundOff] decimal(12,2) NOT NULL,
        [NetAmount] decimal(12,2) NOT NULL,
        [PaymentMode] int NOT NULL,
        [Status] int NOT NULL,
        [TransactionNo] nvarchar(max) NULL,
        [IsTaxInvoice] bit NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Sales] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sales_Patients_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Sales_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [VisitDiagnosticRequests] (
        [Id] uniqueidentifier NOT NULL,
        [VisitId] uniqueidentifier NOT NULL,
        [TestId] uniqueidentifier NULL,
        [TestName] nvarchar(max) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_VisitDiagnosticRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VisitDiagnosticRequests_DiagnosticTests_TestId] FOREIGN KEY ([TestId]) REFERENCES [DiagnosticTests] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_VisitDiagnosticRequests_Visits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [Visits] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [DiagnosticBillItems] (
        [Id] uniqueidentifier NOT NULL,
        [BillId] uniqueidentifier NOT NULL,
        [TestId] uniqueidentifier NULL,
        [TestName] nvarchar(max) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [Quantity] int NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DiagnosticBillItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DiagnosticBillItems_DiagnosticBills_BillId] FOREIGN KEY ([BillId]) REFERENCES [DiagnosticBills] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DiagnosticBillItems_DiagnosticTests_TestId] FOREIGN KEY ([TestId]) REFERENCES [DiagnosticTests] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabOrderReports] (
        [Id] uniqueidentifier NOT NULL,
        [OrderId] uniqueidentifier NOT NULL,
        [ReportId] uniqueidentifier NULL,
        [ReportName] nvarchar(max) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabOrderReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LabOrderReports_LabOrders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [LabOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_LabOrderReports_LabReports_ReportId] FOREIGN KEY ([ReportId]) REFERENCES [LabReports] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [ProcedureBillItems] (
        [Id] uniqueidentifier NOT NULL,
        [BillId] uniqueidentifier NOT NULL,
        [ProcedureId] uniqueidentifier NULL,
        [ProcedureName] nvarchar(max) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [Quantity] int NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ProcedureBillItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcedureBillItems_ProcedureBills_BillId] FOREIGN KEY ([BillId]) REFERENCES [ProcedureBills] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProcedureBillItems_Procedures_ProcedureId] FOREIGN KEY ([ProcedureId]) REFERENCES [Procedures] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [SaleItems] (
        [Id] uniqueidentifier NOT NULL,
        [SaleId] uniqueidentifier NOT NULL,
        [ProductId] uniqueidentifier NOT NULL,
        [BatchId] uniqueidentifier NOT NULL,
        [ProductName] nvarchar(max) NOT NULL,
        [BatchNo] nvarchar(max) NOT NULL,
        [ExpiryDate] datetime2 NOT NULL,
        [HsnCode] nvarchar(max) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitsPerPack] int NOT NULL,
        [Mrp] decimal(12,2) NOT NULL,
        [DiscountPercent] decimal(12,2) NOT NULL,
        [GstRate] decimal(12,2) NOT NULL,
        [TaxableAmount] decimal(12,2) NOT NULL,
        [GstAmount] decimal(12,2) NOT NULL,
        [LineTotal] decimal(12,2) NOT NULL,
        [PackLabel] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SaleItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SaleItems_Sales_SaleId] FOREIGN KEY ([SaleId]) REFERENCES [Sales] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE TABLE [LabResults] (
        [Id] uniqueidentifier NOT NULL,
        [OrderReportId] uniqueidentifier NOT NULL,
        [AnalyteId] uniqueidentifier NULL,
        [AnalyteName] nvarchar(max) NOT NULL,
        [Units] nvarchar(max) NOT NULL,
        [ResultValue] nvarchar(max) NOT NULL,
        [ReferenceRangeDisplay] nvarchar(max) NOT NULL,
        [Flag] int NOT NULL,
        [EnteredOn] datetime2 NOT NULL,
        [EnteredBy] nvarchar(max) NULL,
        [VerifiedOn] datetime2 NULL,
        [VerifiedBy] nvarchar(max) NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [RowVersion] varbinary(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_LabResults] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LabResults_LabAnalytes_AnalyteId] FOREIGN KEY ([AnalyteId]) REFERENCES [LabAnalytes] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_LabResults_LabOrderReports_OrderReportId] FOREIGN KEY ([OrderReportId]) REFERENCES [LabOrderReports] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AnesthesiaTypeMasters_Name] ON [AnesthesiaTypeMasters] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_DoctorId] ON [Appointments] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_PatientId] ON [Appointments] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_RescheduledFromId] ON [Appointments] ([RescheduledFromId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Appointments_ScheduledOn] ON [Appointments] ([ScheduledOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Appointments_TenantId_AppointmentNo] ON [Appointments] ([TenantId], [AppointmentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Batches_ProductId_BatchNo] ON [Batches] ([ProductId], [BatchNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Counters_TenantId_Name] ON [Counters] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalCaseReplacements_DentalCaseId] ON [DentalCaseReplacements] ([DentalCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalCaseReplacements_ReplacementId] ON [DentalCaseReplacements] ([ReplacementId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalCases_DoctorId] ON [DentalCases] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalCases_PackageId] ON [DentalCases] ([PackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalCases_PatientId] ON [DentalCases] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalCases_ProcedureId] ON [DentalCases] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalPackageItems_PackageId] ON [DentalPackageItems] ([PackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalPackageItems_ProcedureId] ON [DentalPackageItems] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalPackageMasters_Name] ON [DentalPackageMasters] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalPayments_DentalCaseId] ON [DentalPayments] ([DentalCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalPayments_PaidOn] ON [DentalPayments] ([PaidOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DentalPayments_TenantId_ReceiptNo] ON [DentalPayments] ([TenantId], [ReceiptNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalReplacementMasters_Name] ON [DentalReplacementMasters] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalSittings_AnesthesiaTypeId] ON [DentalSittings] ([AnesthesiaTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalSittings_DentalCaseId] ON [DentalSittings] ([DentalCaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DentalSittings_DoctorId] ON [DentalSittings] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiagnosticBillItems_BillId] ON [DiagnosticBillItems] ([BillId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiagnosticBillItems_TestId] ON [DiagnosticBillItems] ([TestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiagnosticBills_BillDate] ON [DiagnosticBills] ([BillDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiagnosticBills_PatientId] ON [DiagnosticBills] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiagnosticBills_TenantId_BillNo] ON [DiagnosticBills] ([TenantId], [BillNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiagnosticBills_VisitId] ON [DiagnosticBills] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DiagnosticTests_Name] ON [DiagnosticTests] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_GrowthMeasurements_PatientId] ON [GrowthMeasurements] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_GrowthMeasurements_VisitId] ON [GrowthMeasurements] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ImportProfiles_TenantId_Name] ON [ImportProfiles] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabAnalyteReferenceRanges_AnalyteId] ON [LabAnalyteReferenceRanges] ([AnalyteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabAnalytes_Name] ON [LabAnalytes] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabOrderReports_OrderId] ON [LabOrderReports] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabOrderReports_ReportId] ON [LabOrderReports] ([ReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabOrders_OrderDate] ON [LabOrders] ([OrderDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabOrders_PackageId] ON [LabOrders] ([PackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabOrders_PatientId] ON [LabOrders] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LabOrders_TenantId_OrderNo] ON [LabOrders] ([TenantId], [OrderNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabOrders_VisitId] ON [LabOrders] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabPackageMasters_Name] ON [LabPackageMasters] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabPackageReports_PackageId] ON [LabPackageReports] ([PackageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabPackageReports_ReportId] ON [LabPackageReports] ([ReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabReportAnalytes_AnalyteId] ON [LabReportAnalytes] ([AnalyteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabReportAnalytes_ReportId] ON [LabReportAnalytes] ([ReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabReports_Name] ON [LabReports] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabResults_AnalyteId] ON [LabResults] ([AnalyteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LabResults_OrderReportId] ON [LabResults] ([OrderReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Patients_Name] ON [Patients] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Patients_Phone] ON [Patients] ([Phone]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Patients_TenantId_PatientNo] ON [Patients] ([TenantId], [PatientNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PediatricProfiles_PatientId] ON [PediatricProfiles] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PrescriptionItems_ProductId] ON [PrescriptionItems] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PrescriptionItems_VisitId] ON [PrescriptionItems] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcedureBillItems_BillId] ON [ProcedureBillItems] ([BillId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcedureBillItems_ProcedureId] ON [ProcedureBillItems] ([ProcedureId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcedureBills_BillDate] ON [ProcedureBills] ([BillDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcedureBills_PatientId] ON [ProcedureBills] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProcedureBills_TenantId_BillNo] ON [ProcedureBills] ([TenantId], [BillNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcedureBills_VisitId] ON [ProcedureBills] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Procedures_Department_Name] ON [Procedures] ([Department], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_Name] ON [Products] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Products_TenantId_SearchKey] ON [Products] ([TenantId], [SearchKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReminderLogs_DueOn] ON [ReminderLogs] ([DueOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReminderLogs_PatientId] ON [ReminderLogs] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SaleItems_SaleId] ON [SaleItems] ([SaleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Sales_BillDate] ON [Sales] ([BillDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Sales_PatientId] ON [Sales] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Sales_TenantId_BillNo] ON [Sales] ([TenantId], [BillNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Sales_VisitId] ON [Sales] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Settings_TenantId_Key] ON [Settings] ([TenantId], [Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_AdjustedOn] ON [StockAdjustments] ([AdjustedOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_BatchId] ON [StockAdjustments] ([BatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_ProductId] ON [StockAdjustments] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockEntries_SupplierInvoiceNo] ON [StockEntries] ([SupplierInvoiceNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockEntries_TenantId_EntryNo] ON [StockEntries] ([TenantId], [EntryNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockEntryItems_ProductId] ON [StockEntryItems] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockEntryItems_StockEntryId] ON [StockEntryItems] ([StockEntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Slug] ON [Tenants] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VaccinationRecords_BatchId] ON [VaccinationRecords] ([BatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VaccinationRecords_PatientId] ON [VaccinationRecords] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VaccinationRecords_ProductId] ON [VaccinationRecords] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VaccinationRecords_VaccineId] ON [VaccinationRecords] ([VaccineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VaccineMasters_Name] ON [VaccineMasters] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VendorProductCodes_ProductId] ON [VendorProductCodes] ([ProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_VendorProductCodes_TenantId_VendorProfile_Code] ON [VendorProductCodes] ([TenantId], [VendorProfile], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VisitDiagnosticRequests_TestId] ON [VisitDiagnosticRequests] ([TestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_VisitDiagnosticRequests_VisitId] ON [VisitDiagnosticRequests] ([VisitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_AppointmentId] ON [Visits] ([AppointmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_DoctorId] ON [Visits] ([DoctorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_FeePaidOn] ON [Visits] ([FeePaidOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_FollowUpOn] ON [Visits] ([FollowUpOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_PatientId] ON [Visits] ([PatientId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Visits_ScheduledOn] ON [Visits] ([ScheduledOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Visits_TenantId_VisitNo] ON [Visits] ([TenantId], [VisitNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824171118_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824171118_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826021158_FixProductSearchKeyIndexFilter'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826021158_FixProductSearchKeyIndexFilter', N'10.0.11');
END;

COMMIT;
GO

