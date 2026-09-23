using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class AltaUnificadaEmpleadoUsuarioUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_empleados_usuario_id",
                schema: "compartido",
                table: "empleados");

            migrationBuilder.AddColumn<string>(
                name: "email_contacto",
                schema: "compartido",
                table: "empleados",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            // Prechequeo: Empleado → Usuario pasa a 1:0..1. Si hay usuarios
            // vinculados a más de un empleado se aborta con un mensaje que
            // los enumera; se corrigen a mano (reporte de reconciliación,
            // categoría G del plan 15) antes de reintentar.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE duplicados text;
                BEGIN
                    SELECT string_agg(usuario_id::text || ' (' || n || ' empleados)', ', ')
                    INTO duplicados
                    FROM (
                        SELECT usuario_id, count(*) AS n
                        FROM compartido.empleados
                        WHERE usuario_id IS NOT NULL
                        GROUP BY usuario_id
                        HAVING count(*) > 1
                    ) d;

                    IF duplicados IS NOT NULL THEN
                        RAISE EXCEPTION 'No se puede crear el índice único ix_empleados_usuario_id: usuarios vinculados a más de un empleado: %', duplicados;
                    END IF;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_usuario_id",
                schema: "compartido",
                table: "empleados",
                column: "usuario_id",
                unique: true,
                filter: "usuario_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_empleados_usuario_id",
                schema: "compartido",
                table: "empleados");

            migrationBuilder.DropColumn(
                name: "email_contacto",
                schema: "compartido",
                table: "empleados");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_usuario_id",
                schema: "compartido",
                table: "empleados",
                column: "usuario_id");
        }
    }
}
