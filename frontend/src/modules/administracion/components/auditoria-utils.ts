export interface AuditoriaLookups {
  sucursales?: Record<string, string>;
  usuarios?: Record<string, string>;
  usuariosEmail?: Record<string, string>;
  empresas?: Record<string, string>;
  departamentos?: Record<string, string>;
  puestos?: Record<string, string>;
  empleados?: Record<string, string>;
  roles?: Record<string, string>;
  almacenes?: Record<string, string>;
}

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const ISO_DATETIME_REGEX = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/;
const ISO_DATE_REGEX = /^\d{4}-\d{2}-\d{2}$/;

const DICCIONARIO_CAMPOS: Record<string, string> = {
  departamentoid: 'Departamento',
  puestoid: 'Puesto',
  jefedirectoid: 'Jefe Directo',
  sucursalid: 'Sucursal',
  empresaid: 'Empresa',
  usuarioid: 'Usuario de Acceso',
  rolid: 'Rol',
  rolsugeridoid: 'Rol Sugerido',
  rolsugeridoefectivoid: 'Rol Sugerido',
  asignadoporusuarioid: 'Asignado Por',
  almacenid: 'Almacén',
  subalmacenid: 'Subalmacén',
  ubicacionid: 'Ubicación',
  codigonomina: 'Código de Nómina',
  emailcontacto: 'Correo de Contacto Personal',
  email: 'Correo Electrónico',
  tipoempleado: 'Tipo de Empleado',
  tipocontrato: 'Tipo de Contrato',
  fechaingreso: 'Fecha de Ingreso',
  fechabaja: 'Fecha de Baja',
  motivobaja: 'Motivo de Baja',
  razonsocial: 'Razón Social',
  nombrecomercial: 'Nombre Comercial',
  regimenfiscal: 'Régimen Fiscal',
  codigopostal: 'Código Postal',
  bloqueadohasta: 'Bloqueado Hasta',
  essuperadmin: 'Es Super Administrador',
  createdat: 'Fecha de Creación',
  updatedat: 'Fecha de Modificación',
  createdby: 'Creado Por',
  updatedby: 'Modificado Por',
  deletedat: 'Fecha de Eliminación',
  estatus: 'Estatus',
  estado: 'Estado de Acceso',
  activo: 'Activo',
  version: 'Versión',
  clave: 'Clave',
  nombre: 'Nombre',
  descripcion: 'Descripción',
  rfc: 'RFC',
  tipo: 'Tipo',
  canal: 'Canal',
  motivo: 'Motivo',
  resultado: 'Resultado',
  folio: 'Folio',
  monto: 'Monto',
  moneda: 'Moneda',
};

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

/** Transforma nombres técnicos como "departamentoId" a "Departamento", "jefeDirectoId" a "Jefe Directo", etc. */
export function humanizarCampo(campo: string): string {
  if (!campo) return '';
  const key = campo.toLowerCase().replace(/[^a-z0-9]/g, '');
  if (DICCIONARIO_CAMPOS[key]) {
    return DICCIONARIO_CAMPOS[key];
  }

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
  const c = campo.toLowerCase().replace(/[^a-z0-9]/g, '');

  // 1. Match prioritario por el contexto del campo
  if (
    (c.includes('jefe') ||
      c.includes('supervisor') ||
      c.includes('reporta') ||
      c.includes('empleado')) &&
    (lookups.empleados?.[id] || lookups.usuarios?.[id])
  ) {
    return lookups.empleados?.[id] ?? lookups.usuarios?.[id] ?? null;
  }

  if ((c.includes('departamento') || c.includes('depto')) && lookups.departamentos?.[id]) {
    return lookups.departamentos[id];
  }

  if (c.includes('puesto') && lookups.puestos?.[id]) {
    return lookups.puestos[id];
  }

  if (c.includes('sucursal') && lookups.sucursales?.[id]) {
    return lookups.sucursales[id];
  }

  if (
    (c.includes('usuario') ||
      c.includes('user') ||
      c.includes('actor') ||
      c.includes('creado') ||
      c.includes('modificado') ||
      c.includes('asignado') ||
      c.includes('responsable')) &&
    (lookups.usuarios?.[id] || lookups.empleados?.[id])
  ) {
    return lookups.usuarios?.[id] ?? lookups.empleados?.[id] ?? null;
  }

  if (c.includes('rol') && lookups.roles?.[id]) {
    return lookups.roles[id];
  }

  if (c.includes('empresa') && lookups.empresas?.[id]) {
    return lookups.empresas[id];
  }

  if (c.includes('almacen') && lookups.almacenes?.[id]) {
    return lookups.almacenes[id];
  }

  // 2. Búsqueda en los diccionarios disponibles en orden de prioridad
  if (lookups.departamentos?.[id]) return lookups.departamentos[id];
  if (lookups.puestos?.[id]) return lookups.puestos[id];
  if (lookups.empleados?.[id]) return lookups.empleados[id];
  if (lookups.roles?.[id]) return lookups.roles[id];
  if (lookups.sucursales?.[id]) return lookups.sucursales[id];
  if (lookups.usuarios?.[id]) return lookups.usuarios[id];
  if (lookups.empresas?.[id]) return lookups.empresas[id];
  if (lookups.almacenes?.[id]) return lookups.almacenes[id];

  return null;
}

