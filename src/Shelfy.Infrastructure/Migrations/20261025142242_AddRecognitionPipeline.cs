using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecognitionPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecognitionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShelfId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PhotoSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BooksAdded = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecognitionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecognitionJobs_Shelves_ShelfId",
                        column: x => x.ShelfId,
                        principalTable: "Shelves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DetectedSpines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecognitionJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    RawText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    CandidateTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CandidateAuthor = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CandidateIsbn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedIsbn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MatchScore = table.Column<double>(type: "double precision", nullable: true),
                    MatchSource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetectedSpines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DetectedSpines_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DetectedSpines_RecognitionJobs_RecognitionJobId",
                        column: x => x.RecognitionJobId,
                        principalTable: "RecognitionJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetectedSpines_BookId",
                table: "DetectedSpines",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_DetectedSpines_RecognitionJobId",
                table: "DetectedSpines",
                column: "RecognitionJobId");

            migrationBuilder.CreateIndex(
                name: "IX_RecognitionJobs_ShelfId_PhotoSha256",
                table: "RecognitionJobs",
                columns: new[] { "ShelfId", "PhotoSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_RecognitionJobs_Status_NextAttemptAt",
                table: "RecognitionJobs",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DetectedSpines");

            migrationBuilder.DropTable(
                name: "RecognitionJobs");
        }
    }
}
