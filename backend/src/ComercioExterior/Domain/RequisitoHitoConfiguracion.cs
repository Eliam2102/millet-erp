using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed class RequisitoHitoConfiguracion : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public TipoExpediente Tipo { get; private set; }
    public Guid TipoDocumentoId { get; private set; }
    public string HitoClave { get; private set; } = "";
    private RequisitoHitoConfiguracion() { }
    public RequisitoHitoConfiguracion(Guid empresa, TipoExpediente tipo, Guid documento, string hito) : base(Guid.CreateVersion7())
    { EmpresaId = empresa; Tipo = tipo; TipoDocumentoId = documento; Cambiar(hito); }
    public void Cambiar(string hito)
    {
        if (!HitoExpediente.Catalogo.Any(h => h.Clave == hito && h.Clave is not ("solicitado" or "cerrado")))
            throw new BusinessRuleException("CE_HITO_INVALIDO", "Seleccione el hito que requiere el documento.");
        HitoClave = hito;
    }
}
