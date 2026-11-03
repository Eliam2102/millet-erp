using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Depositos;

namespace Millet.Tesoreria.Application.Depositos;

/// <summary>Sección <c>Tesoreria:DepositoAutomatico</c> (86e3ke6yn). Umbral en MXN pendiente de validar con Millet (V30).</summary>
public sealed class DepositoAutomaticoOptions
{
    public const string SectionName = "Tesoreria:DepositoAutomatico";

    /// <summary>Un depósito igual o mayor a este monto (MXN) siempre pasa a revisión humana.</summary>
    public decimal UmbralMxn { get; set; } = 100_000m;

    /// <summary>Fragmentos de la descripción del extracto que delatan un cheque (sin mayúsculas). Solo en appsettings: Bind agrega a un arreglo con valor por omisión.</summary>
    public string[] PalabrasCheque { get; set; } = [];
}

/// <summary>
/// Datos que la regla necesita y no están en la línea ni en la propuesta. Los resuelve el llamador
/// (importación de extracto) antes de evaluar, para que la regla siga siendo pura.
/// </summary>
/// <param name="ImportadoPor">Usuario que importa el extracto (quedaría como confirmador).</param>
/// <param name="MonedaCuenta">Moneda de la cuenta bancaria del extracto.</param>
/// <param name="PeriodoAbierto">¿El período contable de la línea está abierto?</param>
/// <param name="MovimientoDeLineaId">Movimiento del libro ya emparejado con la línea, si existe.</param>
/// <param name="DevolucionEnCurso">¿Ese movimiento tiene una devolución a cliente solicitada o autorizada?</param>
public sealed record ContextoDepositoAutomatico(
    Guid ImportadoPor, string MonedaCuenta, bool PeriodoAbierto,
    Guid? MovimientoDeLineaId = null, bool DevolucionEnCurso = false);

public abstract record ResultadoReglaDeposito
{
    /// <summary>Confirma y aplica sin intervención humana; <paramref name="ReglaTexto"/> va a la bitácora.</summary>
    public sealed record AutoConfirmar(DepositoConfirmacion Deposito, string ReglaTexto) : ResultadoReglaDeposito;

    /// <summary>Hay candidato(s) pero un humano decide; <paramref name="Motivo"/> es texto para el usuario.</summary>
    public sealed record Revision(string Motivo, IReadOnlyList<DepositoConfirmacion> Candidatos) : ResultadoReglaDeposito;

    /// <summary>Ninguna propuesta pendiente coincide con la referencia: el depósito queda sin identificar.</summary>
    public sealed record SinCoincidencia : ResultadoReglaDeposito;
}

/// <summary>
/// 86e3ke6yn: decide si un abono del extracto se confirma solo. Auto únicamente si TODO se cumple:
/// referencia igual (normalizada) y monto exacto, respaldo de factura única saldada, no es cheque,
/// monto menor al umbral, un solo candidato, MXN. Cualquier duda va a revisión.
/// </summary>
public static class ReglaDepositoAutomatico
{
    public const string ReglaTextoAuto =
        "Referencia y monto exactos, una factura saldada, no es cheque, bajo el umbral, un solo candidato";

    public static ResultadoReglaDeposito Evaluar(
        ExtractoLinea linea, IReadOnlyList<DepositoConfirmacion> candidatos,
        DepositoAutomaticoOptions opciones, ContextoDepositoAutomatico contexto)
    {
        var referencia = Normalizar(linea.Referencia);
        if (linea.Sentido != Domain.Movimientos.SentidoMovimiento.Ingreso || referencia is null)
            return new ResultadoReglaDeposito.SinCoincidencia();

        var coinciden = candidatos
            .Where(c => c.PropuestaCxcId is not null && c.Estado == EstadoDepositoConfirmacion.Pendiente
                && Normalizar(c.DepositoRef) == referencia)
            .ToList();
        if (coinciden.Count == 0) return new ResultadoReglaDeposito.SinCoincidencia();

        ResultadoReglaDeposito Revision(string motivo) => new ResultadoReglaDeposito.Revision(motivo, coinciden);

        if (coinciden.Count > 1)
            return Revision($"Hay {coinciden.Count} propuestas pendientes con la misma referencia.");
        var d = coinciden[0];

        if (EsCheque(linea.Descripcion, opciones))
            return Revision("Cheque: se confirma a mano.");
        if (linea.Monto >= opciones.UmbralMxn)
            return Revision($"El monto ({linea.Monto:N2}) es igual o mayor al umbral de {opciones.UmbralMxn:N2} MXN.");
        if (!EsMxn(contexto.MonedaCuenta) || !EsMxn(d.Moneda))
            return Revision("La moneda no es MXN.");

        var facturas = DepositoMapper.ParsePayload(d.FacturasJson);
        if (facturas.Count == 0 || d.SaldoAFavorPorIdentificar > 0)
            return Revision("Anticipo o saldo a favor: la propuesta no se respalda solo con facturas.");
        if (facturas.Count > 1)
            return Revision($"La propuesta cubre {facturas.Count} facturas.");
        if (facturas[0].SaldoInsolutoDespues is not decimal saldo)
            return Revision("No se conoce el saldo de la factura tras el pago.");
        if (saldo > 0)
            return Revision($"Pago parcial: la factura queda con saldo de {saldo:N2}.");

        if (d.MontoEsperado != linea.Monto)
            return Revision($"El monto del banco ({linea.Monto:N2}) difiere del propuesto ({d.MontoEsperado:N2}).");

        if (d.MovimientoSugeridoId is Guid ligado && ligado != contexto.MovimientoDeLineaId)
            return Revision("La propuesta está ligada a otro movimiento bancario.");
        if (contexto.DevolucionEnCurso)
            return Revision("El ingreso tiene una devolución a cliente en curso.");
        if (d.PropuestoPor is null || d.PropuestoPor == contexto.ImportadoPor)
            return Revision("Quien importa el extracto propuso el cobro, o no se sabe quién lo propuso.");
        if (!contexto.PeriodoAbierto)
            return Revision("El período contable está cerrado.");

        return new ResultadoReglaDeposito.AutoConfirmar(d, ReglaTextoAuto);
    }

    private static bool EsMxn(string? moneda) => string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase);

    private static string? Normalizar(string? s)
    {
        var t = s?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    // Heurística de texto: no hay bandera de cheque en el extracto. Ante duda, revisión.
    private static bool EsCheque(string descripcion, DepositoAutomaticoOptions opciones)
    {
        return opciones.PalabrasCheque.Any(p => !string.IsNullOrWhiteSpace(p)
            && descripcion.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
