using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "doctors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    full_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    specialty = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doctors", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password_hash = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    role = table.Column<sbyte>(type: "tinyint", nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "schedule_slots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    doctor_id = table.Column<long>(type: "bigint", nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    booked_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    status = table.Column<sbyte>(type: "tinyint", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    end_time_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    start_time_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_slots", x => x.id);
                    table.CheckConstraint("ck_slot_booked", "booked_count >= 0 AND booked_count <= capacity");
                    table.CheckConstraint("ck_slot_capacity", "capacity >= 1");
                    table.CheckConstraint("ck_slot_time_order", "start_time_utc < end_time_utc");
                    table.ForeignKey(
                        name: "fk_slot_doctor",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "patients",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    full_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patients", x => x.id);
                    table.ForeignKey(
                        name: "fk_patients_user",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    slot_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<sbyte>(type: "tinyint", nullable: false),
                    booked_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    cancelled_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    active_patient_id = table.Column<long>(type: "bigint", nullable: true, computedColumnSql: "IF(status = 0, patient_id, NULL)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_appt_patient",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appt_slot",
                        column: x => x.slot_id,
                        principalTable: "schedule_slots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_appt_patient_status",
                table: "appointments",
                columns: new[] { "patient_id", "status", "booked_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_appt_slot",
                table: "appointments",
                column: "slot_id");

            migrationBuilder.CreateIndex(
                name: "ux_appt_slot_active_patient",
                table: "appointments",
                columns: new[] { "slot_id", "active_patient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctors_specialty",
                table: "doctors",
                column: "specialty");

            migrationBuilder.CreateIndex(
                name: "ux_patients_user",
                table: "patients",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_slots_doctor_id",
                table: "schedule_slots",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);

            // ux_slot_doctor_start / ix_slot_doctor_time 混合 doctor_id 與 TimeSlot（Complex Type）的
            // start_time_utc，EF Core 9 不支援用 Fluent API 對 Complex Type 屬性建 composite index
            // （見 ScheduleSlotConfiguration 內的說明），故改用原始 SQL 直接建立。因為不屬於 EF 模型，
            // 之後 `dotnet ef migrations add` 的 model diff 不會知道這兩個 index 存在，需手動維護。
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX `ux_slot_doctor_start` ON `schedule_slots` (`doctor_id`, `start_time_utc`);");

            migrationBuilder.Sql(
                "CREATE INDEX `ix_slot_doctor_time` ON `schedule_slots` (`doctor_id`, `start_time_utc`, `status`);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX `ix_slot_doctor_time` ON `schedule_slots`;");
            migrationBuilder.Sql("DROP INDEX `ux_slot_doctor_start` ON `schedule_slots`;");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "patients");

            migrationBuilder.DropTable(
                name: "schedule_slots");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "doctors");
        }
    }
}
