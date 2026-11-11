using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Proformas;

/// <summary>
/// M-234: conformidad del cliente con UNA versión de la proforma (quién, cuándo y por qué medio).
/// Vive en su propia tabla (una fila por proforma como máximo) para que registrarla no modifique la
/// proforma entregada, que es inmutable; aprobar la versión 2 no toca la 1.
/// </summary>
public sealed class ProformaConformidad : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ProformaPedidoId { get; private set; }

    /// <summary>Persona del cliente que dio la conformidad.</summary>
    public string Quien { get; private set; } = string.Empty;

    /// <summary>Día en que el cliente dio su conformidad.</summary>
    public DateOnly Fecha { get; private set; }

    public MedioConformidadProforma Medio { get; private set; }
    public string? Notas { get; private set; }

    public Guid? RegistradaPorUsuarioId { get; private set; }
    public string? RegistradaPorNombre { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }

    private ProformaConformidad() { }

    private ProformaConformidad(
        Guid id, Guid empresaId, Guid proformaPedidoId, string quien, DateOnly fecha, MedioConformidadProforma medio,
        string? notas, Guid? usuarioId, string? usuarioNombre, DateTimeOffset registradaEn) : base(id)
    {
        EmpresaId = empresaId;
        ProformaPedidoId = proformaPedidoId;
        Quien = quien;
        Fecha = fecha;
        Medio = medio;
        Notas = notas;
        RegistradaPorUsuarioId = usuarioId;
        RegistradaPorNombre = usuarioNombre;
        RegistradaEn = registradaEn;
    }

    /// <summary>
    /// Registra la conformidad de <paramref name="proforma"/>. Una proforma vencida (<paramref name="hoy"/> posterior
    /// a su vigencia) no se puede aprobar. <paramref name="diaGenerada"/> es el día fiscal de generación de la proforma.
    /// </summary>
    public static ProformaConformidad Registrar(
        ProformaPedido proforma, string quien, DateOnly fecha, MedioConformidadProforma medio, string? notas,
        Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora, DateOnly hoy, DateOnly diaGenerada)
    {
        ArgumentNullException.ThrowIfNull(proforma);
        if (proforma.VencidaEl(hoy))
            throw new BusinessRuleException(
                "FAC_PROFORMA_VENCIDA",
                $"La proforma {proforma.Folio} venció el {proforma.VigenteHasta:dd/MM/yyyy}; genera una versión nueva para registrar la conformidad.");
        if (string.IsNullOrWhiteSpace(quien))
            throw new BusinessRuleException("FAC_PROFORMA_CONFORMIDAD_SIN_QUIEN", "Indica quién del cliente dio la conformidad.");
        if (!Enum.IsDefined(medio))
            throw new BusinessRuleException("FAC_PROFORMA_CONFORMIDAD_MEDIO_INVALIDO", "El medio de la conformidad no es válido.");
        if (fecha > hoy)
            throw new BusinessRuleException("FAC_PROFORMA_CONFORMIDAD_FECHA_FUTURA", "La fecha de la conformidad no puede ser futura.");
        if (fecha < diaGenerada)
            throw new BusinessRuleException("FAC_PROFORMA_CONFORMIDAD_FECHA_ANTERIOR", "La conformidad no puede ser anterior a la fecha en que se generó la proforma.");

        return new(Guid.CreateVersion7(), proforma.EmpresaId, proforma.Id, quien.Trim(), fecha, medio,
            string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(), usuarioId, usuarioNombre, ahora);
    }
}
