using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class CatalogosOcSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "condiciones_pago",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dias_credito = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_condiciones_pago", x => x.id);
                    table.CheckConstraint("ck_condiciones_pago_dias", "dias_credito BETWEEN 0 AND 365");
                    table.CheckConstraint("ck_condiciones_pago_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "incoterms",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("pk_incoterms", x => x.id);
                    table.CheckConstraint("ck_incoterms_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "regimenes_fiscales",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    aplica_persona_fisica = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_regimenes_fiscales", x => x.id);
                    table.CheckConstraint("ck_regimenes_fiscales_codigo_3", "char_length(codigo) = 3");
                    table.CheckConstraint("ck_regimenes_fiscales_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.CreateTable(
                name: "transportistas",
                schema: "compartido",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telefono = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("pk_transportistas", x => x.id);
                    table.CheckConstraint("ck_transportistas_estatus", "estatus BETWEEN 0 AND 2");
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "condiciones_pago",
                columns: new[] { "id", "clave", "created_at", "created_by", "deleted_at", "dias_credito", "estatus", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0003-0000-0000-000000000001"), "CONTADO", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 0, (short)0, "Contado", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0003-0000-0000-000000000002"), "15D", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 15, (short)0, "15 días", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0003-0000-0000-000000000003"), "30D", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 30, (short)0, "30 días", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0003-0000-0000-000000000004"), "45D", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 45, (short)0, "45 días", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0003-0000-0000-000000000005"), "60D", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 60, (short)0, "60 días", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0003-0000-0000-000000000006"), "90D", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 90, (short)0, "90 días", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0003-0000-0000-000000000007"), "120D", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, 120, (short)0, "120 días", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "incoterms",
                columns: new[] { "id", "codigo", "created_at", "created_by", "deleted_at", "estatus", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0001-0000-0000-000000000001"), "EXW", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Ex Works", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000002"), "FCA", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Free Carrier", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000003"), "CPT", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Carriage Paid To", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000004"), "CIP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Carriage and Insurance Paid To", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000005"), "DAP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Delivered at Place", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000006"), "DPU", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Delivered at Place Unloaded", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000007"), "DDP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Delivered Duty Paid", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000008"), "FAS", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Free Alongside Ship", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000009"), "FOB", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Free on Board", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000010"), "CFR", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Cost and Freight", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0001-0000-0000-000000000011"), "CIF", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Cost, Insurance and Freight", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.InsertData(
                schema: "compartido",
                table: "regimenes_fiscales",
                columns: new[] { "id", "aplica_persona_fisica", "codigo", "created_at", "created_by", "deleted_at", "estatus", "nombre", "updated_at", "updated_by", "version" },
                values: new object[,]
                {
                    { new Guid("00000002-0002-0000-0000-000000000001"), false, "601", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "General de Ley Personas Morales", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000002"), false, "603", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Personas Morales con Fines no Lucrativos", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000003"), true, "605", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Sueldos y Salarios e Ingresos Asimilados a Salarios", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000004"), true, "606", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Arrendamiento", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000005"), true, "612", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Personas Físicas con Actividades Empresariales y Profesionales", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000006"), true, "621", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Incorporación Fiscal", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000007"), true, "625", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Régimen de las Actividades Empresariales con ingresos a través de Plataformas Tecnológicas", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 },
                    { new Guid("00000002-0002-0000-0000-000000000008"), true, "626", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", null, (short)0, "Régimen Simplificado de Confianza (RESICO)", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "seed", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_condiciones_pago_clave",
                schema: "compartido",
                table: "condiciones_pago",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_condiciones_pago_estatus",
                schema: "compartido",
                table: "condiciones_pago",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_incoterms_codigo",
                schema: "compartido",
                table: "incoterms",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incoterms_estatus",
                schema: "compartido",
                table: "incoterms",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_regimenes_fiscales_codigo",
                schema: "compartido",
                table: "regimenes_fiscales",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regimenes_fiscales_estatus",
                schema: "compartido",
                table: "regimenes_fiscales",
                column: "estatus");

            migrationBuilder.CreateIndex(
                name: "ix_transportistas_clave",
                schema: "compartido",
                table: "transportistas",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transportistas_estatus",
                schema: "compartido",
                table: "transportistas",
                column: "estatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "condiciones_pago",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "incoterms",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "regimenes_fiscales",
                schema: "compartido");

            migrationBuilder.DropTable(
                name: "transportistas",
                schema: "compartido");
        }
    }
}
