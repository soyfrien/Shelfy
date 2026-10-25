using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfHidden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "Shelves",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Shelves",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "Shelves");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Shelves");
        }
    }
}
