using Millet.CuentasPorPagar.Tests.P3;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>P8.4a · CA8.7: la factura se detiene con «Daños» desde la recepción y desde la captura.</summary>
public sealed class DanosRecepcionTests
{
    private static OcRecepcionRegistradaCommand RecepcionConDano(P3FacturaConOcTests.Ambiente a, Guid subAlmacenEnRevision)
    {
        var buena = a.Recepcion(10);
        var linea = buena.Payload.Lineas[0];
        return buena with
        {
            Payload = buena.Payload with
            {
                Lineas = [linea, linea with { LineaRecepcionId = Guid.NewGuid(), SubAlmacenId = subAlmacenEnRevision }],
            },
        };
    }

    private static async Task<P3FacturaConOcTests.Ambiente> AmbienteAsync()
    {
        var a = new P3FacturaConOcTests.Ambiente(recibido: 10);
        await a.Db.Database.EnsureCreatedAsync(); // siembra el catálogo de motivos (InMemory)
        return a;
    }

    private static void DebeEstarDetenida(Domain.FacturaProveedor.FacturaProveedor f)
    {
        f.Estado.Should().Be(EstadoPasivo.EnRevision);
        f.MotivoRevisionId.Should().Be(DetencionPorDanoService.MotivoDanosId);
        f.DependenciaRevisoraId.Should().Be(DependenciasFake.ComprasId);
    }

    [Fact]
    public async Task Factura_capturada_despues_de_la_recepcion_con_dano_nace_detenida()
    {
        await using var a = await AmbienteAsync();
        var rev = Guid.NewGuid();
        a.Almacen.EnRevision.Add(rev);
        await a.Recepciones.Handle(RecepcionConDano(a, rev), default);

        var r = await a.Handler.Handle(a.Command(10, 20), default);

        r.Estado.Should().Be(EstadoPasivo.EnRevision);
        DebeEstarDetenida(await a.Db.FacturasProveedor.SingleAsync(f => f.Id == r.Id));
    }

    [Fact]
    public async Task Factura_capturada_antes_de_la_recepcion_con_dano_queda_detenida_e_idempotente()
    {
        await using var a = await AmbienteAsync();
        var rev = Guid.NewGuid();
        a.Almacen.EnRevision.Add(rev);
        var r = await a.Handler.Handle(a.Command(10, 20), default);
        var cmd = RecepcionConDano(a, rev);

        await a.Recepciones.Handle(cmd, default);
        await a.Recepciones.Handle(cmd, default);

        DebeEstarDetenida(await a.Db.FacturasProveedor.SingleAsync(f => f.Id == r.Id));
        (await a.Db.RecepcionesOcLocal.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Recepcion_sin_dano_no_detiene()
    {
        await using var a = await AmbienteAsync();
        var r = await a.Handler.Handle(a.Command(10, 20), default);

        await a.Recepciones.Handle(RecepcionConDano(a, Guid.NewGuid()), default);

        (await a.Db.FacturasProveedor.SingleAsync(f => f.Id == r.Id)).Estado.Should().Be(EstadoPasivo.Capturada);
    }

    [Fact]
    public async Task Factura_autorizada_se_detiene_y_no_se_republica_como_autorizada()
    {
        await using var a = await AmbienteAsync();
        var rev = Guid.NewGuid();
        a.Almacen.EnRevision.Add(rev);
        var r = await a.Handler.Handle(a.Command(10, 20), default);
        var f = await a.Db.FacturasProveedor.SingleAsync(x => x.Id == r.Id);
        f.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();
        a.Eventos.Integraciones.Clear();

        await a.Recepciones.Handle(RecepcionConDano(a, rev), default);

        DebeEstarDetenida(f);
        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Should().BeEmpty();
        // Tesorería debe retirar el pasivo de la cola de pago: el contexto publica el retiro con el id de la factura.
        a.Eventos.Integraciones.OfType<PasivoRetiradoDePagoIntegrationEvent>().Should().ContainSingle()
            .Which.FacturaProveedorId.Should().Be(r.Id);
    }

    [Fact]
    public async Task Factura_nueva_se_detiene_aun_si_el_dano_ya_se_dictamino_Q5()
    {
        // Q5: el daño sigue contando aunque Almacén ya lo haya dictaminado (la recepción conserva la línea en
        // MAT-REV). El pasivo se libera a mano por el área (G1.4 traerá la elegibilidad parcial por línea).
        await using var a = await AmbienteAsync();
        var rev = Guid.NewGuid();
        a.Almacen.EnRevision.Add(rev);
        await a.Recepciones.Handle(RecepcionConDano(a, rev), default);
        // El dictamen no cambia la recepción registrada en CxP: no hay evento que la "limpie".

        var r = await a.Handler.Handle(a.Command(10, 20), default);

        r.Estado.Should().Be(EstadoPasivo.EnRevision);
        DebeEstarDetenida(await a.Db.FacturasProveedor.SingleAsync(f => f.Id == r.Id));
    }
}
