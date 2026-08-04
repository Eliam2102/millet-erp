namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Canal por el que un CFDI llegó al ERP (§9.1 del 00-levantamiento).
///
/// <para>
/// MVP soporta los 3 canales activos: descarga SAT (FiscalAPI), mailbox
/// dedicado y carga manual. <see cref="PortalProveedor"/> queda
/// reservado para post-MVP (§9.1 punto 4) — el enum lo expone para que
/// el modelo no necesite migrarse cuando se habilite.
/// </para>
/// </summary>
public enum CanalOrigenCfdi
{
    Desconocido     = 0,
    DescargaSat     = 1,
    Mailbox         = 2,
    CargaManual     = 3,
    PortalProveedor = 4,
}
