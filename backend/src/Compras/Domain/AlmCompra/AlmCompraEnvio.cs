using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.AlmCompra;

/// <summary>
/// Bandeja de salida ERP → Almacenes (86e3jvu83 · C3.2): un mensaje del contrato v6 (COMPRA_RECIBIDA,
/// COMPRA_CANCELADA, COMPRA_MODIFICADA) por fila. Se guarda en la misma transacción que el cambio de la OC.
/// <para>
/// Orden estricto por <see cref="Referencia"/> (folio de la OC) y <see cref="Secuencia"/>: no se escribe un mensaje
/// mientras el anterior de su referencia no tenga acuse PROCESADO/DUPLICADO; un ERROR detiene la referencia.
/// <see cref="MensajeId"/> es único (idempotencia): se deriva de la OC, el tipo y un ordinal.
/// </para>
/// Sin auditoría (ADR-0008): es una bandeja técnica; su historia es el estado, los intentos y el acuse de cada fila.
/// </summary>
public sealed class AlmCompraEnvio : BaseEntity, IPerteneceAEmpresa, INotAudited
{
    public const string TipoRecibida = "COMPRA_RECIBIDA";
    public const string TipoCancelada = "COMPRA_CANCELADA";
    public const string TipoModificada = "COMPRA_MODIFICADA";

    /// <summary>Por escribir (o por reintentar tras una falla del transporte).</summary>
    public const string Pendiente = "PENDIENTE";
    /// <summary>Escrito en el destino; falta el acuse de Almacenes.</summary>
    public const string Enviado = "ENVIADO";
    public const string Procesado = "PROCESADO";
    public const string Duplicado = "DUPLICADO";
    /// <summary>Destino final de lo que falla siempre: acuse ERROR, error local o reintentos agotados. Detiene la referencia.</summary>
    public const string Error = "ERROR";
    /// <summary>
    /// Almacenes rechazó una COMPRA_CANCELADA (ya hay entrada): el ERP revierte la cancelación y la fila queda como historia
    /// sin detener la referencia (el resto de los mensajes de la compra, p. ej. el pedimento, sigue su curso).
    /// </summary>
    public const string Rechazado = "RECHAZADO";

    public const int VersionContrato = 1;

    public Guid EmpresaId { get; set; }
    public Guid OrdenCompraId { get; private set; }
    public string Referencia { get; private set; } = string.Empty;
    /// <summary>Orden global de inserción (identidad de la base); el orden por referencia sale de aquí.</summary>
    public long Secuencia { get; private set; }
    public string MensajeId { get; private set; } = string.Empty;
    public string Tipo { get; private set; } = string.Empty;
    public int Ordinal { get; private set; }
    /// <summary>Sobre JSON completo. Vacío si el armado local falló (fila en ERROR).</summary>
    public string Sobre { get; private set; } = string.Empty;
    public string Estado { get; private set; } = Pendiente;
    public int Intentos { get; private set; }
    public DateTimeOffset? ProximoIntentoEn { get; private set; }
    public string? UltimoError { get; private set; }
    public string? CompraIdAlmacenes { get; private set; }
    public DateTimeOffset? EnviadoEn { get; private set; }
    public DateTimeOffset? AcuseEn { get; private set; }

    private AlmCompraEnvio() { }

    public AlmCompraEnvio(Guid id, Guid empresaId, Guid ordenCompraId, string referencia, string tipo, int ordinal,
        string mensajeId, string sobre) : base(id)
    {
        EmpresaId = empresaId; OrdenCompraId = ordenCompraId; Referencia = referencia; Tipo = tipo;
        Ordinal = ordinal; MensajeId = mensajeId; Sobre = sobre;
    }

    /// <summary>Fila que no se pudo armar: queda visible en ERROR y no se envía.</summary>
    public static AlmCompraEnvio ConErrorLocal(Guid id, Guid empresaId, Guid ordenCompraId, string referencia,
        string tipo, int ordinal, string mensajeId, string error)
    {
        var e = new AlmCompraEnvio(id, empresaId, ordenCompraId, referencia, tipo, ordinal, mensajeId, string.Empty);
        e.Estado = Error; e.UltimoError = Recortar(error);
        return e;
    }

    public bool Terminado => Estado is Procesado or Duplicado or Rechazado;

    public void MarcarEscrito(DateTimeOffset ahora)
    {
        Estado = Enviado; EnviadoEn = ahora; ProximoIntentoEn = null; UltimoError = null;
    }

    public void RegistrarFalla(string error, DateTimeOffset ahora, int maxIntentos)
    {
        Intentos++;
        UltimoError = Recortar(error);
        if (Intentos >= maxIntentos) { Estado = Error; ProximoIntentoEn = null; return; }
        // Espera creciente: 5 s, 10 s, 20 s ... tope 15 min.
        ProximoIntentoEn = ahora.AddSeconds(Math.Min(5 * Math.Pow(2, Intentos - 1), 900));
    }

    public void AplicarAcuse(string estado, string? compraId, string? error, DateTimeOffset ahora)
    {
        Estado = estado; CompraIdAlmacenes = compraId; AcuseEn = ahora;
        UltimoError = estado is Error or Rechazado ? Recortar(error ?? "Almacenes devolvió ERROR sin detalle.") : null;
    }

    /// <summary>Reintento manual de una fila que no llegó a escribirse (falla local o reintentos agotados).</summary>
    public void Reintentar(string? sobreReconstruido = null)
    {
        if (Estado != Error || EnviadoEn is not null)
            throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("ALM_COMPRA_NO_REINTENTABLE",
                "Solo se reintenta un mensaje con error que aún no se escribió en Almacenes; si Almacenes lo rechazó, se reprocesa allá.");
        if (sobreReconstruido is not null) Sobre = sobreReconstruido;
        Estado = Pendiente; Intentos = 0; ProximoIntentoEn = null; UltimoError = null;
    }

    // El error nunca lleva el sobre ni datos personales: solo texto corto.
    private static string Recortar(string s) => s.Length <= 500 ? s : s[..500];
}