/** Formatea un valor a texto legible en español — sin GUIDs crudos, fechas formateadas, sin llaves ni comillas de JSON. */
export function formatearValorCampo(
  v: unknown,
  campo?: string,
  lookups?: AuditoriaLookups,
): string {
  const c = (campo ?? '').toLowerCase().replace(/[^a-z0-9]/g, '');

  if (v == null) {
    if (c.includes('jefe')) return 'Sin jefe directo';
    if (c.includes('emailcontacto')) return 'Sin correo de contacto';
    if (c.includes('codigonomina')) return 'Sin código de nómina';
    if (c.includes('rolsugerido')) return 'Ninguno';
    if (c.includes('departamento')) return 'Sin departamento';
    if (c.includes('puesto')) return 'Sin puesto';
    if (c.includes('usuario')) return 'Sin usuario vinculado';
    return 'No aplica';
  }

  if (typeof v === 'boolean') return v ? 'Sí' : 'No';

  if (typeof v === 'number') {
    if (c === 'estatus') {
      if (v === 0) return 'Activo';
      if (v === 1) return 'Inactivo';
      if (v === 2) return 'En Revisión';
    }
    if (c === 'estado') {
      if (v === 0) return 'Activo';
      if (v === 1) return 'Pendiente primer acceso';
      if (v === 2) return 'Provisionando cuenta';
      if (v === 3) return 'Error de provisión';
    }
    if (c === 'tipo' || c === 'tiposucursal') {
      if (v === 1) return 'Taller';
      if (v === 2) return 'Planta';
    }
    if (c === 'tipoempleado') {
      if (v === 1) return 'Sindicalizado';
      if (v === 2) return 'De confianza';
    }
    if (c === 'tipocontrato') {
      if (v === 1) return 'Tiempo indeterminado';
      if (v === 2) return 'Tiempo determinado';
    }
    return String(v);
  }

  if (typeof v === 'string') {
    const trimmed = v.trim();
    if (trimmed.length === 0) {
      if (c.includes('jefe')) return 'Sin jefe directo';
      if (c.includes('emailcontacto')) return 'Sin correo de contacto';
      if (c.includes('codigonomina')) return 'Sin código de nómina';
      return 'No aplica';
    }

    if (trimmed === '00000000-0000-0000-0000-000000000000') {
      return 'Sin asignar';
    }

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
      if (trimmed.startsWith('00000000')) return 'Sin asignar';
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

/** Reemplaza GUIDs y nombres técnicos en textos de resumen para que sean comprensibles para el usuario humano. */
export function humanizarTextoConLookups(
  texto: string | null | undefined,
  lookups?: AuditoriaLookups,
): string {
  if (!texto) return '';

  let resultado = texto;

  // Reemplazar nombres de campos técnicos por etiquetas limpias
  resultado = resultado
    .replace(/\bDepartamentoId:\s*/gi, 'Departamento: ')
    .replace(/\bPuestoId:\s*/gi, 'Puesto: ')
    .replace(/\bJefeDirectoId:\s*/gi, 'Jefe directo: ')
    .replace(/\bSucursalId:\s*/gi, 'Sucursal: ')
    .replace(/\bEmpresaId:\s*/gi, 'Empresa: ')
    .replace(/\bUsuarioId:\s*/gi, 'Usuario: ')
    .replace(/\bRolId:\s*/gi, 'Rol: ')
    .replace(/\bRolSugeridoId:\s*/gi, 'Rol sugerido: ')
    .replace(/\bCodigoNomina:\s*/gi, 'Nómina: ')
    .replace(/\bEmailContacto:\s*/gi, 'Correo personal: ');

  // Simplificar entidades compuestas como 'UsuarioEmpresaRol UsuarioEmpresaRol ...'
  resultado = resultado
    .replace(/Creó UsuarioEmpresaRol UsuarioEmpresaRol\s+[0-9a-fA-F-]+/gi, 'Asignó rol a usuario')
    .replace(/Creó UsuarioSucursal UsuarioSucursal\s+[0-9a-fA-F-]+/gi, 'Asignó usuario a sucursal');

  if (!lookups) return resultado;

  // Reemplazar cualquier GUID encontrado por su nombre resuelto
  const guidMatches = resultado.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi);
  if (guidMatches) {
    for (const guid of guidMatches) {
      const nombre = resolverIdReferencia(guid, '', lookups);
      if (nombre) {
        resultado = resultado.split(guid).join(nombre);
      }
    }
  }

  return resultado;
}
