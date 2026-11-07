using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Integration;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// 86e3zf04y · la rotura capturada en la pantalla de Almacén (ALM-03, <c>almacen.salida_rotura.registrada.v1</c>) genera su póliza de
/// merma con la plantilla sembrada por la migración: cargo MERMA con el área o equipo, abono INVENTARIO; sin duplicarse al reentregar
/// el evento, y en Rechazos (sin perderse) cuando la regla C-03 exige un dato que la rotura no trae. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static SalidaRoturaRegistradaIntegrationEvent Rotura(Escenario e, Guid movimiento, decimal valor, Guid? centro) =>
        new(EmpresaBootstrapId, Ahora, Guid.NewGuid(), movimiento, "M-ROT-FIX", Hoy(), Guid.NewGuid(), e.Sucursal, Guid.NewGuid(), "PZA",
            2m, valor / 2m, valor, "rotura_rack", "Rotura en rack", centro, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task Rotura_de_la_pantalla_genera_poliza_de_merma_con_su_equipo_y_no_se_duplica()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var movimiento = Guid.NewGuid();
            var evento = await Almacen(Rotura(e, movimiento, 1_800m, e.CentroCosto));
            await Almacen(Rotura(e, movimiento, 1_800m, e.CentroCosto), evento); // misma entrega otra vez (reintento del bus)

            (await RechazoAsync(evento)).Should().BeNull();
            var sm = (await PolizasAsync(movimiento)).Should().ContainSingle().Subject;
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["MERMA"] && x.Cargo == 1_800m && x.Dim3Id == e.CentroCosto);
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["INVENTARIO"] && x.Abono == 1_800m);
        }
        finally
        {
            await LimpiarAsync(e);
        }
    }

    /// <summary>
    /// Regla C-03 en la cuenta de gastos con ubicación, área y equipo obligatorios (caso de la cuenta 600): el puerto de H1, con la
    /// plantilla de la rotura, dice que exige área y equipo; sin centro la póliza queda en Rechazos y con el equipo nace.
    /// </summary>
    [Fact]
    public async Task Rotura_sin_el_dato_que_exige_la_cuenta_queda_en_rechazos_y_con_el_equipo_genera_su_poliza()
    {
        Escenario? e = null;
        Guid? gastos = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                gastos = await db.Cuentas.IgnoreQueryFilters().Where(c => c.Id == e.Cuentas["MERMA"]).Select(c => c.PadreId).SingleAsync();
            }
            foreach (var dim in new[] { "Dim1", "Dim2", "Dim3" })
                (await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension", new
                {
                    cuentaId = gastos, tipoDocumentoId = (Guid?)null, dimension = dim, requerimiento = "Obligatorio",
                    vigenteDesde = Hoy(), vigenteHasta = (DateOnly?)null, esPrueba = true, nota = "FIX regla C-03 (rotura pantalla)",
                })).StatusCode.Should().Be(HttpStatusCode.Created);

            using (var scope = factory.Services.CreateScope())
            {
                var puerto = scope.ServiceProvider.GetRequiredService<IDimensionesSalidaInventarioPort>();
                var exige = await puerto.ResolverAsync(new SolicitudDimensionesSalida(EmpresaBootstrapId, e.Sucursal, "rotura", Hoy(), null, null,
                    "almacen.salida_rotura.registrada.v1"), default);
                (exige.ExigeArea, exige.ExigeEquipo).Should().Be((true, true));
                exige.Faltante.Should().Contain("el área").And.Contain("el equipo");
            }

            var sinCentro = Guid.NewGuid();
            var eventoSinCentro = await Almacen(Rotura(e, sinCentro, 500m, null));
            var rechazo = await RechazoAsync(eventoSinCentro);
            rechazo.Should().NotBeNull("la rotura no se pierde: espera en Rechazos a que Contabilidad complete el dato");
            rechazo!.Estado.Should().Be(EstadoRechazoContable.Pendiente);
            (await PolizasAsync(sinCentro)).Should().BeEmpty();

            var conEquipo = Guid.NewGuid();
            var eventoConEquipo = await Almacen(Rotura(e, conEquipo, 500m, e.CentroCosto));
            (await RechazoAsync(eventoConEquipo)).Should().BeNull();
            var sm = (await PolizasAsync(conEquipo)).Should().ContainSingle().Subject;
            sm.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["MERMA"] && x.Cargo == 500m
                && x.Dim3Id == e.CentroCosto && x.Dim2Id != null && x.Dim1Id != null);
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
