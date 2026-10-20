interface ProveedorCeldaProps {
  nombre: string | null | undefined;
  rfc?: string | null;
}

/** Entidad de una fila de bandeja: razón social (con `title` por si se trunca) y RFC mono debajo. Nunca muestra el GUID. */
export function ProveedorCelda({ nombre, rfc }: ProveedorCeldaProps) {
  const etiqueta = nombre?.trim() || 'Sin proveedor';
  return (
    <div className="max-w-56">
      <div className={nombre?.trim() ? "truncate" : "truncate text-ink-muted"} title={etiqueta}>
        {etiqueta}
      </div>
      {rfc ? <div className="font-mono text-2xs text-ink-muted">{rfc}</div> : null}
    </div>
  );
}
