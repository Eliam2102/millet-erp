using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-453 (86e3zr740): el motor no pone área por omisión ni exime el equipo. La comisión de la terminal carga a gastos con una línea
/// de plantilla sin centro; con la regla C-03 de prueba (área y equipo obligatorios en la cuenta padre de gastos del escenario), el
/// evento queda en Rechazos como «Dato faltante» con el aviso del validador y no nace póliza. Datos ficticios; la regla se borra al final.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    [Fact]
    public async Task M453_poliza_automatica_a_gastos_sin_area_ni_equipo_queda_en_rechazos_sin_valor_por_omision()
    {
        Escenario? e = null;
        Guid? gastos = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                gastos = await db.Cuentas.IgnoreQueryFilters().Where(c => c.Id == e.Cuentas["COMISIONES_BANCARIAS"]).Select(c => c.PadreId).SingleAsync();
            }
            foreach (var dim in new[] { "Dim2", "Dim3" })
                (await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension", new
                {
                    cuentaId = gastos, tipoDocumentoId = (Guid?)null, dimension = dim, requerimiento = "Obligatorio",
                    vigenteDesde = Hoy(), vigenteHasta = (DateOnly?)null, esPrueba = true, nota = "FIX regla C-03 (M-453)",
                })).StatusCode.Should().Be(HttpStatusCode.Created);

            var factura = Guid.NewGuid();
            var evento = await Facturacion(FacturaBanco(e, factura, 6_000m, 960m));

            (await PolizasAsync(factura)).Should().BeEmpty();
            var rechazo = (await RechazoAsync(evento))!;
            rechazo.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
            rechazo.Mensaje.Should().Contain("Falta Área").And.Contain("Falta Equipo").And.NotContain("Errores de validación");
        }
        finally
        {
            if (gastos is { } padre)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE regla_id IN (SELECT id FROM contabilidad.reglas_dimension WHERE cuenta_id = {0})", padre);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id = {0}", padre);
            }
            await LimpiarAsync(e);
        }
    }
}
