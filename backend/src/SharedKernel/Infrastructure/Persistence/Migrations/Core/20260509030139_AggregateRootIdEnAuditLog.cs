using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.SharedKernel.Infrastructure.Persistence.Migrations.Core
{
    /// <inheritdoc />
    public partial class AggregateRootIdEnAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "aggregate_root_id",
                schema: "core",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_modulo_aggregate_root_id_timestamp",
                schema: "core",
                table: "audit_log",
                columns: new[] { "modulo", "aggregate_root_id", "timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_log_modulo_aggregate_root_id_timestamp",
                schema: "core",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "aggregate_root_id",
                schema: "core",
                table: "audit_log");
        }
    }
}
