using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CentrosCosto.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Refactor al modelo Dim (CECO-PR4, 01-diseno Rev 0.4 / levantamiento
    /// §7.4): RENOMBRES puros — nada de drop+create. Las cinco tablas están
    /// VACÍAS en todos los ambientes que importan (prod/CI nunca se
    /// sembraron; el dev local se limpió por runbook antes de este PR —
    /// STOP #1 del kickoff).
    ///
    /// <para>
    /// Cambios: sucursales_ceco→dim1 (drop sucursal_id + su UNIQUE — muere
    /// el vínculo con compartido; clave_ceco→clave; +nombre),
    /// departamentos→dim2, equipos→dim3, grupos→grupos_dim2,
    /// subgrupos→grupos_dim3, con columnas FK, índices y constraints
    /// (PK/FK/CHECK) renombrados a los nombres que espera el modelo nuevo.
    /// El Up fue escrito a mano (EF generaba drop+create al no poder
    /// inferir renombres); el Designer/snapshot generados describen el
    /// modelo destino y quedan tal cual — el drift-check del gate verifica
    /// que el esquema resultante coincide exactamente.
    /// </para>
    /// </summary>
    public partial class ModeloDimRenombrado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Tablas ────────────────────────────────────────────────
            migrationBuilder.RenameTable(name: "sucursales_ceco", schema: "centros_costo", newName: "dim1", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "departamentos", schema: "centros_costo", newName: "dim2", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "equipos", schema: "centros_costo", newName: "dim3", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "grupos", schema: "centros_costo", newName: "grupos_dim2", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "subgrupos", schema: "centros_costo", newName: "grupos_dim3", newSchema: "centros_costo");

            // ── 2. dim1: muere el vínculo (tabla vacía — sin datos que migrar) ──
            migrationBuilder.DropIndex(name: "ux_sucursales_ceco_sucursal_id", schema: "centros_costo", table: "dim1");
            migrationBuilder.DropColumn(name: "sucursal_id", schema: "centros_costo", table: "dim1");
            migrationBuilder.RenameColumn(name: "clave_ceco", schema: "centros_costo", table: "dim1", newName: "clave");
            migrationBuilder.AddColumn<string>(
                name: "nombre", schema: "centros_costo", table: "dim1",
                type: "character varying(254)", maxLength: 254, nullable: false, defaultValue: "");

            // ── 3. Columnas FK ───────────────────────────────────────────
            migrationBuilder.RenameColumn(name: "sucursal_centro_costo_id", schema: "centros_costo", table: "dim2", newName: "dim1_id");
            migrationBuilder.RenameColumn(name: "grupo_id", schema: "centros_costo", table: "dim2", newName: "grupo_dim2_id");
            migrationBuilder.RenameColumn(name: "departamento_id", schema: "centros_costo", table: "dim3", newName: "dim2_id");
            migrationBuilder.RenameColumn(name: "subgrupo_id", schema: "centros_costo", table: "dim3", newName: "grupo_dim3_id");

            // ── 4. Índices ───────────────────────────────────────────────
            migrationBuilder.RenameIndex(name: "ux_sucursales_ceco_clave_ceco", schema: "centros_costo", table: "dim1", newName: "ux_dim1_clave");
            migrationBuilder.RenameIndex(name: "ix_sucursales_ceco_estatus", schema: "centros_costo", table: "dim1", newName: "ix_dim1_estatus");
            migrationBuilder.RenameIndex(name: "ux_grupos_nombre", schema: "centros_costo", table: "grupos_dim2", newName: "ux_grupos_dim2_nombre");
            migrationBuilder.RenameIndex(name: "ix_grupos_estatus", schema: "centros_costo", table: "grupos_dim2", newName: "ix_grupos_dim2_estatus");
            migrationBuilder.RenameIndex(name: "ux_subgrupos_nombre", schema: "centros_costo", table: "grupos_dim3", newName: "ux_grupos_dim3_nombre");
            migrationBuilder.RenameIndex(name: "ix_subgrupos_estatus", schema: "centros_costo", table: "grupos_dim3", newName: "ix_grupos_dim3_estatus");
            migrationBuilder.RenameIndex(name: "ux_departamentos_clave", schema: "centros_costo", table: "dim2", newName: "ux_dim2_clave");
            migrationBuilder.RenameIndex(name: "ix_departamentos_estatus", schema: "centros_costo", table: "dim2", newName: "ix_dim2_estatus");
            migrationBuilder.RenameIndex(name: "ix_departamentos_grupo_id", schema: "centros_costo", table: "dim2", newName: "ix_dim2_grupo_dim2_id");
            migrationBuilder.RenameIndex(name: "ix_departamentos_sucursal_centro_costo_id", schema: "centros_costo", table: "dim2", newName: "ix_dim2_dim1_id");
            migrationBuilder.RenameIndex(name: "ux_equipos_clave", schema: "centros_costo", table: "dim3", newName: "ux_dim3_clave");
            migrationBuilder.RenameIndex(name: "ix_equipos_estatus", schema: "centros_costo", table: "dim3", newName: "ix_dim3_estatus");
            migrationBuilder.RenameIndex(name: "ix_equipos_departamento_id", schema: "centros_costo", table: "dim3", newName: "ix_dim3_dim2_id");
            migrationBuilder.RenameIndex(name: "ix_equipos_subgrupo_id", schema: "centros_costo", table: "dim3", newName: "ix_dim3_grupo_dim3_id");

            // ── 5. Constraints (PK/FK/CHECK): Postgres RENAME CONSTRAINT ─
            migrationBuilder.Sql("""
                ALTER TABLE centros_costo.dim1 RENAME CONSTRAINT pk_sucursales_ceco TO pk_dim1;
                ALTER TABLE centros_costo.dim1 RENAME CONSTRAINT ck_sucursales_ceco_estatus TO ck_dim1_estatus;
                ALTER TABLE centros_costo.grupos_dim2 RENAME CONSTRAINT pk_grupos TO pk_grupos_dim2;
                ALTER TABLE centros_costo.grupos_dim2 RENAME CONSTRAINT ck_grupos_estatus TO ck_grupos_dim2_estatus;
                ALTER TABLE centros_costo.grupos_dim3 RENAME CONSTRAINT pk_subgrupos TO pk_grupos_dim3;
                ALTER TABLE centros_costo.grupos_dim3 RENAME CONSTRAINT ck_subgrupos_estatus TO ck_grupos_dim3_estatus;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT pk_departamentos TO pk_dim2;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT ck_departamentos_estatus TO ck_dim2_estatus;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT fk_departamentos_grupos_grupo_id TO fk_dim2_grupos_dim2_grupo_dim2_id;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT fk_departamentos_sucursales_ceco_sucursal_centro_costo_id TO fk_dim2_dim1_dim1_id;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT pk_equipos TO pk_dim3;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT ck_equipos_estatus TO ck_dim3_estatus;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT fk_equipos_departamentos_departamento_id TO fk_dim3_dim2_dim2_id;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT fk_equipos_subgrupos_subgrupo_id TO fk_dim3_grupos_dim3_grupo_dim3_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE centros_costo.dim1 RENAME CONSTRAINT pk_dim1 TO pk_sucursales_ceco;
                ALTER TABLE centros_costo.dim1 RENAME CONSTRAINT ck_dim1_estatus TO ck_sucursales_ceco_estatus;
                ALTER TABLE centros_costo.grupos_dim2 RENAME CONSTRAINT pk_grupos_dim2 TO pk_grupos;
                ALTER TABLE centros_costo.grupos_dim2 RENAME CONSTRAINT ck_grupos_dim2_estatus TO ck_grupos_estatus;
                ALTER TABLE centros_costo.grupos_dim3 RENAME CONSTRAINT pk_grupos_dim3 TO pk_subgrupos;
                ALTER TABLE centros_costo.grupos_dim3 RENAME CONSTRAINT ck_grupos_dim3_estatus TO ck_subgrupos_estatus;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT pk_dim2 TO pk_departamentos;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT ck_dim2_estatus TO ck_departamentos_estatus;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT fk_dim2_grupos_dim2_grupo_dim2_id TO fk_departamentos_grupos_grupo_id;
                ALTER TABLE centros_costo.dim2 RENAME CONSTRAINT fk_dim2_dim1_dim1_id TO fk_departamentos_sucursales_ceco_sucursal_centro_costo_id;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT pk_dim3 TO pk_equipos;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT ck_dim3_estatus TO ck_equipos_estatus;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT fk_dim3_dim2_dim2_id TO fk_equipos_departamentos_departamento_id;
                ALTER TABLE centros_costo.dim3 RENAME CONSTRAINT fk_dim3_grupos_dim3_grupo_dim3_id TO fk_equipos_subgrupos_subgrupo_id;
                """);

            migrationBuilder.RenameIndex(name: "ux_dim1_clave", schema: "centros_costo", table: "dim1", newName: "ux_sucursales_ceco_clave_ceco");
            migrationBuilder.RenameIndex(name: "ix_dim1_estatus", schema: "centros_costo", table: "dim1", newName: "ix_sucursales_ceco_estatus");
            migrationBuilder.RenameIndex(name: "ux_grupos_dim2_nombre", schema: "centros_costo", table: "grupos_dim2", newName: "ux_grupos_nombre");
            migrationBuilder.RenameIndex(name: "ix_grupos_dim2_estatus", schema: "centros_costo", table: "grupos_dim2", newName: "ix_grupos_estatus");
            migrationBuilder.RenameIndex(name: "ux_grupos_dim3_nombre", schema: "centros_costo", table: "grupos_dim3", newName: "ux_subgrupos_nombre");
            migrationBuilder.RenameIndex(name: "ix_grupos_dim3_estatus", schema: "centros_costo", table: "grupos_dim3", newName: "ix_subgrupos_estatus");
            migrationBuilder.RenameIndex(name: "ux_dim2_clave", schema: "centros_costo", table: "dim2", newName: "ux_departamentos_clave");
            migrationBuilder.RenameIndex(name: "ix_dim2_estatus", schema: "centros_costo", table: "dim2", newName: "ix_departamentos_estatus");
            migrationBuilder.RenameIndex(name: "ix_dim2_grupo_dim2_id", schema: "centros_costo", table: "dim2", newName: "ix_departamentos_grupo_id");
            migrationBuilder.RenameIndex(name: "ix_dim2_dim1_id", schema: "centros_costo", table: "dim2", newName: "ix_departamentos_sucursal_centro_costo_id");
            migrationBuilder.RenameIndex(name: "ux_dim3_clave", schema: "centros_costo", table: "dim3", newName: "ux_equipos_clave");
            migrationBuilder.RenameIndex(name: "ix_dim3_estatus", schema: "centros_costo", table: "dim3", newName: "ix_equipos_estatus");
            migrationBuilder.RenameIndex(name: "ix_dim3_dim2_id", schema: "centros_costo", table: "dim3", newName: "ix_equipos_departamento_id");
            migrationBuilder.RenameIndex(name: "ix_dim3_grupo_dim3_id", schema: "centros_costo", table: "dim3", newName: "ix_equipos_subgrupo_id");

            migrationBuilder.RenameColumn(name: "dim1_id", schema: "centros_costo", table: "dim2", newName: "sucursal_centro_costo_id");
            migrationBuilder.RenameColumn(name: "grupo_dim2_id", schema: "centros_costo", table: "dim2", newName: "grupo_id");
            migrationBuilder.RenameColumn(name: "dim2_id", schema: "centros_costo", table: "dim3", newName: "departamento_id");
            migrationBuilder.RenameColumn(name: "grupo_dim3_id", schema: "centros_costo", table: "dim3", newName: "subgrupo_id");

            migrationBuilder.DropColumn(name: "nombre", schema: "centros_costo", table: "dim1");
            migrationBuilder.RenameColumn(name: "clave", schema: "centros_costo", table: "dim1", newName: "clave_ceco");
            migrationBuilder.AddColumn<Guid>(
                name: "sucursal_id", schema: "centros_costo", table: "dim1",
                type: "uuid", nullable: false, defaultValue: Guid.Empty);
            migrationBuilder.CreateIndex(
                name: "ux_sucursales_ceco_sucursal_id", schema: "centros_costo", table: "dim1",
                column: "sucursal_id", unique: true);

            migrationBuilder.RenameTable(name: "dim1", schema: "centros_costo", newName: "sucursales_ceco", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "dim2", schema: "centros_costo", newName: "departamentos", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "dim3", schema: "centros_costo", newName: "equipos", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "grupos_dim2", schema: "centros_costo", newName: "grupos", newSchema: "centros_costo");
            migrationBuilder.RenameTable(name: "grupos_dim3", schema: "centros_costo", newName: "subgrupos", newSchema: "centros_costo");
        }
    }
}
