using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Master local de tarjetas corporativas (§3.1, §4.1 del anexo TC,
/// F7-PR4). Cada <see cref="Tarjeta"/> tiene un titular fijo (firma el
/// estado de cuenta) y N usuarios autorizados.
///
/// <para>
/// El número completo nunca se almacena — solo
/// <see cref="NumeroTarjetaEnmascarado"/>. El banco proveedor
/// (<see cref="BancoProveedorId"/>) es un <c>Proveedor</c> especial tipo
/// <c>BancoEmisorTC</c> registrado en DatosMaestros.
/// </para>
/// </summary>
public sealed class Tarjeta : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public string Emisora { get; private set; } = default!;        // 'Amex', 'Banamex'
    public string PerfilParser { get; private set; } = default!;   // FK lógica al perfil del parser bancario
    public NumeroTarjetaEnmascarado Numero { get; private set; } = default!;
    public string NombreAlias { get; private set; } = default!;    // 'Amex Corporativa Dirección'
    public Guid TitularId { get; private set; }
    public Guid BancoProveedorId { get; private set; }

    public decimal LimiteCreditoMxn { get; private set; }
    public string MonedaDefault { get; private set; } = "MXN";

    public short DiaCorte { get; private set; }       // 1-31
    public short DiaLimitePago { get; private set; }  // offset desde corte (en días)

    public EstadoTarjeta Estado { get; private set; }
    public DateOnly? FechaBloqueo { get; private set; }
    public string? MotivoBloqueo { get; private set; }

    public DateOnly VigenciaDesde { get; private set; }
    public DateOnly? VigenciaHasta { get; private set; }

    private readonly List<TarjetaUsuarioAutorizado> _usuariosAutorizados = [];
    public IReadOnlyCollection<TarjetaUsuarioAutorizado> UsuariosAutorizados =>
        _usuariosAutorizados.AsReadOnly();

    private Tarjeta() { }

    public static Tarjeta Crear(
        Guid empresaId,
        string emisora,
        string perfilParser,
        NumeroTarjetaEnmascarado numero,
        string nombreAlias,
        Guid titularId,
        Guid bancoProveedorId,
        decimal limiteCreditoMxn,
        string monedaDefault,
        short diaCorte,
        short diaLimitePago,
        DateOnly vigenciaDesde)
    {
        if (string.IsNullOrWhiteSpace(emisora))
            throw new BusinessRuleException("TC_EMISORA_VACIA", "La emisora es obligatoria.");
        if (string.IsNullOrWhiteSpace(perfilParser))
            throw new BusinessRuleException("TC_PERFIL_PARSER_VACIO", "El perfil de parser es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombreAlias))
            throw new BusinessRuleException("TC_ALIAS_VACIO", "El nombre alias es obligatorio.");
        if (titularId == Guid.Empty)
            throw new BusinessRuleException("TC_TITULAR_VACIO", "El titular es obligatorio.");
        if (bancoProveedorId == Guid.Empty)
            throw new BusinessRuleException("TC_BANCO_VACIO", "El banco proveedor es obligatorio.");
        if (limiteCreditoMxn <= 0)
            throw new BusinessRuleException("TC_LIMITE_INVALIDO",
                "El límite de crédito debe ser > 0.");
        if (string.IsNullOrWhiteSpace(monedaDefault) || monedaDefault.Length != 3)
            throw new BusinessRuleException("TC_MONEDA_INVALIDA",
                "La moneda default debe ser código ISO 4217 de 3 letras.");
        if (diaCorte < 1 || diaCorte > 31)
            throw new BusinessRuleException("TC_DIA_CORTE_INVALIDO",
                "El día de corte debe estar entre 1 y 31.");
        if (diaLimitePago < 1 || diaLimitePago > 60)
            throw new BusinessRuleException("TC_DIA_LIMITE_INVALIDO",
                "El día límite de pago (offset) debe estar entre 1 y 60.");

        return new Tarjeta
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            Emisora = emisora.Trim(),
            PerfilParser = perfilParser.Trim(),
            Numero = numero,
            NombreAlias = nombreAlias.Trim(),
            TitularId = titularId,
            BancoProveedorId = bancoProveedorId,
            LimiteCreditoMxn = limiteCreditoMxn,
            MonedaDefault = monedaDefault.ToUpperInvariant(),
            DiaCorte = diaCorte,
            DiaLimitePago = diaLimitePago,
            Estado = EstadoTarjeta.Activa,
            VigenciaDesde = vigenciaDesde,
        };
    }

    public void ActualizarDatos(
        string nombreAlias,
        decimal limiteCreditoMxn,
        short diaCorte,
        short diaLimitePago)
    {
        if (Estado == EstadoTarjeta.Cancelada)
            throw new BusinessRuleException("TC_CANCELADA_NO_EDITABLE",
                "Una tarjeta Cancelada no se puede editar.");
        if (string.IsNullOrWhiteSpace(nombreAlias))
            throw new BusinessRuleException("TC_ALIAS_VACIO", "El nombre alias es obligatorio.");
        if (limiteCreditoMxn <= 0)
            throw new BusinessRuleException("TC_LIMITE_INVALIDO",
                "El límite de crédito debe ser > 0.");
        if (diaCorte < 1 || diaCorte > 31)
            throw new BusinessRuleException("TC_DIA_CORTE_INVALIDO",
                "El día de corte debe estar entre 1 y 31.");
        if (diaLimitePago < 1 || diaLimitePago > 60)
            throw new BusinessRuleException("TC_DIA_LIMITE_INVALIDO",
                "El día límite de pago (offset) debe estar entre 1 y 60.");

        NombreAlias = nombreAlias.Trim();
        LimiteCreditoMxn = limiteCreditoMxn;
        DiaCorte = diaCorte;
        DiaLimitePago = diaLimitePago;
    }

    public void Bloquear(string motivo, DateOnly fecha)
    {
        if (Estado != EstadoTarjeta.Activa)
            throw new BusinessRuleException("TC_NO_BLOQUEABLE",
                $"Solo se bloquea desde Activa (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("TC_MOTIVO_BLOQUEO_VACIO",
                "El motivo de bloqueo es obligatorio.");

        Estado = EstadoTarjeta.Bloqueada;
        FechaBloqueo = fecha;
        MotivoBloqueo = motivo.Trim();
    }

    public void Reactivar()
    {
        if (Estado != EstadoTarjeta.Bloqueada)
            throw new BusinessRuleException("TC_NO_REACTIVABLE",
                $"Solo se reactiva desde Bloqueada (actual: {Estado}).");

        Estado = EstadoTarjeta.Activa;
        FechaBloqueo = null;
        MotivoBloqueo = null;
    }

    public void Cancelar(DateOnly fecha)
    {
        if (Estado == EstadoTarjeta.Cancelada)
            throw new BusinessRuleException("TC_YA_CANCELADA",
                "La tarjeta ya está cancelada.");

        Estado = EstadoTarjeta.Cancelada;
        VigenciaHasta = fecha;
    }

    public TarjetaUsuarioAutorizado AgregarUsuarioAutorizado(
        Guid empleadoId,
        DateOnly vigenciaDesde,
        DateOnly? vigenciaHasta,
        decimal? montoMaxMensualMxn)
    {
        if (Estado == EstadoTarjeta.Cancelada)
            throw new BusinessRuleException("TC_CANCELADA_NO_EDITABLE",
                "No se puede agregar usuarios a una tarjeta cancelada.");

        var existe = _usuariosAutorizados.Any(u =>
            u.EmpleadoId == empleadoId
            && (u.VigenciaHasta is null || u.VigenciaHasta >= vigenciaDesde));
        if (existe)
        {
            throw new BusinessRuleException(
                "TC_USR_DUPLICADO",
                $"El empleado {empleadoId} ya tiene una autorización vigente en esta tarjeta.");
        }

        var usuario = new TarjetaUsuarioAutorizado(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaId,
            tarjetaId: Id,
            empleadoId: empleadoId,
            vigenciaDesde: vigenciaDesde,
            vigenciaHasta: vigenciaHasta,
            montoMaxMensualMxn: montoMaxMensualMxn);

        _usuariosAutorizados.Add(usuario);
        return usuario;
    }

    public void CerrarUsuarioAutorizado(Guid usuarioId, DateOnly fecha)
    {
        var usr = _usuariosAutorizados.FirstOrDefault(u => u.Id == usuarioId)
            ?? throw new EntityNotFoundException(
                "TC_USR_NO_ENCONTRADO",
                $"No se encontró el usuario autorizado '{usuarioId}'.");
        usr.Cerrar(fecha);
    }

    /// <summary>
    /// Verifica que la tarjeta esté disponible para registrar un cargo
    /// en una fecha dada. Las tarjetas <c>Bloqueada</c> no aceptan
    /// movimientos con <c>fechaMovimiento &gt; FechaBloqueo</c>.
    /// </summary>
    public void AsegurarPuedeAceptarCargo(DateOnly fechaMovimiento)
    {
        if (Estado == EstadoTarjeta.Cancelada)
        {
            throw new BusinessRuleException("TC_CANCELADA_SIN_MOVIMIENTOS",
                "La tarjeta está cancelada — no acepta nuevos movimientos.");
        }
        if (Estado == EstadoTarjeta.Bloqueada
            && FechaBloqueo is DateOnly bloqueo
            && fechaMovimiento > bloqueo)
        {
            throw new BusinessRuleException(
                "TC_BLOQUEADA_FECHA_POSTERIOR",
                $"La tarjeta fue bloqueada el {bloqueo:O} — no acepta cargos con fecha posterior.");
        }
        if (fechaMovimiento < VigenciaDesde)
        {
            throw new BusinessRuleException(
                "TC_FUERA_DE_VIGENCIA",
                $"La fecha del cargo es anterior a la vigencia de la tarjeta ({VigenciaDesde:O}).");
        }
        if (VigenciaHasta is DateOnly hasta && fechaMovimiento > hasta)
        {
            throw new BusinessRuleException(
                "TC_FUERA_DE_VIGENCIA",
                $"La fecha del cargo es posterior a la vigencia de la tarjeta ({hasta:O}).");
        }
    }

    /// <summary>
    /// Verifica que un empleado esté autorizado a usar esta TC en la
    /// fecha del movimiento. El titular siempre puede usar la TC.
    /// </summary>
    public void AsegurarUsuarioAutorizado(Guid empleadoId, DateOnly fechaMovimiento)
    {
        if (empleadoId == TitularId) return;

        var autorizado = _usuariosAutorizados.Any(u =>
            u.EmpleadoId == empleadoId && u.EstaVigente(fechaMovimiento));
        if (!autorizado)
        {
            throw new BusinessRuleException(
                "TC_USR_NO_AUTORIZADO",
                $"El empleado {empleadoId} no está autorizado a usar esta tarjeta el {fechaMovimiento:O}.");
        }
    }
}
