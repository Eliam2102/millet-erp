namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>Quién manda sobre un campo de un maestro sincronizado desde A+W y qué pasa si difiere.</summary>
public sealed record CampoDuenoAw(string Entidad, string Campo, string Dueno, string SiDifiere);

/// <summary>
/// Matriz campo → dueño (ADM-06). Versión <c>0-propuesta</c>: refleja el comportamiento actual
/// (razón social, RFC y CP fiscal son del ERP) hasta que Fiscal y TI la congelen (contrato I13.4).
/// </summary>
public static class MatrizCampoDuenoAw
{
    public const string Version = "0-propuesta";
    public const string DuenoAw = "AW";
    public const string DuenoErp = "ERP";

    private const string AplicaAw = "Se aplica A+W y queda el antes/después en la bitácora.";

    public static readonly IReadOnlyList<CampoDuenoAw> Campos =
    [
        new("Cliente", "Nombre de origen", DuenoAw, AplicaAw),
        new("Cliente", "Domicilio de origen", DuenoAw, AplicaAw),
        new("Cliente", "Condición de pago", DuenoAw, AplicaAw),
        new("Cliente", "Moneda de origen", DuenoAw, "Se registra; la moneda del cliente solo cambia al crear y con mapeo aprobado."),
        new("Cliente", "Límite de crédito de referencia", DuenoAw, "Solo informa; nunca cambia la línea de crédito de CxC."),
        new("Cliente", "Bloqueo A+W", DuenoAw, "Con mapeo aprobado bloquea o reactiva sin borrar historia; queda en la bitácora."),
        new("Cliente", "Estado A+W", DuenoAw, "Se registra; solo cambia el estatus con mapeo aprobado."),
        new("Cliente", "Razón social", DuenoErp, "Se conserva el ERP; A+W solo informa la diferencia."),
        new("Cliente", "RFC y CP fiscal", DuenoErp, "A+W solo completa si el ERP está vacío; nunca sobrescribe."),
        new("Cliente", "Teléfono y correo", DuenoErp, "A+W solo completa si el ERP está vacío."),
        new("Cliente", "Régimen, uso de CFDI, clasificación y moneda del cliente", DuenoErp, "Se conserva el ERP; A+W solo informa."),
        new("Cliente", "RFC repetido entre clientes de A+W", DuenoAw, "No se fusionan solos: va a revisión (pendiente) con su causa y se resuelve unificando los clientes o manteniéndolos separados; se cuenta en el reporte de la corrida."),
        new("ProductoAw", "Descripción", DuenoAw, "Se aplica A+W. Si el ERP la editó, queda conflicto pendiente hasta «Aplicar A+W»."),
        new("ProductoAw", "Unidad", DuenoAw, "Se aplica A+W si tiene equivalencia; si no, queda pendiente. Si el ERP la editó, queda conflicto pendiente hasta «Aplicar A+W»."),
        new("ProductoAw", "Medidas (variantes)", DuenoAw, "Cada medida es una variante; nunca se colapsan."),
        new("ProductoAw", "Baja A+W", DuenoAw, "Baja lógica sin borrar historia; solo se reactiva una baja que vino de A+W."),
        new("ProductoAw", "Claves SAT, objeto de impuesto, tasas y aduana", DuenoErp, "Nunca se sobrescriben desde A+W."),
    ];
}
