using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Domain.Cheques;

public enum EstadoChequeRecibido : short
{
    /// <summary>Recibido y depositado; el banco aún no lo acredita. No suma al saldo.</summary>
    EnFirmePendiente = 1,
    /// <summary>El banco lo acreditó: ya existe el ingreso en libros.</summary>
    Disponible = 2,
    /// <summary>El banco lo devolvió. Si ya estaba disponible, el ingreso se compensó con su contramovimiento.</summary>
    Rebotado = 3,
}

/// <summary>
/// M-372 (TES-09): cheque de cliente recibido y depositado en una cuenta propia. Nace «en firme pendiente» y no
/// genera movimiento bancario, así no suma al saldo. Al confirmarse (línea del estado de cuenta o a mano con la
/// referencia del banco) nace el ingreso y pasa a disponible. Si rebota se marca así; si ya estaba disponible, el
/// ingreso se compensa con su contramovimiento. El aviso a CxC lo publica el handler.
/// </summary>
public sealed class ChequeRecibido : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CuentaBancariaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public string BancoEmisor { get; private set; } = default!;
    public string Numero { get; private set; } = default!;
    public string Librador { get; private set; } = default!;
    public decimal Importe { get; private set; }
    public string Moneda { get; private set; } = default!;
    public DateOnly FechaRecepcion { get; private set; }
    public EstadoChequeRecibido Estado { get; private set; } = EstadoChequeRecibido.EnFirmePendiente;
    public Guid RegistradoPor { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }

    /// <summary>Ingreso que nació al confirmarse.</summary>
    public Guid? MovimientoId { get; private set; }
    /// <summary>Línea del estado de cuenta con que se confirmó (null si fue a mano).</summary>
    public Guid? ExtractoLineaId { get; private set; }
    public Guid? ConfirmadoPor { get; private set; }
    public DateTimeOffset? ConfirmadoEn { get; private set; }

    public Guid? ContramovimientoId { get; private set; }
    public string? MotivoRebote { get; private set; }
    public Guid? RebotadoPor { get; private set; }
    public DateTimeOffset? RebotadoEn { get; private set; }

    private ChequeRecibido() { }

    public static ChequeRecibido Registrar(Guid empresaId, CuentaBancaria cuenta, Guid clienteId, string bancoEmisor, string numero,
        string librador, decimal importe, DateOnly fechaRecepcion, Guid usuarioId, DateTimeOffset ahora)
    {
        if (!cuenta.Activa)
            throw new BusinessRuleException("CHEQUE_CUENTA_INACTIVA", "La cuenta donde se deposita el cheque está inactiva.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("CHEQUE_CLIENTE_OBLIGATORIO", "Indica el cliente que entregó el cheque.");
        if (string.IsNullOrWhiteSpace(bancoEmisor) || string.IsNullOrWhiteSpace(numero) || string.IsNullOrWhiteSpace(librador))
            throw new BusinessRuleException("CHEQUE_DATOS_OBLIGATORIOS", "Banco, número y librador del cheque son obligatorios.");
        if (importe <= 0 || decimal.Round(importe, 2) != importe)
            throw new BusinessRuleException("CHEQUE_IMPORTE_INVALIDO", "El importe debe ser mayor a cero y con dos decimales.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CHEQUE_USUARIO_VACIO", "El usuario que registra es obligatorio.");
        return new ChequeRecibido
        {
            Id = Guid.CreateVersion7(), EmpresaId = empresaId, CuentaBancariaId = cuenta.Id, ClienteId = clienteId,
            BancoEmisor = bancoEmisor.Trim(), Numero = numero.Trim(), Librador = librador.Trim(), Importe = importe,
            Moneda = cuenta.Moneda, FechaRecepcion = fechaRecepcion, RegistradoPor = usuarioId, RegistradoEn = ahora,
        };
    }

    /// <summary>
    /// Pasa a disponible: crea el ingreso en libros por el importe del cheque. <paramref name="extractoLineaId"/> se anota
    /// cuando la confirmación viene del estado de cuenta (el handler empareja la línea con el ingreso).
    /// </summary>
    public MovimientoBancario Confirmar(CuentaBancaria cuenta, DateOnly fechaValor, string referenciaBancaria,
        Guid? extractoLineaId, Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoChequeRecibido.EnFirmePendiente)
            throw new BusinessRuleException("CHEQUE_NO_PENDIENTE", "Solo un cheque en firme pendiente puede confirmarse.");
        if (cuenta.Id != CuentaBancariaId)
            throw new BusinessRuleException("CHEQUE_CUENTA_DISTINTA", "El ingreso debe ser en la cuenta donde se depositó el cheque.");
        if (string.IsNullOrWhiteSpace(referenciaBancaria))
            throw new BusinessRuleException("CHEQUE_REFERENCIA_OBLIGATORIA", "Indica la referencia con que el banco acreditó el cheque.");
        if (fechaValor < FechaRecepcion)
            throw new BusinessRuleException("CHEQUE_FECHA_INVALIDA", "El banco no puede acreditar el cheque antes de recibirlo.");

        var mov = MovimientoBancario.RegistrarIngreso(EmpresaId, cuenta, Importe, fechaValor, referenciaBancaria, null,
            BeneficiarioTipo.Cliente, ClienteId, usuarioId, ahora);
        Estado = EstadoChequeRecibido.Disponible;
        MovimientoId = mov.Id;
        ExtractoLineaId = extractoLineaId;
        ConfirmadoPor = usuarioId;
        ConfirmadoEn = ahora;
        return mov;
    }

    /// <summary>
    /// El banco devolvió el cheque. Si ya estaba disponible regresa el contramovimiento (egreso por el importe) que
    /// compensa el ingreso; si seguía pendiente no hay nada que compensar y regresa null.
    /// </summary>
    public MovimientoBancario? Rebotar(MovimientoBancario? ingreso, DateOnly fecha, string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado == EstadoChequeRecibido.Rebotado)
            throw new BusinessRuleException("CHEQUE_YA_REBOTADO", "El cheque ya está marcado como rebotado.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CHEQUE_MOTIVO_OBLIGATORIO", "Indica el motivo de la devolución del banco.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CHEQUE_USUARIO_VACIO", "El usuario que registra es obligatorio.");

        MovimientoBancario? contra = null;
        if (Estado == EstadoChequeRecibido.Disponible)
        {
            if (ingreso is null || ingreso.Id != MovimientoId)
                throw new BusinessRuleException("CHEQUE_INGRESO_INVALIDO", "El ingreso del cheque no coincide.");
            if (fecha < ingreso.FechaValor)
                throw new BusinessRuleException("CHEQUE_FECHA_INVALIDA", "La devolución no puede ser anterior al ingreso del cheque.");
            contra = ingreso.CrearContramovimiento(Importe, fecha, usuarioId, ahora, $"Cheque {Numero} rebotado: {motivo.Trim()}");
            ContramovimientoId = contra.Id;
        }
        Estado = EstadoChequeRecibido.Rebotado;
        MotivoRebote = motivo.Trim();
        RebotadoPor = usuarioId;
        RebotadoEn = ahora;
        return contra;
    }
}
