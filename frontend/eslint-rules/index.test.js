import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import { ESLint } from 'eslint';

const eslint = new ESLint();
const lint = async (code, filePath = 'src/features/facturacion/F0Probe.tsx') =>
  (await eslint.lintText(code, { filePath }))[0].messages;

test('detecta paleta en literales y templates; hex y dark en className; texto interno', async () => {
  const messages = await lint(
    'export const Probe = () => <div className={`bg-blue-500 dark:text-[#abc]`}>El backend valida (D03)</div>;',
  );
  for (const rule of ['no-paleta-default', 'no-hex', 'no-dark', 'no-texto-interno']) {
    assert(messages.some((m) => m.ruleId === `ds/${rule}` && m.severity === 2));
  }
  const literals = await lint(
    'export const Probe = () => <div className="border-red-500">{"archivo.md"}</div>;',
  );
  assert(literals.some((m) => m.ruleId === 'ds/no-paleta-default'));
  assert(literals.some((m) => m.ruleId === 'ds/no-texto-interno'));
});
test('acepta tokens y limita hex/dark al className', async () => {
  const messages = await lint(
    'export const Probe = () => <div className="bg-surface-card text-ink">{"dark: text-[#fff]"}</div>;',
  );
  assert(!messages.some((m) => m.ruleId?.startsWith('ds/')));
});
test('respeta todas las exclusiones', async () => {
  for (const filePath of [
    'src/features/demo/Probe.test.tsx',
    'src/components/erp/reportes/exporters/Probe.tsx',
    'src/features/almacen/components/impresion/Probe.tsx',
    'src/components/auth/DevUserSelector.tsx',
  ]) {
    const messages = await lint(
      'export const Probe = () => <div className="bg-red-500">backend</div>;',
      filePath,
    );
    assert(!messages.some((m) => m.ruleId?.startsWith('ds/')), filePath);
  }
});
test('conserva restricciones de fechas UTC, centros de costo e idempotencia como error', async () => {
  const messages = await lint(
    'export const Probe = () => <div>{new Date().toISOString().slice(0, 10)}{"Dimensión 1"}{JSON.stringify({ idempotencyKey: `key-${1}` })}</div>;',
  );
  assert.equal(
    messages.filter((m) => m.ruleId === 'no-restricted-syntax' && m.severity === 2).length,
    3,
  );
});

for (const filePath of ['src/lib/F3Probe.ts', 'src/features/demo/F3Probe.tsx']) {
  test(`texto interno en ${filePath}: marcadores, decisiones y templates`, async () => {
    for (const value of ['[PROVEEDOR]', 'ADR-0036', 'ADR0036', 'stub', 'TODO', 'Por confirmar']) {
      const messages = await lint(`export const texto = ${JSON.stringify(value)};`, filePath);
      assert(
        messages.some((m) => m.ruleId === 'ds/no-texto-interno' && m.severity === 2),
        value,
      );
    }
    for (const value of [
      'Sin proveedor',
      '[]',
      '[0]',
      '#[ref]',
      'todo',
      'Todo listo',
      'Método de pago',
    ]) {
      const messages = await lint(`export const texto = ${JSON.stringify(value)};`, filePath);
      assert(!messages.some((m) => m.ruleId === 'ds/no-texto-interno'), value);
    }
    const templates = await lint('export const texto = `ADR-0036 ${1}`;', filePath);
    assert(templates.some((m) => m.ruleId === 'ds/no-texto-interno'));
  });
}
test('JSX recortado y excepciones técnicas no ocultan el texto visible', async () => {
  const messages = await lint(
    'export const Probe = () => <div className="stub" data-kind="TODO" aria-describedby="stub" key="TODO"><SelectItem value="TODO"> [PROVEEDOR] </SelectItem></div>;',
  );
  assert.equal(messages.filter((m) => m.ruleId === 'ds/no-texto-interno').length, 1);
  const technical = await lint(
    'export const options = { queryKey: ["todo", "TODO"], payload: 1, endpoint: 2, DTO: 3 }; fetch("/api/stub"); apiRequest("stub"); new URL("https://example.test/ADR-0036"); // TODO comentario',
    'src/lib/F3Probe.ts',
  );
  assert(!technical.some((m) => m.ruleId === 'ds/no-texto-interno'));
});
test('excluye pruebas TS, test y mocks de texto interno', async () => {
  for (const filePath of [
    'src/lib/Probe.test.ts',
    'src/lib/Probe.spec.ts',
    'src/test/Probe.ts',
    'src/mocks/Probe.ts',
    'src/__mocks__/Probe.ts',
  ]) {
    const messages = await lint('export const texto = "[PROVEEDOR]";', filePath);
    assert(!messages.some((m) => m.ruleId === 'ds/no-texto-interno'), filePath);
  }
});

test('las etiquetas accesibles también son texto de usuario', async () => {
  const messages = await lint(
    'export const Probe = () => <button aria-label="[PROVEEDOR]">Abrir</button>;',
  );
  assert(messages.some((m) => m.ruleId === 'ds/no-texto-interno'));
});

test('F5: blanco, negro y todos los prefijos son errores globales', async () => {
  for (const prefix of ['bg', 'text', 'border', 'ring', 'outline', 'fill', 'stroke', 'divide', 'from', 'via', 'to', 'decoration', 'placeholder', 'shadow', 'accent', 'caret']) {
    for (const color of ['white', 'black/60', 'red-600/50']) {
      const messages = await lint(`export const Probe = () => <div className="hover:${prefix}-${color}" />;`, 'src/components/F5Probe.tsx');
      assert(messages.some((m) => m.ruleId === 'ds/no-paleta-default' && m.severity === 2), `${prefix}-${color}`);
    }
  }
  const template = await lint('export const classes = `shadow-black/20 accent-white caret-gray-500`;', 'src/components/F5Probe.tsx');
  assert(template.some((m) => m.ruleId === 'ds/no-paleta-default' && m.severity === 2));
});

test('F5: tokens, transparent y current siguen permitidos; hex y dark son errores globales', async () => {
  const allowed = await lint('export const Probe = () => <div className="bg-transparent text-current shadow-brand/20 accent-brand caret-ink bg-neutral-bg" />;', 'src/components/F5Probe.tsx');
  assert(!allowed.some((m) => m.ruleId?.startsWith('ds/')));
  const forbidden = await lint('export const Probe = () => <div className="dark:bg-brand text-[#abc]" />;', 'src/components/F5Probe.tsx');
  for (const rule of ['ds/no-hex', 'ds/no-dark']) assert(forbidden.some((m) => m.ruleId === rule && m.severity === 2));
});
