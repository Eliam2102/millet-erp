using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed class ParticipanteExpediente : BaseEntity, IAuditable, IPerteneceAEmpresa, IBelongsToAggregate
{
    public Guid EmpresaId { get; set; }
    public Guid ExpedienteId { get; private set; }
    public Guid AggregateRootId => ExpedienteId;
    public string Nombre { get; private set; } = "";
    public string Tipo { get; private set; } = "";
    public string? Contacto { get; private set; }
    public PapelParticipante Papel { get; private set; }
    public Guid? ProveedorId { get; private set; }
    public Guid? ClienteId { get; private set; }

    private ParticipanteExpediente()
    {
    }

    internal ParticipanteExpediente(Guid id, Guid empresa, Guid expediente, string nombre, string tipo, string? contacto, PapelParticipante papel, Guid? proveedor, Guid? cliente) : base(id)
    {
        EmpresaId = empresa;
        ExpedienteId = expediente;
        Editar(nombre, tipo, contacto, papel, proveedor, cliente);
    }

    internal void Editar(string nombre, string tipo, string? contacto, PapelParticipante papel, Guid? proveedor, Guid? cliente)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 200 || string.IsNullOrWhiteSpace(tipo) || tipo.Trim().Length > 80 || contacto?.Length > 300 || !Enum.IsDefined(papel) || proveedor == Guid.Empty || cliente == Guid.Empty || (proveedor is not null && cliente is not null))
            throw new BusinessRuleException("CE_PARTICIPANTE_INVALIDO", "Capture nombre, tipo, papel y como máximo una referencia de cliente o proveedor.");
        Nombre = nombre.Trim();
        Tipo = tipo.Trim();
        Contacto = contacto?.Trim();
        Papel = papel;
        ProveedorId = proveedor;
        ClienteId = cliente;
    }
}
