import { z } from 'zod';
import { CancelarOcSchema } from '@/features/compras/ordenes/schemas/cancelar';

/**
 * Schema Zod del body POST <c>/{id}/cancelar-con-recepciones</c>
 * (UF5-PR1, F5-PR4). Cancela una OC con recepciones parciales o
 * completas. Las cantidades ya recibidas permanecen en las líneas
 * (trazabilidad contable); las cantidades no recibidas se liberan al
 * pool de la RQ origen vía <c>LineaRqLiberadaEvent</c>.
 *
 * <para><b>Doble firma — política actual</b>: el backend valida que el
 * usuario actual tenga los <b>3 permisos</b> simultáneamente:
 * <c>cancelar-doble</c> + <c>autorizar-nivel1</c> +
 * <c>autorizar-nivel2</c>. NO recibe IDs de usuario separados — un solo
 * usuario certifica las dos firmas.</para>
 *
 * <para><b>UX del frontend</b>: el <c>&lt;DobleFirmaDialog/&gt;</c>
 * presenta un checklist visual donde el usuario reconoce explícitamente
 * que está firmando como N1 + N2 (los dos checkboxes son obligatorios
 * antes de habilitar el submit). Esos flags <b>NO</b> se mandan al
 * backend — son control de UX para evitar que el usuario haga la
 * cancelación por accidente sin entender que firma dos veces.</para>
 *
 * <para><b>Promoción futura</b>: si el backend evoluciona para aceptar
 * 2 IDs separados (un usuario con <c>cancelar-doble</c> + dos firmas
 * de usuarios distintos con sus permisos respectivos), agregar
 * <c>UsuarioAutorizadorN1Id</c> / <c>UsuarioAutorizadorN2Id</c> al body
 * y promover el dialog a 2 selectores con validación
 * <c>n1Id !== n2Id</c>.</para>
 */
export const CancelarOcConRecepcionesSchema = CancelarOcSchema;

export type CancelarOcConRecepcionesValues = z.infer<
  typeof CancelarOcConRecepcionesSchema
>;
