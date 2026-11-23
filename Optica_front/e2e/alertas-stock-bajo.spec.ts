import { expect, test } from '@playwright/test';

const contrasenaAdministrador = 'E2E-Admin-Acceso-481!';
const urlApi = 'http://localhost:8080';
const urlFrontend = 'http://localhost:4200';

interface ProductoCreadoE2E {
  id: number;
}

interface VentaCreadaE2E {
  idVenta: number;
  productos: Array<{ productoId: number; cantidad: number }>;
}

test('la alerta de stock bajo aparece después de vender y desaparece al reponer', async ({
  page,
}) => {
  test.setTimeout(120_000);
  const idEjecucion = Date.now();
  const codigo = `E2E-STOCK-${idEjecucion}`;
  const nombre = `Producto alerta ${idEjecucion}`;

  await test.step('el administrador inicia sesión', async () => {
    await page.goto('/admin');
    await page.getByLabel('RUT, correo electrónico o nombre de usuario').fill('e2e-admin');
    await page.getByRole('textbox', { name: 'Contraseña' }).fill(contrasenaAdministrador);

    const login = page.waitForResponse(
      (response) =>
        response.url() === `${urlApi}/api/Autenticacion/login` &&
        response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Ingresar' }).click();
    expect((await login).status(), 'El administrador sintético debe iniciar sesión').toBe(200);
    await expect(page).toHaveURL(/\/admin\/panel/);
  });

  let idProducto = 0;
  await test.step('prepara un producto por sobre su mínimo para venderlo', async () => {
    const respuesta = await page.context().request.post(`${urlApi}/api/Productos/json`, {
      data: {
        codigo,
        nombre,
        categoria: 'Armazones',
        precio: 12000,
        stock: 2,
        stockMinimo: 1,
        estado: 'Disponible',
      },
      headers: { Origin: urlFrontend },
    });

    expect(respuesta.status(), 'El producto de prueba debe quedar registrado').toBe(200);
    idProducto = ((await respuesta.json()) as ProductoCreadoE2E).id;
    expect(idProducto).toBeGreaterThan(0);
  });

  await test.step('registra una venta y observa la alerta de stock bajo', async () => {
    await page.goto('/admin/panel/ventas');
    await expect(page.getByRole('heading', { name: 'Registrar venta' })).toBeVisible();
    await page.getByLabel('Nombre o código del producto').fill(codigo);
    await page.getByRole('button', { name: `Agregar ${nombre}, código ${codigo}` }).click();

    const ventaRegistrada = page.waitForResponse(
      (response) =>
        response.url() === `${urlApi}/api/Ventas` && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Registrar compra' }).click();
    const respuestaVenta = await ventaRegistrada;
    expect(respuestaVenta.status(), 'La venta debe registrarse correctamente').toBe(201);
    const venta = (await respuestaVenta.json()) as VentaCreadaE2E;
    expect(venta.productos).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ productoId: idProducto, cantidad: 1 }),
      ]),
    );
    await expect(page.getByText(`Venta #${venta.idVenta} registrada correctamente.`)).toBeVisible();

    await page.getByRole('button', { name: /Notificaciones:/ }).click();
    const alerta = page
      .locator('#panel-notificaciones')
      .getByText(`Bajo stock: ${nombre}`, { exact: true });
    await expect(
      alerta,
      'La API debe publicar el stock actualizado después de la venta',
    ).toBeVisible({
      timeout: 45_000,
    });
    await expect(page.locator('#panel-notificaciones')).toContainText('Quedan 1; mínimo 1.');
  });

  await test.step('repone el producto y espera que la alerta se retire', async () => {
    await page.goto('/admin/panel/stock');
    await expect(page.getByRole('heading', { name: 'Consultar stock' })).toBeVisible();
    const filaProducto = page.getByRole('row').filter({ hasText: codigo });
    await expect(filaProducto).toBeVisible();
    await filaProducto.getByRole('button', { name: 'Editar' }).click();
    await filaProducto.getByLabel('Existencias').fill('5');
    await filaProducto.getByLabel('Motivo del cambio').fill(`Reposición E2E ${idEjecucion}`);

    const stockActualizado = page.waitForResponse(
      (response) =>
        response.url() === `${urlApi}/api/Productos/${idProducto}/stock` &&
        response.request().method() === 'PATCH',
    );
    await filaProducto.getByRole('button', { name: 'Guardar' }).click();
    expect((await stockActualizado).status(), 'La reposición debe guardarse').toBe(200);
    await expect(filaProducto).toContainText('5 unidades');

    await expect(
      page.locator('#panel-notificaciones').getByText(`Bajo stock: ${nombre}`, { exact: true }),
      'La alerta debe desaparecer al superar el mínimo configurado',
    ).toHaveCount(0, { timeout: 45_000 });
  });
});
