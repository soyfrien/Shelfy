using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Dismissed",
                table: "DetectedSpines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TextKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UseCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCorrections_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCorrections_BookId",
                table: "UserCorrections",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCorrections_TextKey",
                table: "UserCorrections",
                column: "TextKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCorrections");

            migrationBuilder.DropColumn(
                name: "Dismissed",
                table: "DetectedSpines");
        }
    }
}
