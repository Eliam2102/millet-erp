using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
namespace Millet.DatosMaestros.Domain;

public enum OrigenFiscalProducto { Pendiente, ReglaFamilia, ExcepcionProducto }
public enum EstadoFiscalProducto { FiscalPendiente, PorReglaSinRevisar, Confirmada }
public sealed record FichaFiscalProducto(string? ClaveProdServSat, string? ClaveUnidadSat, string? ObjetoImp,
    decimal? TasaIvaTraslado, decimal? TasaRetencionIva = null, decimal? TasaRetencionIsr = null)
{
    public IReadOnlyList<string> Faltantes => new[] {
        string.IsNullOrWhiteSpace(ClaveProdServSat) ? "Clave de producto SAT" : null,
        string.IsNullOrWhiteSpace(ClaveUnidadSat) ? "Clave de unidad SAT" : null,
        string.IsNullOrWhiteSpace(ObjetoImp) ? "Objeto de impuesto" : null,
        TasaIvaTraslado is null ? "Tasa de IVA" : null }.OfType<string>().ToArray();
    public string? CampoInvalido(bool completa)
    {
        if ((completa && string.IsNullOrWhiteSpace(ClaveProdServSat)) || (ClaveProdServSat is not null && !Regex.IsMatch(ClaveProdServSat, "^[0-9]{8}$"))) return "Clave de producto SAT";
        if ((completa && string.IsNullOrWhiteSpace(ClaveUnidadSat)) || (ClaveUnidadSat is not null && !Regex.IsMatch(ClaveUnidadSat, "^[A-Z0-9]{1,5}$"))) return "Clave de unidad SAT";
        if ((completa && string.IsNullOrWhiteSpace(ObjetoImp)) || (ObjetoImp is not null && !Regex.IsMatch(ObjetoImp, "^0[1-8]$"))) return "Objeto de impuesto";
        if ((completa && TasaIvaTraslado is null) || TasaIvaTraslado is < 0 or > 1) return "Tasa IVA";
        if (TasaRetencionIva is < 0 or > 1) return "Retención IVA";
        if (TasaRetencionIsr is < 0 or > 1) return "Retención ISR";
        return null;
    }
    public void Validar(bool completa)
    {
        if (CampoInvalido(completa) is not null)
            throw new BusinessRuleException("FICHA_FISCAL_INVALIDA", "Indica clave de producto SAT de ocho dígitos, unidad SAT, objeto de impuesto 01–08 y tasas entre 0 y 1. " + string.Join(", ", Faltantes));
    }
}
public sealed class ReglaFiscalAw : BaseEntity, IAuditable
{
    // Tipo reutiliza #79; Grupo/Wgr permiten equivalencias más específicas, sin campos nuevos en A+W.
    public string Campo { get; private set; } = "Tipo";
    public string Valor { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool Activa { get; private set; } = true;
    public FichaFiscalProducto Ficha { get; private set; } = null!;
    private ReglaFiscalAw() { }
    public ReglaFiscalAw(Guid id, string campo, string valor, string descripcion, FichaFiscalProducto ficha) : base(id) => Editar(campo,valor,descripcion,ficha,true);
    public void Editar(string campo, string valor, string descripcion, FichaFiscalProducto ficha, bool activa)
    {
        if (campo is not ("Tipo" or "Grupo" or "Wgr") || string.IsNullOrWhiteSpace(valor) || valor.Trim().Length > 100 || descripcion.Length > 500)
            throw new BusinessRuleException("REGLA_FISCAL_INVALIDA", "Elige Tipo, Grupo o Wgr e indica un valor de hasta 100 caracteres.");
        ficha.Validar(true); Campo = campo; Valor = valor.Trim(); Descripcion = descripcion; Ficha = ficha; Activa = activa;
    }
    public bool Coincide(ProductoAw p) => Activa && string.Equals(Valor, Campo switch { "Tipo" => p.Tipo, "Grupo" => p.Grupo, _ => p.Wgr }, StringComparison.OrdinalIgnoreCase);
}
public sealed class CambioFiscalAw : BaseEntity, IAuditable
{
    public Guid EntidadId { get; private set; }
    public string TipoEntidad { get; private set; } = string.Empty;
    public string Accion { get; private set; } = string.Empty;
    public string AntesJson { get; private set; } = string.Empty;
    public string DespuesJson { get; private set; } = string.Empty;
    public Guid? UsuarioId { get; private set; }
    private CambioFiscalAw() { }
    public CambioFiscalAw(Guid id, Guid entidadId, string tipoEntidad, string accion, string antes, string despues, Guid? usuario) : base(id)
    { EntidadId=entidadId; TipoEntidad=tipoEntidad; Accion=accion; AntesJson=antes; DespuesJson=despues; UsuarioId=usuario; }
}
