using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>86e3tactf: motivo 13 «Instrucción de no pagar» — solo la libera quien la pidió (o quien tenga el permiso de reemplazo).</summary>
public sealed class InstruccionNoPagarTests
{
    private static readonly Guid MotivoNoPagar = Domain.FacturaProveedor.FacturaProveedor.MotivoIndicacionExpresaId;
    private static readonly Guid UsuarioA = Guid.NewGuid();
    private static readonly Guid UsuarioB = Guid.NewGuid();

    private static Domain.FacturaProveedor.FacturaProveedor Capturar() =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null, proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-013", serieProveedor: "A", fechaDocumento: DateTimeOffset.UtcNow, fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m, impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null, tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m, redondeoAplicado: 0m, ahora: DateTimeOffset.UtcNow);

    private static Domain.FacturaProveedor.FacturaProveedor Detenida(Guid usuario, Guid? solicitante = null)
    {
        var f = Capturar();
        f.EnviarARevision(MotivoNoPagar, Guid.NewGuid(), usuario, DateTimeOffset.UtcNow, solicitante);
        return f;
    }

    [Fact]
    public void El_id_del_motivo_es_el_del_seed_INDICACION_EXPRESA()
    {
        MotivoRevisionConfiguration.SeedMotivos.Single(m => m.Codigo == "INDICACION_EXPRESA").Id.Should().Be(MotivoNoPagar);
    }

    [Fact]
    public void Al_enviar_con_motivo_13_se_guarda_quien_lo_pidio_por_omision_quien_la_envia()
    {
        Detenida(UsuarioA).SolicitanteRevisionId.Should().Be(UsuarioA);
        Detenida(UsuarioA, solicitante: UsuarioB).SolicitanteRevisionId.Should().Be(UsuarioB);
    }

    [Fact]
    public void Otro_motivo_no_guarda_solicitante()
    {
        var f = Capturar();
        f.EnviarARevision(Guid.NewGuid(), Guid.NewGuid(), UsuarioA, DateTimeOffset.UtcNow, UsuarioB);
        f.SolicitanteRevisionId.Should().BeNull();
    }

    [Fact]
    public void Sin_usuario_ni_solicitante_no_se_puede_enviar_con_motivo_13()
    {
        var act = () => Capturar().EnviarARevision(MotivoNoPagar, Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_SOLICITANTE_REQUERIDO");
    }

    [Fact]
    public void B_intenta_liberar_la_factura_que_pidio_A_y_se_rechaza_A_la_libera()
    {
        var f = Detenida(UsuarioA);

        var act = () => f.LiberarRevision("Ya no aplica", UsuarioB, DateTimeOffset.UtcNow);
        act.Should().Throw<ForbiddenException>().Where(e => e.Code == "SOLO_LIBERA_SOLICITANTE");
        f.Estado.Should().Be(EstadoPasivo.EnRevision);

        f.LiberarRevision("Ya no aplica", UsuarioA, DateTimeOffset.UtcNow);
        f.Estado.Should().Be(EstadoPasivo.Capturada);
        f.SolicitanteRevisionId.Should().BeNull();
        f.Bitacora.Last().Motivo.Should().Contain($"solicitó {UsuarioA}");
    }

    [Fact]
    public void Con_el_permiso_de_reemplazo_libera_otro_usuario_y_la_bitacora_dice_quien_pidio()
    {
        var f = Detenida(UsuarioA);

        f.LiberarRevision("El solicitante no está", UsuarioB, DateTimeOffset.UtcNow, puedeReemplazarSolicitante: true);

        f.Estado.Should().Be(EstadoPasivo.Capturada);
        f.Bitacora.Last().UsuarioId.Should().Be(UsuarioB);
        f.Bitacora.Last().Motivo.Should().Contain($"solicitó {UsuarioA}");
    }

    [Fact]
    public void Otros_motivos_siguen_liberandolos_cualquier_usuario_con_permiso()
    {
        var f = Capturar();
        f.EnviarARevision(Guid.NewGuid(), Guid.NewGuid(), UsuarioA, DateTimeOffset.UtcNow);
        f.LiberarRevision("Corregido", UsuarioB, DateTimeOffset.UtcNow);
        f.Estado.Should().Be(EstadoPasivo.Capturada);
    }
}
