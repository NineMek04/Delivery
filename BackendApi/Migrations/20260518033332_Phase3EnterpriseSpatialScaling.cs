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
            // ── Indexes สำหรับตารางที่ไม่ถูก Partition (สร้างผ่าน EF Core ได้ปกติ) ──

            migrationBuilder.CreateIndex(
                name: "IX_Riders_CurrentLocation_Gist",
                table: "Riders",
                column: "CurrentLocation")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AssignedRiderId",
                table: "Orders",
                column: "AssignedRiderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DropoffLocation_Gist",
                table: "Orders",
                column: "DropoffLocation")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PickupLocation_Gist",
                table: "Orders",
                column: "PickupLocation")
                .Annotation("Npgsql:IndexMethod", "gist");

            // ── Raw SQL: Clustering + Partitioning สำหรับ RiderLocationHistories ──
            // หมายเหตุ: ไม่สร้าง IX_RiderLocationHistories_* ผ่าน EF Core API เพราะตารางนี้
            // จะถูก drop และสร้างใหม่เป็น Partitioned Table ใน Raw SQL ด้านล่าง
            // Index ทั้งหมดของตารางนี้จึงถูกสร้างผ่าน Raw SQL แทน เพื่อให้ถูกต้องบน Partitioned Table

            migrationBuilder.Sql(@"
-- 1. Physical Clustering (ทำก่อน Partitioning ขณะที่ Index พร้อมแล้ว)
CLUSTER ""Riders"" USING ""IX_Riders_CurrentLocation_Gist"";
CLUSTER ""Orders"" USING ""IX_Orders_PickupLocation_Gist"";

-- 2. Drop Index เดิมบน RiderLocationHistories ก่อน Rename
--    (Index จะถูกลบพร้อมตารางอยู่แล้ว แต่ Drop ชัดเจนเพื่อป้องกัน name conflict)
DROP INDEX IF EXISTS ""IX_RiderLocationHistories_RiderId_RecordedAt"";

-- 3. Rename ตารางเดิม
ALTER TABLE ""RiderLocationHistories"" RENAME TO ""RiderLocationHistories_old"";
ALTER TABLE ""RiderLocationHistories_old"" DROP CONSTRAINT ""PK_RiderLocationHistories"";

-- 4. สร้าง Partitioned Table ใหม่ (PARTITION BY RANGE บน RecordedAt)
CREATE TABLE ""RiderLocationHistories"" (
    ""Id"" text NOT NULL,
    ""RiderId"" text NOT NULL,
    ""Location"" geometry(Point, 4326) NOT NULL,
    ""RecordedAt"" timestamp with time zone NOT NULL,
    ""RecordedFromIp"" text,
    ""OrderId"" text,
    CONSTRAINT ""PK_RiderLocationHistories"" PRIMARY KEY (""Id"", ""RecordedAt"")
) PARTITION BY RANGE (""RecordedAt"");

-- 5. สร้าง Partition ล่วงหน้า 3 เดือน
CREATE TABLE ""RiderLocationHistories_2026_05""
    PARTITION OF ""RiderLocationHistories""
    FOR VALUES FROM ('2026-05-01') TO ('2026-06-01');

CREATE TABLE ""RiderLocationHistories_2026_06""
    PARTITION OF ""RiderLocationHistories""
    FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');

CREATE TABLE ""RiderLocationHistories_2026_07""
    PARTITION OF ""RiderLocationHistories""
    FOR VALUES FROM ('2026-07-01') TO ('2026-08-01');

-- 6. ย้ายข้อมูลเดิม (ถ้ามี) ไปยัง Partitioned Table
INSERT INTO ""RiderLocationHistories"" SELECT * FROM ""RiderLocationHistories_old"";

-- 7. ลบตารางเดิม
DROP TABLE ""RiderLocationHistories_old"";

-- 8. สร้าง GiST Index บน Partitioned Table (กระจายไป Partition ย่อยอัตโนมัติ)
CREATE INDEX ""IX_RiderLocationHistories_Location_Gist""
    ON ""RiderLocationHistories"" USING gist (""Location"");

-- 9. สร้าง Composite B-tree Index (RiderId + RecordedAt) บน Partitioned Table
CREATE INDEX ""IX_RiderLocationHistories_RiderId_RecordedAt""
    ON ""RiderLocationHistories"" (""RiderId"", ""RecordedAt"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Rollback of table partitioning is not supported. " +
                "Restore from backup if needed.");
        }
    }
}
