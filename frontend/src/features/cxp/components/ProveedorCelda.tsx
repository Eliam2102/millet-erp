import { Badge } from '@/components/ui/badge';

interface ProveedorCeldaProps {
  nombre: string | null | undefined;
  rfc?: string | null;
  /** 86e3tactf: bloqueo total del proveedor; la factura se captura y valida, pero no se paga. */
  bloqueado?: boolean;
}

/** Entidad de una fila de bandeja: razón social (con `title` por si se trunca) y RFC mono debajo. Nunca muestra el GUID. */
export function ProveedorCelda({ nombre, rfc, bloqueado }: ProveedorCeldaProps) {
  const etiqueta = nombre?.trim() || 'Sin proveedor';
  return (
    <div className="max-w-56">
      <div className={nombre?.trim() ? "truncate" : "truncate text-ink-muted"} title={etiqueta}>
        {etiqueta}
      </div>
      {rfc ? <div className="font-mono text-2xs text-ink-muted">{rfc}</div> : null}
      {bloqueado ? <Badge variant="danger">Proveedor bloqueado</Badge> : null}
    </div>
  );
}
