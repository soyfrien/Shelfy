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

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    ALTER TABLE "Users" ADD "MailingAddress" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetRoles" (
        "Id" uuid NOT NULL,
        "Name" character varying(256),
        "NormalizedName" character varying(256),
        "ConcurrencyStamp" text,
        CONSTRAINT "PK_AspNetRoles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetUsers" (
        "Id" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UserName" character varying(256),
        "NormalizedUserName" character varying(256),
        "Email" character varying(256),
        "NormalizedEmail" character varying(256),
        "EmailConfirmed" boolean NOT NULL,
        "PasswordHash" text,
        "SecurityStamp" text,
        "ConcurrencyStamp" text,
        "PhoneNumber" text,
        "PhoneNumberConfirmed" boolean NOT NULL,
        "TwoFactorEnabled" boolean NOT NULL,
        "LockoutEnd" timestamp with time zone,
        "LockoutEnabled" boolean NOT NULL,
        "AccessFailedCount" integer NOT NULL,
        CONSTRAINT "PK_AspNetUsers" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AspNetUsers_Users_Id" FOREIGN KEY ("Id") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "Copies" (
        "Id" uuid NOT NULL,
        "Code" character varying(8) NOT NULL,
        "BookId" uuid NOT NULL,
        "OwnerId" uuid NOT NULL,
        "HomeShelfId" uuid,
        "CurrentHolderId" uuid NOT NULL,
        "CurrentShelfId" uuid,
        "Status" character varying(20) NOT NULL,
        "AllowPassAlong" boolean NOT NULL,
        "Condition" character varying(100),
        "Notes" character varying(1000),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "Version" integer NOT NULL,
        CONSTRAINT "PK_Copies" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Copies_Books_BookId" FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Copies_Shelves_CurrentShelfId" FOREIGN KEY ("CurrentShelfId") REFERENCES "Shelves" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Copies_Shelves_HomeShelfId" FOREIGN KEY ("HomeShelfId") REFERENCES "Shelves" ("Id") ON DELETE SET NULL,
        CONSTRAINT "FK_Copies_Users_CurrentHolderId" FOREIGN KEY ("CurrentHolderId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Copies_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetRoleClaims" (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY,
        "RoleId" uuid NOT NULL,
        "ClaimType" text,
        "ClaimValue" text,
        CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetUserClaims" (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY,
        "UserId" uuid NOT NULL,
        "ClaimType" text,
        "ClaimValue" text,
        CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetUserLogins" (
        "LoginProvider" text NOT NULL,
        "ProviderKey" text NOT NULL,
        "ProviderDisplayName" text,
        "UserId" uuid NOT NULL,
        CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
        CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetUserRoles" (
        "UserId" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
        CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "AspNetUserTokens" (
        "UserId" uuid NOT NULL,
        "LoginProvider" text NOT NULL,
        "Name" text NOT NULL,
        "Value" text,
        CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
        CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "Loans" (
        "Id" uuid NOT NULL,
        "CopyId" uuid NOT NULL,
        "OwnerId" uuid NOT NULL,
        "BorrowerId" uuid NOT NULL,
        "SenderId" uuid,
        "PreviousLoanId" uuid,
        "Status" character varying(20) NOT NULL,
        "Message" character varying(500),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ApprovedAt" timestamp with time zone,
        "MailedAt" timestamp with time zone,
        "ReceivedAt" timestamp with time zone,
        "ReturnRequestedAt" timestamp with time zone,
        "ReturnMailedAt" timestamp with time zone,
        "ClosedAt" timestamp with time zone,
        CONSTRAINT "PK_Loans" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_Loans_Copies_CopyId" FOREIGN KEY ("CopyId") REFERENCES "Copies" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_Loans_Users_BorrowerId" FOREIGN KEY ("BorrowerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
        CONSTRAINT "FK_Loans_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE TABLE "CopyMovements" (
        "Id" uuid NOT NULL,
        "CopyId" uuid NOT NULL,
        "LoanId" uuid,
        "Kind" character varying(20) NOT NULL,
        "FromUserId" uuid,
        "FromShelfId" uuid,
        "ToUserId" uuid NOT NULL,
        "ToShelfId" uuid,
        "StartedAt" timestamp with time zone NOT NULL,
        "ReceivedAt" timestamp with time zone,
        CONSTRAINT "PK_CopyMovements" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CopyMovements_Copies_CopyId" FOREIGN KEY ("CopyId") REFERENCES "Copies" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_CopyMovements_Loans_LoanId" FOREIGN KEY ("LoanId") REFERENCES "Loans" ("Id") ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Copies_BookId" ON "Copies" ("BookId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE UNIQUE INDEX "IX_Copies_Code" ON "Copies" ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Copies_CurrentHolderId" ON "Copies" ("CurrentHolderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Copies_CurrentShelfId" ON "Copies" ("CurrentShelfId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Copies_HomeShelfId" ON "Copies" ("HomeShelfId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Copies_OwnerId" ON "Copies" ("OwnerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_CopyMovements_CopyId_StartedAt" ON "CopyMovements" ("CopyId", "StartedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_CopyMovements_LoanId" ON "CopyMovements" ("LoanId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Loans_BorrowerId" ON "Loans" ("BorrowerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Loans_CopyId_Status" ON "Loans" ("CopyId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    CREATE INDEX "IX_Loans_OwnerId" ON "Loans" ("OwnerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261116000522_AddAccountsAndLending') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261116000522_AddAccountsAndLending', '8.0.32');
    END IF;
END $EF$;
COMMIT;

