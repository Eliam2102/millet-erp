namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Estado del ciclo de vida de un <see cref="CfdiRecibido"/> (§4.1 del
/// 00-levantamiento).
///
/// <list type="bullet">
///   <item><see cref="PorProcesar"/>: tiene XML descargado y parseado;
///         espera captura. Es el estado inicial de cualquier CFDI nuevo
///         (descarga SAT, mailbox o carga manual).</item>
///   <item><see cref="ConvertidoEnPasivo"/>: ya se convirtió en
///         FacturaProveedor / NotaCreditoProveedor / AnticipoProveedor.</item>
///   <item><see cref="Duplicado"/>: otro CFDI con el mismo UUID ingresó
///         primero por un canal distinto.</item>
///   <item><see cref="Descartado"/>: el Auxiliar lo descartó (error de
///         emisión, no aplica, etc.) con motivo.</item>
/// </list>
///
/// <para>
/// PR-14 (doc 02 §13.10): el estado <c>MetadataOnly</c> (= 5) introducido
/// en PR-12 se eliminó al cambiar la rule a <c>SatQueryType=CFDI</c>: ahora
/// el Poller cosecha XMLs junto con metadata y el adapter siempre crea
/// CFDIs en estado <see cref="PorProcesar"/>. Si alguna BD tenía filas
/// históricas con estado = 5, deben tratarse como dato sucio (purgar o
/// re-cosechar manualmente).
/// </para>
/// </summary>
public enum EstadoCfdiRecibido
{
    PorProcesar        = 1,
    ConvertidoEnPasivo = 2,
    Duplicado          = 3,
    Descartado         = 4,
}
