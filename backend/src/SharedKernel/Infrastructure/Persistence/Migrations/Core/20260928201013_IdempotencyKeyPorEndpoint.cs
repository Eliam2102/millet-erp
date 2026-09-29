using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class IdempotencyKeyPorEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "core",
                table: "idempotency_keys");

            migrationBuilder.AddColumn<string>(
                name: "response_headers",
                schema: "core",
                table: "idempotency_keys",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "core",
                table: "idempotency_keys",
                columns: new[] { "empresa_id", "usuario_id", "key", "http_method", "path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "core",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "response_headers",
                schema: "core",
                table: "idempotency_keys");

            migrationBuilder.AddPrimaryKey(
                name: "pk_idempotency_keys",
                schema: "core",
                table: "idempotency_keys",
                columns: new[] { "empresa_id", "usuario_id", "key" });
        }
    }
}
