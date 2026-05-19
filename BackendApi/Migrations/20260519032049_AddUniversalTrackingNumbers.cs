using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUniversalTrackingNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RefNumber",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "RefNumber",
                table: "Shops",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "RefNumber",
                table: "Riders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<long>(
                name: "RefNumber",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RefNumber",
                table: "Users",
                column: "RefNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shops_RefNumber",
                table: "Shops",
                column: "RefNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Riders_RefNumber",
                table: "Riders",
                column: "RefNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RefNumber",
                table: "Orders",
                column: "RefNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_RefNumber",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Shops_RefNumber",
                table: "Shops");

            migrationBuilder.DropIndex(
                name: "IX_Riders_RefNumber",
                table: "Riders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RefNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RefNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefNumber",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "RefNumber",
                table: "Riders");

            migrationBuilder.DropColumn(
                name: "RefNumber",
                table: "Orders");
        }
    }
}
