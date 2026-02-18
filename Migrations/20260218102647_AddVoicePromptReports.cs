using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoService.Migrations
{
    /// <inheritdoc />
    public partial class AddVoicePromptReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voice_prompt_reports",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    voice_prompt_id = table.Column<int>(type: "int", nullable: false),
                    reporter_user_id = table.Column<int>(type: "int", nullable: false),
                    target_user_id = table.Column<int>(type: "int", nullable: false),
                    reason = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "pending")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voice_prompt_reports", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "voice_prompts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    stored_file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    duration_seconds = table.Column<double>(type: "double", nullable: false),
                    mime_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    moderation_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "AUTO_APPROVED")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    transcript_text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: true),
                    is_deleted = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voice_prompts", x => x.id);
                    table.CheckConstraint("ck_voice_prompts_duration_range", "duration_seconds >= 3 AND duration_seconds <= 30");
                    table.CheckConstraint("ck_voice_prompts_file_size_positive", "file_size_bytes > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_photos_moderation_queue",
                table: "photos",
                columns: new[] { "moderation_status", "is_deleted", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_user_ordering",
                table: "photos",
                columns: new[] { "user_id", "is_deleted", "is_primary", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_voice_prompt_reports_status",
                table: "voice_prompt_reports",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_voice_prompt_reports_voice_prompt_id_reporter_user_id",
                table: "voice_prompt_reports",
                columns: new[] { "voice_prompt_id", "reporter_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voice_prompts_moderation_status",
                table: "voice_prompts",
                column: "moderation_status");

            migrationBuilder.CreateIndex(
                name: "ix_voice_prompts_user_active",
                table: "voice_prompts",
                columns: new[] { "user_id", "is_deleted" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voice_prompt_reports");

            migrationBuilder.DropTable(
                name: "voice_prompts");

            migrationBuilder.DropIndex(
                name: "ix_photos_moderation_queue",
                table: "photos");

            migrationBuilder.DropIndex(
                name: "ix_photos_user_ordering",
                table: "photos");
        }
    }
}
