import { readFile } from 'node:fs/promises';
import { expect, test } from '@playwright/test';

const contrasenaAdministrador = 'E2E-Admin-Acceso-481!';
const urlApi = 'http://localhost:8080';

test('el administrador visualiza y descarga el reporte de stock', async ({ page }, testInfo) => {
  await test.step('el administrador inicia sesión correctamente', async () => {
    await page.goto('/admin');
    await page.getByLabel('RUT, correo electrónico o nombre de usuario').fill('e2e-admin');
    await page.getByRole('textbox', { name: 'Contraseña' }).fill(contrasenaAdministrador);

    const login = page.waitForResponse(response =>
      response.url() === `${urlApi}/api/Autenticacion/login` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Ingresar' }).click();

    expect((await login).status(), 'El administrador sintético debe iniciar sesión').toBe(200);
    await expect(page).toHaveURL(/\/admin\/panel/);
  });

  await test.step('se dirige a consultar stock', async () => {
    await page.getByRole('button', { name: 'Inventario' }).hover();
    await page.getByRole('link', { name: 'Consultar Stock', exact: true }).click();

    await expect(page).toHaveURL(/\/admin\/panel\/stock$/);
    await expect(page.getByRole('heading', { name: 'Consultar stock' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Ver PDF' })).toBeEnabled();
    await expect(page.getByRole('button', { name: 'Descargar PDF' })).toBeEnabled();
  });

  await test.step('visualiza el reporte PDF', async () => {
    const ventanaReportePromise = page.waitForEvent('popup');
    const descargaVistaPreviaPromise = page.context().waitForEvent('download');
    await page.getByRole('button', { name: 'Ver PDF' }).click();
    const [ventanaReporte, descargaVistaPrevia] = await Promise.all([
      ventanaReportePromise,
      descargaVistaPreviaPromise
    ]);

    expect(ventanaReporte.isClosed()).toBe(false);
    const rutaVistaPrevia = testInfo.outputPath('vista-previa.pdf');
    await descargaVistaPrevia.saveAs(rutaVistaPrevia);
    const contenidoVistaPrevia = await readFile(rutaVistaPrevia);
    expect(contenidoVistaPrevia.subarray(0, 5).toString('ascii')).toBe('%PDF-');
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  await test.step('descarga y verifica el archivo PDF', async () => {
    const descargaPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Descargar PDF' }).click();
    const descarga = await descargaPromise;

    expect(descarga.suggestedFilename()).toMatch(/^reporte-inventario-\d{4}-\d{2}-\d{2}\.pdf$/);
    const rutaArchivo = testInfo.outputPath(descarga.suggestedFilename());
    await descarga.saveAs(rutaArchivo);
    const contenido = await readFile(rutaArchivo);
    expect(contenido.subarray(0, 5).toString('ascii')).toBe('%PDF-');
  });
});
