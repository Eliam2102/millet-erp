import { z } from 'zod';

/**
 * Schemas Zod de los forms del módulo Identidad → Usuarios. Mirror de
 * <c>CrearUsuarioValidator</c> y <c>ActualizarUsuarioValidator</c> del
 * backend (F-Admin-PR4.2).
 *
 * <para><b>Email</b>: regex simple <c>local@dominio.tld</c> (mismo
 * patrón que el backend); la fuente de verdad de unicidad es la BD.</para>
 *
 * <para><b>Nombre</b>: 1..254. <b>DepartamentoId</b>: opcional (UUID
 * string o <c>null</c>).</para>
 */

const EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

/**
 * Regex laxo de UUID — formato <c>8-4-4-4-12</c> hex sin requerir
 * version/variant válidos de RFC 4122. Razón: el backend usa GUIDs
 * deterministas en seeds (ej. <c>00000003-0000-0000-0000-000000000001</c>
 * para empresa inicial, <c>00000002-0003-...</c> para roles del bootstrap)
 * que NO califican como UUID v1-v8 pero son válidos como identificadores
 * opacos. Zod v4 <c>.uuid()</c> los rechazaría — usar <c>.regex()</c>
 * laxo en su lugar. Mismo patrón que <c>UUID_SHAPE_RE</c> en
 * <c>features/compras/schemas/linea.ts</c> y otros.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const CrearUsuarioSchema = z.object({
  email: z
    .string()
    .trim()
    .toLowerCase()
    .min(1, 'Email requerido')
    .max(254, 'Máximo 254 caracteres')
    .regex(EMAIL_RE, 'Formato de email inválido (local@dominio.tld)'),
  nombreCompleto: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  departamentoId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Departamento inválido')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
  // Workaround manual hasta que <c>EntraIdResolver</c> esté wireado:
  // si el admin pega el ObjectId desde Azure Portal, se manda al
  // backend tal cual y el usuario podrá iniciar sesión con su cuenta
  // real de Entra ID. Si se deja vacío, el backend genera placeholder
  // <c>dev-{email}</c> (que NO permite login real).
  entraIdObjectId: z
    .string()
    .trim()
    .transform((v) => (v.length === 0 ? null : v))
    .nullable()
    .refine(
      (v) => v == null || /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v),
      'Object ID inválido (formato GUID esperado)',
    ),
});

export type CrearUsuarioValues = z.infer<typeof CrearUsuarioSchema>;

/**
 * Schema para el PATCH /usuarios/{id}. Email, nombre y departamentoId
 * editables; el <c>entraOid</c> NO se edita (queda fijado al crear).
 */
export const ActualizarUsuarioSchema = z.object({
  email: z
    .string()
    .trim()
    .toLowerCase()
    .min(1, 'Email requerido')
    .max(254, 'Máximo 254 caracteres')
    .regex(EMAIL_RE, 'Formato de email inválido (local@dominio.tld)'),
  nombreCompleto: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  departamentoId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Departamento inválido')
    .nullable()
    .transform((v) => (v != null && v.length === 0 ? null : v)),
});

export type ActualizarUsuarioValues = z.infer<typeof ActualizarUsuarioSchema>;

/**
 * Schema para asignar un rol a un usuario en una empresa (POST
 * <c>/usuarios/{id}/asignaciones</c>).
 */
export const AsignarRolSchema = z.object({
  empresaId: z.string().regex(UUID_SHAPE_RE, 'Empresa requerida'),
  rolId: z.string().regex(UUID_SHAPE_RE, 'Rol requerido'),
});

export type AsignarRolValues = z.infer<typeof AsignarRolSchema>;
