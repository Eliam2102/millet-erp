// LOCAL-ONLY config for the manuals walkthrough (86e3wjt33). Run: npx playwright test -c playwright.manuales.config.ts e2e/manuales/<file>
import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e/ez-local',
  outputDir: '/tmp/claude-1000/-home-devgeo-Agentes-2026-11-04-turno-nocturno/b03d097c-2d23-424c-8535-71819016901e/scratchpad/pw-ez',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 900_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: { headless: true, navigationTimeout: 30000, launchOptions: { executablePath: '/home/devgeo/.cache/ms-playwright/chromium-1247/chrome-linux64/chrome' }, actionTimeout: 20_000, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
});
