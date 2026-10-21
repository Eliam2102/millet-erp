using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Domain;

public sealed class ClaseActivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Clave { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public Guid? CuentaActivoId { get; private set; }
    public Guid? CuentaDepreciacionId { get; private set; }
    public Guid? CuentaGastoId { get; private set; }
    public Guid? CuentaPuenteId { get; private set; }
    public decimal TasaFiscalAnual { get; private set; }
    public string Metodo { get; private set; } = "LineaRecta";
    public bool Activa { get; private set; } = true;
    private ClaseActivo() { }
    public ClaseActivo(Guid id, Guid empresa, string clave, string nombre, decimal tasa) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20 || !clave.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new BusinessRuleException("AF_CLAVE_INVALIDA", "La clave debe tener de 1 a 20 letras, números o guiones.");
        EmpresaId = empresa; Clave = clave.ToUpperInvariant();
        Editar(nombre, tasa, null, null, null, null, true);
    }
    public void Editar(string nombre, decimal tasa, Guid? activo, Guid? depreciacion, Guid? gasto, Guid? puente, bool activa)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 200 || tasa is < 0 or > 100 || activo == Guid.Empty || depreciacion == Guid.Empty || gasto == Guid.Empty || puente == Guid.Empty)
            throw new BusinessRuleException("AF_CLASE_INVALIDA", "Capture nombre, tasa de 0 a 100 y cuentas válidas.");
        Nombre = nombre.Trim(); TasaFiscalAnual = tasa; CuentaActivoId = activo; CuentaDepreciacionId = depreciacion;
        CuentaGastoId = gasto; CuentaPuenteId = puente; Activa = activa;
    }
}
