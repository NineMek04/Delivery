using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendApi.Migrations
{
    /// <inheritdoc />
    public partial class Phase3EnterpriseSpatialScaling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""RiderLocationHistories"" CASCADE;

                CREATE TABLE ""RiderLocationHistories"" (
                    ""Id"" text NOT NULL,
                    ""RiderId"" text NOT NULL,
                    ""Location"" geometry(Point, 4326) NOT NULL,
                    ""RecordedAt"" timestamp with time zone NOT NULL,
                    ""RecordedFromIp"" text,
                    ""OrderId"" text,
                    CONSTRAINT ""PK_RiderLocationHistories"" PRIMARY KEY (""Id"", ""RecordedAt"")
                ) PARTITION BY RANGE (""RecordedAt"");

                CREATE INDEX ""IX_RiderLocationHistories_Location_Gist"" ON ""RiderLocationHistories"" USING gist (""Location"");
                CREATE INDEX ""IX_RiderLocationHistories_RiderId_RecordedAt"" ON ""RiderLocationHistories"" (""RiderId"", ""RecordedAt"" DESC);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS ""RiderLocationHistories"" CASCADE;

                CREATE TABLE ""RiderLocationHistories"" (
                    ""Id"" text NOT NULL,
                    ""RiderId"" text NOT NULL,
                    ""Location"" geometry(Point, 4326) NOT NULL,
                    ""RecordedAt"" timestamp with time zone NOT NULL,
                    ""RecordedFromIp"" text,
                    ""OrderId"" text,
                    CONSTRAINT ""PK_RiderLocationHistories"" PRIMARY KEY (""Id"")
                );
            ");
        }
    }
}
