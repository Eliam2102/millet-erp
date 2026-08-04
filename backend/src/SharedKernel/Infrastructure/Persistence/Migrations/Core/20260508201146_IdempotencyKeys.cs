using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class IdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "core",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    request_body_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    response_body_truncated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.empresa_id, x.usuario_id, x.key });
                    table.CheckConstraint("ck_idempotency_keys_status", "status IN ('processing', 'completed', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_created_at",
                schema: "core",
                table: "idempotency_keys",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_status_created_at",
                schema: "core",
                table: "idempotency_keys",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "core");
        }
    }
}
