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

