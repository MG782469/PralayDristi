using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PralayDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dams",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    river = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    height_m = table.Column<double>(type: "double precision", nullable: false),
                    gross_storage_mcm = table.Column<double>(type: "double precision", nullable: false),
                    crest_length_m = table.Column<double>(type: "double precision", nullable: false),
                    crest_width_m = table.Column<double>(type: "double precision", nullable: false),
                    downstream_km = table.Column<double>(type: "double precision", nullable: true),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approximate = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cascade_with = table.Column<string>(type: "jsonb", nullable: false),
                    dykes = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dams", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "simulation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    progress = table.Column<double>(type: "double precision", nullable: false),
                    stage = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    spec_json = table.Column<string>(type: "jsonb", nullable: false),
                    summary_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finished_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dam_id = table.Column<int>(type: "integer", nullable: true),
                    lake_detection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulation_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_simulation_runs_dams_dam_id",
                        column: x => x.dam_id,
                        principalTable: "dams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_simulation_runs_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    population_at_risk = table.Column<int>(type: "integer", nullable: false),
                    first_arrival_min = table.Column<double>(type: "double precision", nullable: true),
                    cap_xml = table.Column<string>(type: "text", nullable: false),
                    acknowledged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerts_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "simulation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lake_detections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    detected_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lon = table.Column<double>(type: "double precision", nullable: false),
                    area_km2 = table.Column<double>(type: "double precision", nullable: false),
                    previous_area_km2 = table.Column<double>(type: "double precision", nullable: true),
                    polygon = table.Column<string>(type: "jsonb", nullable: true),
                    source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    estimated_volume_mcm = table.Column<double>(type: "double precision", nullable: true),
                    estimated_level = table.Column<double>(type: "double precision", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lake_detections", x => x.id);
                    table.ForeignKey(
                        name: "fk_lake_detections_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "simulation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_created_utc",
                table: "alerts",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_run_id",
                table: "alerts",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_dams_name",
                table: "dams",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lake_detections_detected_utc",
                table: "lake_detections",
                column: "detected_utc");

            migrationBuilder.CreateIndex(
                name: "ix_lake_detections_run_id",
                table: "lake_detections",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_simulation_runs_created_by_user_id",
                table: "simulation_runs",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_simulation_runs_created_utc",
                table: "simulation_runs",
                column: "created_utc");

            migrationBuilder.CreateIndex(
                name: "ix_simulation_runs_dam_id",
                table: "simulation_runs",
                column: "dam_id");

            migrationBuilder.CreateIndex(
                name: "ix_simulation_runs_origin_status",
                table: "simulation_runs",
                columns: new[] { "origin", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "lake_detections");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "simulation_runs");

            migrationBuilder.DropTable(
                name: "dams");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
