using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "RecognitionJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TileCount",
                table: "RecognitionJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TileIndex",
                table: "RecognitionJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RecognitionJobs_ShelfId_BatchId",
                table: "RecognitionJobs",
                columns: new[] { "ShelfId", "BatchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecognitionJobs_ShelfId_BatchId",
                table: "RecognitionJobs");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "RecognitionJobs");

            migrationBuilder.DropColumn(
                name: "TileCount",
                table: "RecognitionJobs");

            migrationBuilder.DropColumn(
                name: "TileIndex",
                table: "RecognitionJobs");
        }
    }
}
