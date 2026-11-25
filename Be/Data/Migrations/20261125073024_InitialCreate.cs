using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Be.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AdminSubject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdminPlayerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MappingVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Config_CrosshairSize = table.Column<double>(type: "double precision", nullable: false),
                    Config_EliminationVisibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Config_FireWindowMs = table.Column<int>(type: "integer", nullable: false),
                    Config_FriendlyFire = table.Column<bool>(type: "boolean", nullable: false),
                    Config_MatchDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Config_MaxTargetsPerShot = table.Column<int>(type: "integer", nullable: false),
                    Config_RespawnMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Config_RespawnSeconds = table.Column<int>(type: "integer", nullable: false),
                    Config_SpawnProtectionSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Eliminations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<string>(type: "character varying(64)", nullable: false),
                    ShotId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ShooterId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ShooterTeamId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetTeamId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespawnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FriendlyFire = table.Column<bool>(type: "boolean", nullable: false),
                    CountsForScore = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eliminations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Eliminations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShotRecords",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(64)", nullable: false),
                    ShooterId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ShotId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CountsForRateLimit = table.Column<bool>(type: "boolean", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShotRecords", x => new { x.SessionId, x.ShooterId, x.ShotId });
                    table.ForeignKey(
                        name: "FK_ShotRecords_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpawnPoints",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", nullable: false),
                    MarkerId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TeamId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpawnPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpawnPoints_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(64)", nullable: false),
                    TeamId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => new { x.SessionId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_Teams_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TeamId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MarkerId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespawnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProtectedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeviceLinkId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Players_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Players_Teams_SessionId_TeamId",
                        columns: x => new { x.SessionId, x.TeamId },
                        principalTable: "Teams",
                        principalColumns: new[] { "SessionId", "TeamId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Eliminations_SessionId_CountsForScore_ShooterTeamId",
                table: "Eliminations",
                columns: new[] { "SessionId", "CountsForScore", "ShooterTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Eliminations_SessionId_ShooterId_ShotId_TargetId",
                table: "Eliminations",
                columns: new[] { "SessionId", "ShooterId", "ShotId", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_SessionId_MarkerId",
                table: "Players",
                columns: new[] { "SessionId", "MarkerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_SessionId_Nickname",
                table: "Players",
                columns: new[] { "SessionId", "Nickname" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_SessionId_TeamId",
                table: "Players",
                columns: new[] { "SessionId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Status_RespawnAt",
                table: "Players",
                columns: new[] { "Status", "RespawnAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Code",
                table: "Sessions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status",
                table: "Sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ShotRecords_SessionId_ShooterId_ReceivedAt",
                table: "ShotRecords",
                columns: new[] { "SessionId", "ShooterId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpawnPoints_SessionId_MarkerId",
                table: "SpawnPoints",
                columns: new[] { "SessionId", "MarkerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Eliminations");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "ShotRecords");

            migrationBuilder.DropTable(
                name: "SpawnPoints");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
