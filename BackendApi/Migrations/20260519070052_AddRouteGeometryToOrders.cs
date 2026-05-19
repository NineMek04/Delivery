using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteGeometryToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncodedPolyline",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RouteDistanceMeters",
                table: "Orders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RouteDurationSeconds",
                table: "Orders",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncodedPolyline",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RouteDistanceMeters",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RouteDurationSeconds",
                table: "Orders");
        }
    }
}
