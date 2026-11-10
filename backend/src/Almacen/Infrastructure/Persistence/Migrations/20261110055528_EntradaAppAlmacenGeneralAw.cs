using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-159: el trigger del ledger deja de rechazar la medida de A+W en la ÚNICA (almacén general). ENTRADA_ALMACEN de la app
    /// y la recepción con factura la registran ahí sin rack; la regla «A+W entra a un rack» de packing list, devolución interna
    /// y dictamen queda en <c>UbicacionEntradaGuard</c>. Mismo cuerpo que <see cref="EntradaGeneralNoVidrio"/> sin ese bloque.
    /// </summary>
    public partial class EntradaAppAlmacenGeneralAw : Migration
    {
        private const string BloqueAwAUnica = """
    -- Artículo de A+W (línea con variante de medida) entra a un rack; lo que no es
    -- de A+W (consumibles, refacciones) puede entrar a la ÚNICA (almacén general).
    IF v_es_entrada AND v_ubi_es_default AND NEW.producto_aw_variante_id IS NOT NULL THEN
        RAISE EXCEPTION
            'ENTRADA_A_UBICACION_UNICA: ubicacion=% es la default (ÚNICA) de sub_almacen=% — los artículos de A+W entran a un rack; el almacén general es para consumibles y refacciones.',
            NEW.ubicacion_id, v_sub_almacen_id
            USING ERRCODE = '23514';
    END IF;

""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sinBloque = EntradaGeneralNoVidrio.TriggerNoVidrioAUnica.Replace(BloqueAwAUnica, "");
            if (sinBloque == EntradaGeneralNoVidrio.TriggerNoVidrioAUnica)
                throw new InvalidOperationException("No se encontró el bloque ENTRADA_A_UBICACION_UNICA en el cuerpo del trigger.");
            migrationBuilder.Sql(sinBloque);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EntradaGeneralNoVidrio.TriggerNoVidrioAUnica);
        }
    }
}
