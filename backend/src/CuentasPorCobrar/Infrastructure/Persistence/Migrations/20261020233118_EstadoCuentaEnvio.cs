using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EstadoCuentaEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estado_cuenta_envio",
                schema: "cuentas_por_cobrar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destinatario = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    mensaje = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    totales_json = table.Column<string>(type: "jsonb", nullable: false),
                    numero_filas = table.Column<int>(type: "integer", nullable: false),
                    pdf_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    detalle_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estado_cuenta_envio", x => x.id);
                    table.CheckConstraint("ck_estado_cuenta_envio_estado", "estado IN (1, 2, 3)");
                    table.CheckConstraint("ck_estado_cuenta_envio_tamano", "tamano_bytes > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_estado_cuenta_envio_cliente_fecha",
                schema: "cuentas_por_cobrar",
                table: "estado_cuenta_envio",
                columns: new[] { "cliente_id", "fecha" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estado_cuenta_envio",
                schema: "cuentas_por_cobrar");
        }
    }
}
