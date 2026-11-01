using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Domain.FoliosTransferencia;

public enum EstadoFolioTransferencia : short { Vigente = 1, Usado = 2, Cancelado = 3 }

/// <summary>
/// Folio de confirmación de transferencia (F6.6, R16 · Manual §2.1): en call
/// center el cliente paga por transferencia; Ingresos valida el abono en el
/// banco y emite este folio desde el ingreso ya registrado; Caja solo aplica
/// el cobro con forma 03 si trae un folio vigente, del mismo cliente y por el
/// mismo importe. Al cobrar queda <c>Usado</c> y ligado al cobro; si el cobro
/// se cancela vuelve a <c>Vigente</c>. Ingresos puede cancelar uno vigente.
/// Un ingreso admite varios folios hasta su disponible (pagos parciales).
/// </summary>
public sealed class FolioTransferencia : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string MonedaCaja = "MXN";

    public Guid EmpresaId { get; set; }

    /// <summary>Folio «TR-000001» (secuencia por empresa).</summary>
    public string Folio { get; private set; } = default!;
    public Guid MovimientoId { get; private set; }
    public Guid ClienteId { get; private set; }
    /// <summary>Pedido o referencia comercial que indicó el vendedor (opcional).</summary>
    public string? Pedido { get; private set; }
    public decimal Importe { get; private set; }
    public EstadoFolioTransferencia Estado { get; private set; } = EstadoFolioTransferencia.Vigente;
    public Guid EmitidoPor { get; private set; }
    public DateTimeOffset EmitidoEn { get; private set; }
    /// <summary>Cobro de caja que lo consumió (Facturación).</summary>
    public Guid? CobroMostradorId { get; private set; }
    public DateTimeOffset? UsadoEn { get; private set; }
    public string? MotivoCancelacion { get; private set; }
    public Guid? CanceladoPor { get; private set; }
    public DateTimeOffset? CanceladoEn { get; private set; }

    private FolioTransferencia() { }

    /// <summary>
    /// Emite el folio contra un ingreso de cliente en pesos. <paramref name="disponible"/>
    /// es lo que queda del ingreso descontando lo aplicado, las devoluciones y
    /// los folios no cancelados (F6.6: varios folios hasta el total del ingreso).
    /// </summary>
    public static FolioTransferencia Emitir(
        Guid empresaId, string folio, MovimientoBancario ingreso, Guid clienteId, string? pedido,
        decimal importe, decimal disponible, Guid emitidoPor, DateTimeOffset ahora)
    {
        if (ingreso.Sentido != SentidoMovimiento.Ingreso || ingreso.ContramovimientoDe is not null)
            throw new BusinessRuleException("FOLIO_TR_NO_INGRESO", "El folio se emite desde un ingreso bancario.");
        if (!string.Equals(ingreso.Moneda, MonedaCaja, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("FOLIO_TR_MONEDA",
                "Caja cobra solo en pesos: el folio se emite desde un ingreso en MXN.");
        if (ingreso.BeneficiarioTipo is not null && ingreso.BeneficiarioTipo != BeneficiarioTipo.Cliente)
            throw new BusinessRuleException("FOLIO_TR_NO_CLIENTE", "El ingreso no es de un cliente.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("FOLIO_TR_CLIENTE_VACIO", "Indica el cliente que pagó la transferencia.");
        if (ingreso.BeneficiarioRef is Guid clienteIngreso && clienteIngreso != clienteId)
            throw new BusinessRuleException("FOLIO_TR_CLIENTE_DISTINTO", "El ingreso está identificado con otro cliente.");
        if (emitidoPor == Guid.Empty)
            throw new BusinessRuleException("FOLIO_TR_USUARIO_VACIO", "El usuario que emite es obligatorio.");
        if (importe <= 0)
            throw new BusinessRuleException("FOLIO_TR_IMPORTE_INVALIDO", "El importe debe ser mayor a cero.");
        if (importe > disponible)
            throw new BusinessRuleException("FOLIO_TR_EXCEDE_INGRESO",
                $"El importe excede lo disponible del ingreso ({disponible:N2}).");

        return new FolioTransferencia
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            Folio = folio,
            MovimientoId = ingreso.Id,
            ClienteId = clienteId,
            Pedido = string.IsNullOrWhiteSpace(pedido) ? null : pedido.Trim(),
            Importe = importe,
            EmitidoPor = emitidoPor,
            EmitidoEn = ahora,
        };
    }

    /// <summary>El cobro de caja lo consumió. Idempotente para el mismo cobro.</summary>
    public void MarcarUsado(Guid cobroMostradorId, DateTimeOffset ahora)
    {
        if (Estado == EstadoFolioTransferencia.Usado && CobroMostradorId == cobroMostradorId) return;
        if (Estado != EstadoFolioTransferencia.Vigente)
            throw new BusinessRuleException("FOLIO_TR_NO_VIGENTE", $"El folio {Folio} no está vigente.");
        Estado = EstadoFolioTransferencia.Usado;
        CobroMostradorId = cobroMostradorId;
        UsadoEn = ahora;
    }

    /// <summary>El cobro que lo usó se canceló: el folio vuelve a vigente. Idempotente.</summary>
    public void Liberar(Guid cobroMostradorId)
    {
        if (Estado != EstadoFolioTransferencia.Usado || CobroMostradorId != cobroMostradorId) return;
        Estado = EstadoFolioTransferencia.Vigente;
        CobroMostradorId = null;
        UsadoEn = null;
    }

    /// <summary>Ingresos cancela un folio que no se usó.</summary>
    public void Cancelar(string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado == EstadoFolioTransferencia.Usado)
            throw new BusinessRuleException("FOLIO_TR_USADO", $"El folio {Folio} ya se aplicó en caja; cancela primero el cobro.");
        if (Estado == EstadoFolioTransferencia.Cancelado)
            throw new BusinessRuleException("FOLIO_TR_CANCELADO", $"El folio {Folio} ya está cancelado.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("FOLIO_TR_MOTIVO_VACIO", "Indica el motivo de la cancelación.");
        Estado = EstadoFolioTransferencia.Cancelado;
        MotivoCancelacion = motivo.Trim();
        CanceladoPor = usuarioId;
        CanceladoEn = ahora;
    }
}

/// <summary>Secuencia atómica del folio «TR-000001» por empresa (mismo patrón que la de devoluciones).</summary>
public sealed class FolioSecuenciaTransferencia
{
    public Guid EmpresaId { get; private set; }
    public int Siguiente { get; private set; }

    private FolioSecuenciaTransferencia() { }
}
