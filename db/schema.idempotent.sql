CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE TABLE "Books" (
        "Id" uuid NOT NULL,
        "Isbn" character varying(20),
        "Title" character varying(500) NOT NULL,
        "Author" character varying(300) NOT NULL,
        "CoverUrl" character varying(2000),
        CONSTRAINT "PK_Books" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "IdentitySubject" character varying(200),
        "DisplayName" character varying(100) NOT NULL,
        "City" character varying(100) NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE TABLE "Shelves" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "IsPublic" boolean NOT NULL,
        CONSTRAINT "PK_Shelves" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Shelves_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE TABLE "ShelfBooks" (
        "ShelfId" uuid NOT NULL,
        "BookId" uuid NOT NULL,
        "Status" character varying(20) NOT NULL,
        "SalePrice" numeric(10,2),
        CONSTRAINT "PK_ShelfBooks" PRIMARY KEY ("ShelfId", "BookId"),
        CONSTRAINT "FK_ShelfBooks_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_ShelfBooks_Shelves_ShelfId" FOREIGN KEY ("ShelfId") REFERENCES "Shelves" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Books_Isbn" ON "Books" ("Isbn");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE INDEX "IX_ShelfBooks_BookId" ON "ShelfBooks" ("BookId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE INDEX "IX_Shelves_UserId" ON "Shelves" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    CREATE UNIQUE INDEX "IX_Users_IdentitySubject" ON "Users" ("IdentitySubject");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025013301_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261025013301_InitialCreate', '8.0.32');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    CREATE TABLE "RecognitionJobs" (
        "Id" uuid NOT NULL,
        "ShelfId" uuid NOT NULL,
        "PhotoKey" character varying(300) NOT NULL,
        "ContentType" character varying(50) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "PhotoSha256" character varying(64) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "Attempts" integer NOT NULL,
        "MaxAttempts" integer NOT NULL,
        "NextAttemptAt" timestamp with time zone NOT NULL,
        "LockedUntil" timestamp with time zone,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "StartedAt" timestamp with time zone,
        "CompletedAt" timestamp with time zone,
        "Error" character varying(2000),
        "BooksAdded" integer NOT NULL,
        CONSTRAINT "PK_RecognitionJobs" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_RecognitionJobs_Shelves_ShelfId" FOREIGN KEY ("ShelfId") REFERENCES "Shelves" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    CREATE TABLE "DetectedSpines" (
        "Id" uuid NOT NULL,
        "RecognitionJobId" uuid NOT NULL,
        "Position" integer NOT NULL,
        "RawText" character varying(1000) NOT NULL,
        "Confidence" double precision,
        "CandidateTitle" character varying(500) NOT NULL,
        "CandidateAuthor" character varying(300),
        "CandidateIsbn" character varying(20),
        "Status" character varying(20) NOT NULL,
        "BookId" uuid,
        "ResolvedIsbn" character varying(20),
        "MatchScore" double precision,
        "MatchSource" character varying(30),
        CONSTRAINT "PK_DetectedSpines" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_DetectedSpines_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_DetectedSpines_RecognitionJobs_RecognitionJobId" FOREIGN KEY ("RecognitionJobId") REFERENCES "RecognitionJobs" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    CREATE INDEX "IX_DetectedSpines_BookId" ON "DetectedSpines" ("BookId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    CREATE INDEX "IX_DetectedSpines_RecognitionJobId" ON "DetectedSpines" ("RecognitionJobId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    CREATE INDEX "IX_RecognitionJobs_ShelfId_PhotoSha256" ON "RecognitionJobs" ("ShelfId", "PhotoSha256");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    CREATE INDEX "IX_RecognitionJobs_Status_NextAttemptAt" ON "RecognitionJobs" ("Status", "NextAttemptAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025142242_AddRecognitionPipeline') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261025142242_AddRecognitionPipeline', '8.0.32');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025214348_AddUploadBatches') THEN
    ALTER TABLE "RecognitionJobs" ADD "BatchId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025214348_AddUploadBatches') THEN
    ALTER TABLE "RecognitionJobs" ADD "TileCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025214348_AddUploadBatches') THEN
    ALTER TABLE "RecognitionJobs" ADD "TileIndex" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025214348_AddUploadBatches') THEN
    CREATE INDEX "IX_RecognitionJobs_ShelfId_BatchId" ON "RecognitionJobs" ("ShelfId", "BatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025214348_AddUploadBatches') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261025214348_AddUploadBatches', '8.0.32');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025223825_AddShelfHidden') THEN
    ALTER TABLE "Shelves" ADD "HiddenAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025223825_AddShelfHidden') THEN
    ALTER TABLE "Shelves" ADD "IsHidden" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261025223825_AddShelfHidden') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261025223825_AddShelfHidden', '8.0.32');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026021852_AddUserCorrections') THEN
    ALTER TABLE "DetectedSpines" ADD "Dismissed" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026021852_AddUserCorrections') THEN
    CREATE TABLE "UserCorrections" (
        "Id" uuid NOT NULL,
        "TextKey" character varying(500) NOT NULL,
        "BookId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UseCount" integer NOT NULL,
        CONSTRAINT "PK_UserCorrections" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_UserCorrections_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026021852_AddUserCorrections') THEN
    CREATE INDEX "IX_UserCorrections_BookId" ON "UserCorrections" ("BookId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026021852_AddUserCorrections') THEN
    CREATE UNIQUE INDEX "IX_UserCorrections_TextKey" ON "UserCorrections" ("TextKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261026021852_AddUserCorrections') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261026021852_AddUserCorrections', '8.0.32');
    END IF;
END $EF$;
COMMIT;

