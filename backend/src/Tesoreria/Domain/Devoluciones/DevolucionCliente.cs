using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Bancos;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Domain.Devoluciones;

public enum OrigenDevolucionCliente : short { DepositoErroneo = 1, NotaCredito = 2 }

public enum EstadoDevolucionCliente : short { Solicitada = 1, Autorizada = 2, Rechazada = 3, Pagada = 4, Cancelada = 5 }

/// <summary>Cuenta bancaria del cliente a la que se devuelve el dinero.</summary>
public sealed record CuentaDestinoDevolucion(string Banco, string Clabe, string Titular);

/// <summary>
/// Solicitud de devolución de dinero a un cliente (T9.6): por un depósito
/// erróneo o por el reembolso de una nota de crédito ya registrada.
/// Ciclo <c>Solicitada → Autorizada → Pagada</c> con <c>Rechazada</c> y
/// <c>Cancelada</c>. Quien solicita no autoriza. Nunca se devuelve más que
/// el origen (T9.6-a); el pago registra un contramovimiento, no borra nada.
/// </summary>
public sealed class DevolucionCliente : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid? SucursalId { get; private set; }
    public OrigenDevolucionCliente Origen { get; private set; }
    public Guid? MovimientoOrigenId { get; private set; }
    public Guid? NotaCreditoId { get; private set; }
    public string? NotaCreditoFolio { get; private set; }
    public string? NotaCreditoUuid { get; private set; }
    public decimal ImporteMaximoOrigen { get; private set; }
    public Guid ClienteId { get; private set; }
    public decimal Importe { get; private set; }
    public string Moneda { get; private set; } = default!;
    public string Motivo { get; private set; } = default!;
    public string? DestinoBanco { get; private set; }
    public string? DestinoClabe { get; private set; }
    public string? DestinoTitular { get; private set; }
    public EstadoDevolucionCliente Estado { get; private set; } = EstadoDevolucionCliente.Solicitada;
    public Guid? SolicitadaPor { get; private set; }
    public DateTimeOffset SolicitadaEn { get; private set; }
    public Guid? AutorizadaPor { get; private set; }
    public DateTimeOffset? AutorizadaEn { get; private set; }
    public Guid? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public string? MotivoCancelacion { get; private set; }
    public Guid? MovimientoPagoId { get; private set; }
    public Guid? PagadaPor { get; private set; }
    public DateTimeOffset? PagadaEn { get; private set; }

    /// <summary>Cuenta contra el tope del origen (T9.6-a): solicitada, autorizada o ya pagada.</summary>
    public bool Comprometida => Estado is EstadoDevolucionCliente.Solicitada or EstadoDevolucionCliente.Autorizada or EstadoDevolucionCliente.Pagada;

    private DevolucionCliente() { }

    public static DevolucionCliente SolicitarPorDepositoErroneo(
        Guid empresaId, MovimientoBancario deposito, Guid clienteId, decimal importe, decimal disponible,
        string motivo, CuentaDestinoDevolucion destino, Guid solicitadaPor, DateTimeOffset ahora)
    {
        if (deposito.Sentido != SentidoMovimiento.Ingreso)
            throw new BusinessRuleException("DEV_DEPOSITO_NO_INGRESO", "Solo un depósito (movimiento de ingreso) puede devolverse.");
        if (deposito.ContramovimientoDe is not null)
            throw new BusinessRuleException("DEV_DEPOSITO_ES_REVERSA", "Un contramovimiento no puede devolverse.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("DEV_CLIENTE_VACIO", "Selecciona el cliente al que se devuelve el dinero.");
        if (solicitadaPor == Guid.Empty)
            throw new BusinessRuleException("DEV_USUARIO_VACIO", "El usuario que solicita es obligatorio.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DEV_MOTIVO_VACIO", "Indica el motivo de la devolución.");
        if (importe <= 0)
            throw new BusinessRuleException("DEV_IMPORTE_INVALIDO", "El importe debe ser mayor a cero.");
        if (importe > disponible)
            throw new BusinessRuleException("DEV_IMPORTE_EXCEDE_DEPOSITO",
                $"La devolución ({importe:N2}) excede lo disponible del depósito ({disponible:N2}). El depósito es de {deposito.Monto:N2} y el resto ya está comprometido en otras devoluciones.");
        var (banco, clabe, titular) = ValidarDestino(destino);

        return new DevolucionCliente
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            SucursalId = deposito.SucursalId,
            Origen = OrigenDevolucionCliente.DepositoErroneo,
            MovimientoOrigenId = deposito.Id,
            ImporteMaximoOrigen = deposito.Monto,
            ClienteId = clienteId,
            Importe = importe,
            Moneda = deposito.Moneda,
            Motivo = motivo.Trim(),
            DestinoBanco = banco,
            DestinoClabe = clabe,
            DestinoTitular = titular,
            Estado = EstadoDevolucionCliente.Solicitada,
            SolicitadaPor = solicitadaPor,
            SolicitadaEn = ahora,
        };
    }

    public static DevolucionCliente SolicitarPorNotaCredito(
        Guid empresaId, Guid notaCreditoId, string folio, string? uuid, Guid clienteId, decimal importe,
        decimal totalNotaCredito, string moneda, Guid? sucursalId, string motivo, Guid? solicitadaPor, DateTimeOffset ahora)
    {
        if (notaCreditoId == Guid.Empty)
            throw new BusinessRuleException("DEV_NC_VACIA", "La nota de crédito es obligatoria.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("DEV_CLIENTE_VACIO", "Selecciona el cliente al que se devuelve el dinero.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DEV_MOTIVO_VACIO", "Indica el motivo de la devolución.");
        if (importe <= 0)
            throw new BusinessRuleException("DEV_IMPORTE_INVALIDO", "El importe debe ser mayor a cero.");
        if (importe > totalNotaCredito)
            throw new BusinessRuleException("DEV_IMPORTE_EXCEDE_NC", "La devolución excede el total de la nota de crédito.");

        return new DevolucionCliente
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            SucursalId = sucursalId,
            Origen = OrigenDevolucionCliente.NotaCredito,
            NotaCreditoId = notaCreditoId,
            NotaCreditoFolio = folio,
            NotaCreditoUuid = uuid,
            ImporteMaximoOrigen = totalNotaCredito,
            ClienteId = clienteId,
            Importe = importe,
            Moneda = moneda.ToUpperInvariant(),
            Motivo = motivo.Trim(),
            Estado = EstadoDevolucionCliente.Solicitada,
            SolicitadaPor = solicitadaPor,
            SolicitadaEn = ahora,
        };
    }

    public void Editar(Guid clienteId, decimal importe, decimal disponible, string motivo, CuentaDestinoDevolucion? destino)
    {
        if (Estado != EstadoDevolucionCliente.Solicitada)
            throw new BusinessRuleException("DEV_NO_EDITABLE", "Solo una devolución solicitada puede editarse.");
        if (Origen == OrigenDevolucionCliente.NotaCredito && clienteId != ClienteId)
            throw new BusinessRuleException("DEV_CLIENTE_NO_EDITABLE", "El cliente de un reembolso de nota de crédito no puede cambiarse.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("DEV_CLIENTE_VACIO", "Selecciona el cliente al que se devuelve el dinero.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DEV_MOTIVO_VACIO", "Indica el motivo de la devolución.");
        if (importe <= 0)
            throw new BusinessRuleException("DEV_IMPORTE_INVALIDO", "El importe debe ser mayor a cero.");
        if (importe > disponible)
        {
            if (Origen == OrigenDevolucionCliente.DepositoErroneo)
                throw new BusinessRuleException("DEV_IMPORTE_EXCEDE_DEPOSITO",
                    $"La devolución ({importe:N2}) excede lo disponible del depósito ({disponible:N2}). El depósito es de {ImporteMaximoOrigen:N2} y el resto ya está comprometido en otras devoluciones.");
            throw new BusinessRuleException("DEV_IMPORTE_EXCEDE_NC", "La devolución excede el total de la nota de crédito.");
        }
        if (destino is null && Origen == OrigenDevolucionCliente.DepositoErroneo)
            throw new BusinessRuleException("DEV_DESTINO_INCOMPLETO", "Captura banco, CLABE y titular de la cuenta destino antes de autorizar.");

        if (destino is not null)
        {
            var (banco, clabe, titular) = ValidarDestino(destino);
            DestinoBanco = banco;
            DestinoClabe = clabe;
            DestinoTitular = titular;
        }
        ClienteId = clienteId;
        Importe = importe;
        Motivo = motivo.Trim();
    }

    public void Cancelar(string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        AsegurarSolicitada();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DEV_MOTIVO_VACIO", "Indica el motivo de la devolución.");
        Estado = EstadoDevolucionCliente.Cancelada;
        MotivoCancelacion = motivo.Trim();
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    public void Autorizar(Guid usuarioId, DateTimeOffset ahora)
    {
        AsegurarSolicitada();
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("DEV_USUARIO_VACIO", "El usuario que autoriza es obligatorio.");
        if (SolicitadaPor == usuarioId)
            throw new BusinessRuleException("DEV_MISMO_USUARIO",
                "Quien solicita la devolución no puede autorizarla. Pide la autorización al Gerente o al Jefe de Tesorería.");
        if (DestinoBanco is null || DestinoClabe is null || DestinoTitular is null)
            throw new BusinessRuleException("DEV_DESTINO_INCOMPLETO", "Captura banco, CLABE y titular de la cuenta destino antes de autorizar.");
        Estado = EstadoDevolucionCliente.Autorizada;
        AutorizadaPor = usuarioId;
        AutorizadaEn = ahora;
    }

    public void Rechazar(string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        AsegurarSolicitada();
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DEV_MOTIVO_VACIO", "Indica el motivo de la devolución.");
        Estado = EstadoDevolucionCliente.Rechazada;
        MotivoRechazo = motivo.Trim();
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    public void MarcarPagada(Guid movimientoPagoId, Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoDevolucionCliente.Autorizada)
            throw new BusinessRuleException("DEV_NO_AUTORIZADA", "Solo una devolución autorizada puede pagarse.");
        MovimientoPagoId = movimientoPagoId;
        PagadaPor = usuarioId;
        PagadaEn = ahora;
        Estado = EstadoDevolucionCliente.Pagada;
    }

    private void AsegurarSolicitada()
    {
        if (Estado != EstadoDevolucionCliente.Solicitada)
            throw new BusinessRuleException("DEV_NO_PENDIENTE", "Solo una devolución solicitada puede cancelarse, autorizarse o rechazarse.");
    }

    private static (string Banco, string Clabe, string Titular) ValidarDestino(CuentaDestinoDevolucion destino)
    {
        if (string.IsNullOrWhiteSpace(destino.Banco) || destino.Banco.Trim().Length > 80
            || string.IsNullOrWhiteSpace(destino.Titular) || destino.Titular.Trim().Length > 200)
            throw new BusinessRuleException("DEV_DESTINO_INCOMPLETO", "Captura banco, CLABE y titular de la cuenta destino antes de autorizar.");
        return (destino.Banco.Trim(), Clabe.Crear(destino.Clabe).Valor, destino.Titular.Trim());
    }
}
