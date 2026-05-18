using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace BackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddShopEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RiderLocationHistories_Location_Gist",
                table: "RiderLocationHistories");

            migrationBuilder.DropIndex(
                name: "IX_RiderLocationHistories_RiderId_RecordedAt",
                table: "RiderLocationHistories");

            migrationBuilder.CreateTable(
                name: "Shops",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MenuName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MenuPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Location = table.Column<Point>(type: "geometry(Point, 4326)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "text", nullable: true),
                    CreatedFromIp = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "text", nullable: true),
                    UpdatedFromIp = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedByName = table.Column<string>(type: "text", nullable: true),
                    DeletedFromIp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shops", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shops_Location_Gist",
                table: "Shops",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Shops");

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationHistories_Location_Gist",
                table: "RiderLocationHistories",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_RiderLocationHistories_RiderId_RecordedAt",
                table: "RiderLocationHistories",
                columns: new[] { "RiderId", "RecordedAt" });
        }
    }
}
