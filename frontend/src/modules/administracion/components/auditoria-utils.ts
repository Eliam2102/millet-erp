export interface AuditoriaLookups {
  sucursales?: Record<string, string>;
  usuarios?: Record<string, string>;
  empresas?: Record<string, string>;
  departamentos?: Record<string, string>;
  puestos?: Record<string, string>;
}

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ISO_DATETIME_REGEX = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/;
const ISO_DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

export function isUuid(v: string | null | undefined): boolean {
  if (!v) return false;
  return UUID_REGEX.test(v.trim());
}

export function formatFecha(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  return d.toLocaleDateString('es-MX', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

export function formatHora(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleTimeString('es-MX', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

export function formatTimestampDetalle(ts: string): string {
  const d = new Date(ts);
  if (Number.isNaN(d.getTime())) return ts;
  const fecha = d.toLocaleDateString('es-MX', {
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  });
  const hora = d.toLocaleTimeString('es-MX', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
  return `${fecha} · ${hora} hrs`;
}

/** "sucursalId" → "Sucursal"; "razon_social" → "Razon Social". */
export function humanizarCampo(campo: string): string {
  const campoLimpio = campo.length > 2 ? campo.replace(/(_id|Id|ID)$/i, '') : campo;
  const espaciado = (campoLimpio || campo)
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .trim();
  const capitalizado = espaciado
    .split(' ')
    .filter((w) => w.length > 0)
    .map((w) => w[0].toUpperCase() + w.slice(1))
    .join(' ');
  return capitalizado;
}

export function resolverIdReferencia(
  id: string,
  campo: string = '',
  lookups?: AuditoriaLookups,
): string | null {
  if (!id || !lookups) return null;
  const c = campo.toLowerCase();

  // Match prioritario por el contexto del campo
  if (c.includes('sucursal') && lookups.sucursales?.[id]) {
    return lookups.sucursales[id];
  }
  if (
    (c.includes('usuario') ||
      c.includes('user') ||
      c.includes('actor') ||
      c.includes('creado') ||
      c.includes('modificado') ||
      c.includes('responsable')) &&
    lookups.usuarios?.[id]
  ) {
    return lookups.usuarios[id];
  }
  if (c.includes('empresa') && lookups.empresas?.[id]) {
    return lookups.empresas[id];
  }
  if (c.includes('departamento') && lookups.departamentos?.[id]) {
    return lookups.departamentos[id];
  }
  if (c.includes('puesto') && lookups.puestos?.[id]) {
    return lookups.puestos[id];
  }

  // Búsqueda en los diccionarios disponibles
  if (lookups.sucursales?.[id]) return lookups.sucursales[id];
  if (lookups.usuarios?.[id]) return lookups.usuarios[id];
  if (lookups.empresas?.[id]) return lookups.empresas[id];
  if (lookups.departamentos?.[id]) return lookups.departamentos[id];
  if (lookups.puestos?.[id]) return lookups.puestos[id];

  return null;
}

/** Formatea un valor a texto legible en español — sin GUIDs crudos, fechas formateadas, sin llaves ni comillas de JSON. */
export function formatearValorCampo(
  v: unknown,
  campo?: string,
  lookups?: AuditoriaLookups,
): string {
  if (v == null) return 'No aplica';
  if (typeof v === 'boolean') return v ? 'Sí' : 'No';
  if (typeof v === 'number') return String(v);

  if (typeof v === 'string') {
    const trimmed = v.trim();
    if (trimmed.length === 0) return 'No aplica';

    // Fecha y hora ISO (e.g. 2026-09-28T10:14:03Z)
    if (ISO_DATETIME_REGEX.test(trimmed)) {
      const d = new Date(trimmed);
      if (!Number.isNaN(d.getTime())) {
        const fecha = d.toLocaleDateString('es-MX', {
          day: '2-digit',
          month: 'short',
          year: 'numeric',
        });
        const hora = d.toLocaleTimeString('es-MX', {
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
        });
        return `${fecha} · ${hora} hrs`;
      }
    }

    // Fecha ISO sin hora (e.g. 2026-09-28)
    if (ISO_DATE_REGEX.test(trimmed)) {
      const d = new Date(`${trimmed}T00:00:00`);
      if (!Number.isNaN(d.getTime())) {
        return d.toLocaleDateString('es-MX', {
          day: '2-digit',
          month: 'short',
          year: 'numeric',
        });
      }
    }

    // UUID / GUID
    if (isUuid(trimmed)) {
      const nombreResuelto = resolverIdReferencia(trimmed, campo, lookups);
      if (nombreResuelto) return nombreResuelto;
      return `ID: ${trimmed.slice(0, 8)}…`;
    }

    return trimmed;
  }

  if (Array.isArray(v)) {
    return v.length === 0
      ? 'No aplica'
      : v.map((x) => formatearValorCampo(x, campo, lookups)).join(', ');
  }

  if (typeof v === 'object') {
    const entries = Object.entries(v as Record<string, unknown>);
    return entries.length === 0
      ? 'No aplica'
      : entries
          .map(([k, val]) => `${humanizarCampo(k)}: ${formatearValorCampo(val, k, lookups)}`)
          .join(' · ');
  }

  return String(v);
}
