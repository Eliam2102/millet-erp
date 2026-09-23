using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Identidad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AltaUnificadaEstadoAccesoUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_cuenta_tecnica",
                schema: "identidad",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "estado_acceso",
                schema: "identidad",
                table: "usuarios",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "motivo_error_provision",
                schema: "identidad",
                table: "usuarios",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "primer_acceso_en",
                schema: "identidad",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill: los usuarios con OID provisional (pending:* o el
            // placeholder histórico dev-{email}) quedan PendientePrimerAcceso
            // (1); el resto conserva Activo (0). "dev-superadmin" y demás
            // OIDs sintéticos de ADR-0015 no traen '@' y siguen Activo.
            migrationBuilder.Sql(@"
                UPDATE identidad.usuarios
                SET estado_acceso = 1
                WHERE entra_oid LIKE 'pending:%'
                   OR (entra_oid LIKE 'dev-%' AND position('@' in entra_oid) > 0);
            ");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_estado_acceso",
                schema: "identidad",
                table: "usuarios",
                column: "estado_acceso");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_estado_acceso",
                schema: "identidad",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "es_cuenta_tecnica",
                schema: "identidad",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "estado_acceso",
                schema: "identidad",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "motivo_error_provision",
                schema: "identidad",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "primer_acceso_en",
                schema: "identidad",
                table: "usuarios");
        }
    }
}
