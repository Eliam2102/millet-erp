using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Application.FacturaProveedor.EnviarARevision;
using Millet.CuentasPorPagar.Application.FacturaProveedor.LiberarRevision;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// U-4 (86e3yzemw · ALM-costo-06): la diferencia del neto de la factura contra la entrada abre su incidencia
/// (rechazo por tolerancia o revisión por diferencia de precio) y se resuelve al recapturar o al liberar la revisión.
/// </summary>
public sealed class IncidenciasCostoU4Tests
{
    private static readonly Guid Capturista = Guid.NewGuid();

    private sealed class IncidenciasFake : IIncidenciasPort
    {
        public List<AbrirIncidenciaSolicitud> Abiertas { get; } = [];
        public List<(string Paso, Guid DocumentoId, string? DetalleClave)> Resueltas { get; } = [];
        public Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
        {
            Abiertas.Add(solicitud);
            return Task.FromResult(new IncidenciaAbierta(Guid.NewGuid(), Abiertas.Count, false));
        }
        public Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken)
        {
            Resueltas.Add((paso, documentoId, detalleClave));
            return Task.FromResult(1);
        }
        public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class Usuario(Guid id) : ICurrentUserContext, ICurrentUserPermissions
    {
        public Guid? UserId => id;
        public string? UserName => "capturista";
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class SinTipoCambio : ITipoCambioReadPort
    {
        public Task<decimal?> ObtenerAsync(string monedaOrigen, string monedaDestino, DateOnly fecha, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Una captura en MXN no consulta el tipo de cambio.");
    }

    private static CapturarFacturaConOcHandler Handler(P3FacturaConOcTests.Ambiente a, IIncidenciasPort inc) =>
        new(a.Db, a, a, a, new XmlCfdiParser(), a, new Mediator(new ServiceCollection().BuildServiceProvider()), a, a,
            new DetencionPorDanoService(a.Db, a.Almacen, new DependenciasFake(), NullLogger<DetencionPorDanoService>.Instance), new SinTipoCambio(),
            inc, new Usuario(Capturista));

    [Fact]
    public async Task Factura_rechazada_por_tolerancia_abre_incidencia_de_la_orden_y_la_recaptura_correcta_la_resuelve()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var inc = new IncidenciasFake();
        var h = Handler(a, inc);

        var rechazada = await h.Handle(a.Command(10, 21, total: 210), default);

        rechazada.Estado.Should().Be(EstadoPasivo.Cancelada);
        var abierta = inc.Abiertas.Should().ContainSingle().Subject;
        var ocId = a.Command(1, 1).OrdenCompraId;
        (abierta.Paso, abierta.Modulo, abierta.TipoDocumento, abierta.DocumentoId, abierta.DetalleClave).Should()
            .Be((PasosIncidencia.AlmCostoNetoFactura, "cuentas_por_pagar", "orden_compra", ocId, "tolerancia"));
        abierta.ResponsableUsuarioId.Should().Be(Capturista);
        abierta.CodigoMotivo.Should().Be("FACTURA_RECHAZADA_POR_TOLERANCIA");
        abierta.RutaReanudacion.Should().Be($"/cxp/facturas/{rechazada.Id}");
        inc.Resueltas.Should().BeEmpty();

        var correcta = await h.Handle(a.Command(10, 20, total: 200), default);

        correcta.Estado.Should().Be(EstadoPasivo.Capturada);
        inc.Resueltas.Should().ContainSingle().Which.Should().Be((PasosIncidencia.AlmCostoNetoFactura, ocId, (string?)"tolerancia"));
    }

    [Fact]
    public async Task Revision_por_diferencia_de_precio_abre_incidencia_y_liberarla_la_resuelve()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        await a.Db.Database.EnsureCreatedAsync();
        var factura = await a.Handler.Handle(a.Command(10, 20, total: 200), default);
        var inc = new IncidenciasFake();
        var usuario = new Usuario(Capturista);
        var motivo = MotivoRevisionConfiguration.SeedMotivos.Single(m => m.Codigo == "DIFERENCIA_PRECIO");
        var version = (await a.Db.FacturasProveedor.SingleAsync(f => f.Id == factura.Id)).Version;

        var enviada = await new EnviarFacturaARevisionHandler(a.Db, new DependenciasFake(), usuario, a, inc)
            .Handle(new(factura.Id, version, motivo.Id, DependenciasFake.ComprasId), default);

        var abierta = inc.Abiertas.Should().ContainSingle().Subject;
        (abierta.Paso, abierta.TipoDocumento, abierta.DocumentoId, abierta.DetalleClave, abierta.CodigoMotivo).Should()
            .Be((PasosIncidencia.AlmCostoNetoFactura, "factura_proveedor", factura.Id, "revision", "DIFERENCIA_PRECIO"));

        await new LiberarRevisionFacturaHandler(a.Db, usuario, a, usuario, inc)
            .Handle(new(factura.Id, enviada.Version, "Compras corrigió el precio con el proveedor."), default);

        inc.Resueltas.Should().ContainSingle().Which.Should().Be((PasosIncidencia.AlmCostoNetoFactura, factura.Id, (string?)"revision"));
    }

    [Fact]
    public async Task Revision_por_otro_motivo_no_abre_incidencia_de_costo()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        await a.Db.Database.EnsureCreatedAsync();
        var factura = await a.Handler.Handle(a.Command(10, 20, total: 200), default);
        var inc = new IncidenciasFake();
        var motivo = MotivoRevisionConfiguration.SeedMotivos.Single(m => m.Codigo == "TIEMPO_RESPUESTA");
        var version = (await a.Db.FacturasProveedor.SingleAsync(f => f.Id == factura.Id)).Version;

        await new EnviarFacturaARevisionHandler(a.Db, new DependenciasFake(), new Usuario(Capturista), a, inc)
            .Handle(new(factura.Id, version, motivo.Id, DependenciasFake.ComprasId), default);

        inc.Abiertas.Should().BeEmpty();
    }
}
