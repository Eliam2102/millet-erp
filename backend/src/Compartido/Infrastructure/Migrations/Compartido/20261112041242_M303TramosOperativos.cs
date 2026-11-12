using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class M303TramosOperativos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tramos_operativos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    detenido = table.Column<bool>(type: "boolean", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    detenido_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detenido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    incidencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ultimo_evento_correcto_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_evento_correcto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    reanudado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reanudado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_reanudacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tramos_operativos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tramos_operativos_clave",
                schema: "compartido",
                table: "tramos_operativos",
                column: "clave",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tramos_operativos",
                schema: "compartido");
        }
    }
}
