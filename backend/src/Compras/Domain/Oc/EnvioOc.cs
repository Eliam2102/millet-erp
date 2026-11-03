using System.Net.Mail;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

public sealed class EnvioOc : BaseEntity, IAuditable, IBelongsToAggregate, IPerteneceAEmpresa
{
    public Guid OrdenCompraId { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid AggregateRootId => OrdenCompraId;
    public Guid PdfId { get; private set; }
    public string PdfBlobUrl { get; private set; } = "";
    public string Destinatario { get; private set; } = "";
    public Guid UsuarioId { get; private set; }
    public string UsuarioNombre { get; private set; } = "";
    public DateTimeOffset Fecha { get; private set; }
    public string Modo { get; private set; } = "";
    public string Resultado { get; private set; } = "pendiente";

    private EnvioOc() { }

    public static string? MotivoBloqueo(OrdenCompra oc)
    {
        var firmas = oc.Autorizaciones.Where(a => a.Ciclo == oc.CicloAutorizacion
            && a.Resultado == ResultadoAutorizacionOc.Autorizado).ToList();
        // Basta con las firmas que pidió su nivel: N1 en menor cuantía, N1 + N2 (personas distintas) sobre umbral.
        return oc.Estado == EstadoOrdenCompra.Autorizada
            && firmas.Any(a => a.Nivel == NivelAutorizacion.Nivel1)
            && (!oc.RequiereNivel2 || (firmas.Any(a => a.Nivel == NivelAutorizacion.Nivel2)
                && firmas.Select(a => a.UsuarioId).Distinct().Count() >= 2))
            ? null : "Autoriza la orden con las firmas que requiere su nivel antes de enviarla al proveedor.";
    }

    public EnvioOc(OrdenCompra oc, OrdenCompraPdf pdf, string? destinatario, string modo,
        Guid usuarioId, string usuarioNombre, DateTimeOffset fecha) : base(Guid.CreateVersion7())
    {
        if (MotivoBloqueo(oc) is { } motivo) throw new BusinessRuleException("OC_ENVIO_NO_AUTORIZADA", motivo);
        destinatario = destinatario?.Trim();
        if (string.IsNullOrEmpty(destinatario) || destinatario.Length > 254 || destinatario.Contains('\r')
            || destinatario.Contains('\n') || !MailAddress.TryCreate(destinatario, out var correo)
            || correo.Address != destinatario)
            throw new BusinessRuleException("OC_ENVIO_CORREO_REQUERIDO", "Captura un correo válido del proveedor antes de enviar la orden.");
        if (modo is not ("correo" or "manual"))
            throw new BusinessRuleException("OC_ENVIO_MODO_INVALIDO", "Selecciona envío por correo o registro manual.");
        if (usuarioId == Guid.Empty) throw new UnauthorizedAccessException("Sin usuario autenticado.");
        if (pdf.OrdenCompraId != oc.Id) throw new BusinessRuleException("OC_ENVIO_PDF_INVALIDO", "El PDF debe corresponder a esta orden.");
        OrdenCompraId = oc.Id;
        EmpresaId = oc.EmpresaId;
        PdfId = pdf.Id;
        PdfBlobUrl = pdf.BlobUrl;
        Destinatario = destinatario;
        UsuarioId = usuarioId;
        UsuarioNombre = usuarioNombre;
        Fecha = fecha;
        Modo = modo;
    }

    internal void Completar(bool simulado) => Resultado = Modo == "manual" ? "registrado" : simulado ? "simulado" : "aceptado";
    internal void Fallar() => Resultado = "fallido";
}
