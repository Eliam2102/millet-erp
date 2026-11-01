/**
 * Validaciones de PERMISOS: un rol sin el permiso no ve el botón y la API responde 403.
 * Requiere una sesión de Entra vigente del rol «auditor» (e2e/.auth/test/auditor.json); si está vencida el caso se omite y se dice por qué.
 */
import { test, expect, newRoleContext, shot } from '../../_harness/fixtures';
import { apiBaseURL, EXPIRED_URL } from '../../_harness/session';
import { captureBearer, DEPTO_COMPRAS, MID, uuid, foto } from './_data';

test.use({ role: 'super-admin', viewport: { width: 1600, height: 900 } });
test.setTimeout(120_000);

test('V-PE-01 un rol sin permiso de crear requisiciones no ve el botón y la API responde 403', async ({ browser, guard }) => {
  guard.allowStatus(403);
  const { context, page } = await newRoleContext(browser, 'auditor');
  const bearer = captureBearer(page);
  await page.setViewportSize({ width: 1600, height: 900 });
  await page.goto('/compras/requisiciones');
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(2500);
  const vencida = EXPIRED_URL.test(page.url()) || (await page.getByRole('button', { name: 'Continuar con Microsoft' }).isVisible().catch(() => false)) || !bearer.v;
  test.skip(vencida, 'La sesión de Entra guardada del rol «auditor» (12-oct-2026) ya venció: no se puede iniciar sesión sin una persona. Renovar con `node scripts/login-entra.mjs auditor`.');
  await expect(page.getByRole('heading', { name: 'Bandeja de requisiciones' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nueva' }), 'sin permiso el botón no se muestra').toHaveCount(0);
  await foto(page, 'V-PE-01-rol-sin-permiso-no-ve-el-boton-nueva-requisicion');
  const res = await page.request.fetch(`${apiBaseURL}/api/v1/compras/requisiciones`, {
    method: 'POST', headers: { authorization: bearer.v, 'Idempotency-Key': uuid() },
    data: { sucursalId: MID, sucursalCodigo: 'MID', folioAnio: new Date().getFullYear(), departamentoId: DEPTO_COMPRAS, clasificacion: 1, prioridad: 1, fechaSolicitud: new Date().toISOString(), descripcion: 'Intento sin permiso', fechaEntregaDeseada: null, proveedorSugeridoId: null, requisitanteId: null },
  });
  expect(res.status()).toBe(403);
  await context.close();
});
