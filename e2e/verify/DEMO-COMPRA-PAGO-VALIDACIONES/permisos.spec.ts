/**
 * Validaciones de PERMISOS: un rol sin el permiso no ve el botón y la API responde 403.
 * Requiere la sesión de Entra guardada del rol «auditor» (e2e/.auth/test/auditor.json): la app entra con SSO («Continuar con Microsoft»).
 */
import { test, expect } from '../../_harness/fixtures';
import { asRole } from '../86e3pc50v/_data';
import { DEPTO_COMPRAS, MID, uuid, foto } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(120_000);

test('V-PE-01 un rol sin permiso de crear requisiciones no ve el botón y la API responde 403', async ({ browser, guard }) => {
  guard.allowStatus(403);
  const { context, page, api } = await asRole(browser, 'auditor', 'qa.auditor@uzieltzaboutlook.onmicrosoft.com', '/compras/requisiciones');
  await page.setViewportSize({ width: 1600, height: 900 });
  await expect(page.getByRole('heading', { name: /^(Bandeja de requisiciones|Requisiciones)$/ }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nueva' }), 'sin permiso el botón no se muestra').toHaveCount(0);
  await foto(page, 'V-PE-01-rol-sin-permiso-no-ve-el-boton-nueva-requisicion');
  const res = await api.post('/api/v1/compras/requisiciones', { sucursalId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), departamentoId: DEPTO_COMPRAS, clasificacion: 1, prioridad: 1, fechaSolicitud: new Date().toISOString(), descripcion: 'Intento sin permiso', fechaEntregaDeseada: null, proveedorSugeridoId: null, requisitanteId: null,
  }, { 'Idempotency-Key': uuid() });
  expect(res.status()).toBe(403);
  await context.close();
});
