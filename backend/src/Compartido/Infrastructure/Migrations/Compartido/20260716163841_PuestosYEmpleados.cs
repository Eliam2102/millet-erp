using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class PuestosYEmpleados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "puestos",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_puestos", x => x.id);
                    table.CheckConstraint("ck_puestos_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "empleados",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    puesto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    jefe_directo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    departamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    codigo_nomina = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    estatus = table.Column<short>(type: "smallint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empleados", x => x.id);
                    table.CheckConstraint("ck_empleados_estatus", "estatus BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "fk_empleados_departamentos_departamento_id",
                        column: x => x.departamento_id,
                        principalSchema: "compartido",
                        principalTable: "departamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empleados_empleados_jefe_directo_id",
                        column: x => x.jefe_directo_id,
                        principalSchema: "compartido",
                        principalTable: "empleados",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empleados_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalSchema: "compartido",
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empleados_puestos_puesto_id",
                        column: x => x.puesto_id,
                        principalSchema: "compartido",
                        principalTable: "puestos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_empleados_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalSchema: "compartido",
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "puestos",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "estatus", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "EJEC", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Ejecutivo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "GER", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Gerente", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "OPER", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Operativo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_empleados_departamento_id",
                schema: "compartido",
                table: "empleados",
                column: "departamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_empresa_id_clave",
                schema: "compartido",
                table: "empleados",
                columns: new[] { "empresa_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_empleados_estatus",
                schema: "compartido",
                table: "empleados",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_jefe_directo_id",
                schema: "compartido",
                table: "empleados",
                column: "jefe_directo_id");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_puesto_id",
                schema: "compartido",
                table: "empleados",
                column: "puesto_id");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_sucursal_id",
                schema: "compartido",
                table: "empleados",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_empleados_usuario_id",
                schema: "compartido",
                table: "empleados",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_puestos_clave",
                schema: "compartido",
                table: "puestos",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_puestos_estatus",
                schema: "compartido",
                table: "puestos",
                column: "estatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "empleados",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "puestos",
                schema: "compartido");
        }
    }
}
