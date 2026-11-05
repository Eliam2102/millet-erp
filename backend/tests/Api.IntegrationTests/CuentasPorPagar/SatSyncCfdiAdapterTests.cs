using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>
/// P8.2/P8.7: sincronización del estatus SAT por el adapter real (IFiscalCfdiReceiver) sobre PostgreSQL.
/// En memoria el filtro por UuidCfdi.Valor "funcionaba"; con Npgsql no se traduce. Datos FIX, se limpian al final.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class SatSyncCfdiAdapterTests(WebApplicationFactory<Program> factory)
{
    private static readonly DateTimeOffset FechaCancel = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task Vigente_no_cambia_el_estatus_ni_la_factura() =>
        Correr(EstadoPasivo.Capturada, solicitudAceptada: false, "Vigente", (cfdi, f) =>
        {
            Assert.Equal(EstatusSatCfdi.Vigente, cfdi.EstatusSat);
            Assert.Equal(EstadoPasivo.Capturada, f.Estado);
        });

    [Fact]
    public Task Cancelado_con_solicitud_aceptada_cancela_la_factura() =>
        Correr(EstadoPasivo.Capturada, solicitudAceptada: true, "Cancelado", (cfdi, f) =>
        {
            Assert.Equal(EstatusSatCfdi.Cancelado, cfdi.EstatusSat);
            Assert.Equal(EstadoPasivo.Cancelada, f.Estado);
        });

    [Fact]
    public Task Cancelado_sin_solicitud_envia_la_factura_autorizada_a_revision() =>
        Correr(EstadoPasivo.Autorizada, solicitudAceptada: false, "Cancelado", (cfdi, f) =>
        {
            Assert.Equal(EstatusSatCfdi.Cancelado, cfdi.EstatusSat);
            Assert.Equal(EstadoPasivo.EnRevision, f.Estado);
        });

    private async Task Correr(EstadoPasivo inicial, bool solicitudAceptada, string estatusSat, Action<CfdiRecibido, Factura> verificar)
    {
        await using var app = factory.ConEmpresa(EmpresaBootstrapId);
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var ahora = DateTimeOffset.UtcNow;
        // Guardar facturas exige periodo contable abierto.
        using (var admin = await LoginAsync(app))
            await AsegurarPeriodosAbiertosAsync(admin, DateOnly.FromDateTime(ahora.DateTime));
        var cfdi =CfdiRecibido.Ingresar(EmpresaBootstrapId, UuidCfdi.Parse(uuid), RfcMexicano.Parse("PRO010101AAA"),
            RfcMexicano.Parse("MIL010101AAA"), Millet.CuentasPorPagar.Domain.Cfdi.TipoCfdi.Ingreso, "FIX-SAT", null, ahora, 116m, 100m, 16m, 0m, "MXN", null,
            CanalOrigenCfdi.DescargaSat, ahora, "blob-fix", null, "hash-fix", estatusSat: EstatusSatCfdi.Vigente);
        var factura = Factura.CapturarSinOc(EmpresaBootstrapId, cfdi.Id, uuid, Guid.NewGuid(), Guid.NewGuid(), "FIX-SAT", null,
            ahora, ahora, DateOnly.FromDateTime(ahora.DateTime).AddDays(30), "MXN", null, 100m, 0m, 16m, 0m, 116m, "Prueba SAT sync", ahora);
        if (inicial == EstadoPasivo.Autorizada) factura.Autorizar(null, ahora);
        cfdi.MarcarConvertidoEnPasivo(factura.Id);
        using (var scope = app.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            db.CfdisRecibidos.Add(cfdi);
            db.FacturasProveedor.Add(factura);
            if (solicitudAceptada)
            {
                var sol = SolicitudCancelacionCfdi.Registrar(EmpresaBootstrapId, cfdi.Id, AreaSolicitudCancelacion.Compras, ahora, null, null);
                sol.Decidir(true, "Acuse FIX", null, null, null, ahora);
                db.SolicitudesCancelacionCfdi.Add(sol);
            }
            await db.SaveChangesAsync();
        }

        try
        {
            using (var scope = app.Services.CreateScope())
            using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
            {
                // Adapter real resuelto del contenedor (sin SAT/PAC): solo entra el payload ya cosechado.
                var receiver = scope.ServiceProvider.GetRequiredService<IFiscalCfdiReceiver>();
                await receiver.IngresarCfdiAsync(new CfdiCosechadoPayload(EmpresaBootstrapId, "MIL010101AAA", uuid, "PRO010101AAA",
                    "Proveedor SA", "MIL010101AAA", "Millet", ahora, ahora, 116m, "I", estatusSat,
                    estatusSat == "Cancelado" ? FechaCancel : null, Guid.NewGuid(), "req-fix", []), CancellationToken.None);
            }

            using var lectura = app.Services.CreateScope();
            using var bypass = lectura.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ldb = lectura.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            verificar(await ldb.CfdisRecibidos.AsNoTracking().SingleAsync(c => c.Id == cfdi.Id),
                await ldb.FacturasProveedor.AsNoTracking().SingleAsync(f => f.Id == factura.Id));
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            await db.SolicitudesCancelacionCfdi.Where(s => s.CfdiRecibidoId == cfdi.Id).ExecuteDeleteAsync();
            await db.FacturasProveedor.Where(f => f.Id == factura.Id).ExecuteDeleteAsync();
            await db.CfdisRecibidos.Where(c => c.Id == cfdi.Id).ExecuteDeleteAsync();
        }
    }
}
