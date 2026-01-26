using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoService.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySQLSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    original_file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stored_file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_extension = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    mime_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    width = table.Column<int>(type: "int", nullable: false),
                    height = table.Column<int>(type: "int", nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    is_primary = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    PrivacyLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BlurredFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BlurIntensity = table.Column<double>(type: "double", nullable: false),
                    RequiresMatch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: true),
                    is_deleted = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: true),
                    moderation_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "AUTO_APPROVED")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SafetyScore = table.Column<double>(type: "double", nullable: true),
                    moderation_results = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModeratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    moderation_notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quality_score = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    metadata = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tags = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photos", x => x.id);
                    table.CheckConstraint("ck_photos_dimensions_positive", "width > 0 AND height > 0");
                    table.CheckConstraint("ck_photos_display_order_positive", "display_order > 0");
                    table.CheckConstraint("ck_photos_file_size_positive", "file_size_bytes > 0");
                    table.CheckConstraint("ck_photos_quality_score_range", "quality_score >= 0 AND quality_score <= 100");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "photo_moderation_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    photo_id = table.Column<int>(type: "int", nullable: false),
                    previous_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    new_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    moderator_id = table.Column<int>(type: "int", nullable: true),
                    reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_moderation_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_photo_moderation_logs_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "photo_processing_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    photo_id = table.Column<int>(type: "int", nullable: false),
                    job_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "PENDING")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    parameters = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    result = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error_message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_photo_processing_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_photo_processing_jobs_photo_id",
                        column: x => x.photo_id,
                        principalTable: "photos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_photo_moderation_logs_created_at",
                table: "photo_moderation_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_photo_moderation_logs_moderator_id",
                table: "photo_moderation_logs",
                column: "moderator_id");

            migrationBuilder.CreateIndex(
                name: "ix_photo_moderation_logs_photo_id",
                table: "photo_moderation_logs",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_photo_processing_jobs_created_at",
                table: "photo_processing_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_photo_processing_jobs_photo_id",
                table: "photo_processing_jobs",
                column: "photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_photo_processing_jobs_status",
                table: "photo_processing_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_photos_content_hash",
                table: "photos",
                column: "content_hash");

            migrationBuilder.CreateIndex(
                name: "ix_photos_metadata",
                table: "photos",
                column: "metadata");

            migrationBuilder.CreateIndex(
                name: "ix_photos_moderation_status",
                table: "photos",
                column: "moderation_status");

            migrationBuilder.CreateIndex(
                name: "ix_photos_tags",
                table: "photos",
                column: "tags");

            migrationBuilder.CreateIndex(
                name: "ix_photos_user_active_display_order",
                table: "photos",
                columns: new[] { "user_id", "is_deleted", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_user_id",
                table: "photos",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_photos_user_primary_active",
                table: "photos",
                columns: new[] { "user_id", "is_primary", "is_deleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "photo_moderation_logs");

            migrationBuilder.DropTable(
                name: "photo_processing_jobs");

            migrationBuilder.DropTable(
                name: "photos");
        }
    }
}
