import { expect, test } from '@playwright/test';
import { randomInt } from 'node:crypto';

const contrasenaAdministrador = 'E2E-Admin-Acceso-481!';
const identificadorCliente = 'cliente-e2e@optica.invalid';
const contrasenaCliente = 'E2E-Cliente-Acceso-271!';
const urlApi = 'http://localhost:8080';
const urlFrontend = 'http://localhost:4200';
const urlMailpit = 'http://localhost:8025';

interface AvisoReposicionE2E {
  idAviso: number;
  estado: string;
}

interface MensajeMailpit {
  ID: string;
  To: Array<{ Address: string }>;
  Subject: string;
  Text: string;
  HTML: string;
}

interface BusquedaMailpit {
  messages: Array<{ ID: string; Subject: string }>;
}

test('el cliente solicita reposición, el administrador recibe la notificación y el cliente recibe el correo', async ({
  browser,
  page: paginaAdministrador,
  request,
}) => {
  test.setTimeout(240_000);
  const idEjecucion = `${Date.now()}-${randomInt(1_000_000)}`;
  const codigo = `E2E-REP-${idEjecucion}`;
  const nombre = `Producto reposición ${idEjecucion}`;
  let idProducto = 0;
  let avisoReposicionId = 0;

  await test.step('prepara un producto agotado y deja al administrador conectado', async () => {
    await paginaAdministrador.goto('/admin');
    await paginaAdministrador.getByLabel('RUT, correo electrónico o nombre de usuario').fill('e2e-admin');
    await paginaAdministrador.getByRole('textbox', { name: 'Contraseña' }).fill(contrasenaAdministrador);

    const login = paginaAdministrador.waitForResponse(response =>
      response.url() === `${urlApi}/api/Autenticacion/login` && response.request().method() === 'POST',
    );
    await paginaAdministrador.getByRole('button', { name: 'Ingresar' }).click();
    expect((await login).status(), 'El administrador sintético debe iniciar sesión').toBe(200);
    await expect(paginaAdministrador).toHaveURL(/\/admin\/panel/);

    const respuestaProducto = await paginaAdministrador.context().request.post(`${urlApi}/api/Productos/json`, {
      data: {
        codigo,
        nombre,
        categoria: 'Armazones',
        precio: 12000,
        stock: 0,
        stockMinimo: 1,
        estado: 'Agotado',
      },
      headers: { Origin: urlFrontend },
    });
    expect(respuestaProducto.status(), 'El producto agotado debe quedar registrado').toBe(200);
    idProducto = ((await respuestaProducto.json()) as { id: number }).id;
    expect(idProducto).toBeGreaterThan(0);
  });

  const contextoCliente = await browser.newContext();
  const paginaCliente = await contextoCliente.newPage();

  try {
    await test.step('el cliente inicia sesión, encuentra el producto agotado y solicita el aviso', async () => {
      await paginaCliente.goto('/cliente/login');
      await paginaCliente.getByLabel('RUT, correo electrónico o nombre de usuario').fill(identificadorCliente);
      await paginaCliente.getByRole('textbox', { name: 'Contraseña' }).fill(contrasenaCliente);

      const login = paginaCliente.waitForResponse(response =>
        response.url() === `${urlApi}/api/Autenticacion/login` && response.request().method() === 'POST',
      );
      await paginaCliente.getByRole('button', { name: 'Ingresar' }).click();
      expect((await login).status(), 'El cliente sintético debe iniciar sesión').toBe(200);
      await expect(paginaCliente).toHaveURL(`${urlFrontend}/cliente/inicio`);

      await paginaCliente.goto('/productos');
      await paginaCliente.getByLabel('Buscar', { exact: true }).fill(codigo);
      await paginaCliente.getByRole('button', { name: 'Buscar', exact: true }).click();
      const producto = paginaCliente.locator('.product-card').filter({ hasText: codigo });
      await expect(producto).toBeVisible();
      await producto.click();
      await expect(paginaCliente.getByRole('dialog')).toBeVisible();
      await expect(paginaCliente.getByRole('dialog')).toContainText('Agotado');

      const solicitud = paginaCliente.waitForResponse(response =>
        response.url() === `${urlApi}/api/AvisosReposicion/producto/${idProducto}`
        && response.request().method() === 'POST',
      );
      await paginaCliente.getByRole('button', { name: 'Avisarme cuando vuelva a estar disponible' }).click();
      const respuestaSolicitud = await solicitud;
      expect(respuestaSolicitud.status(), 'La solicitud del cliente debe registrarse').toBe(201);
      const aviso: AvisoReposicionE2E = await respuestaSolicitud.json();
      expect(aviso.idAviso).toBeGreaterThan(0);
      expect(aviso.estado).toBe('Abierta');
      avisoReposicionId = aviso.idAviso;
      await expect(paginaCliente.getByRole('status')).toContainText('Solicitud registrada');
    });

    await test.step('el administrador ve la notificación persistente en el dashboard', async () => {
      const titulo = `Solicitud de reposición: ${nombre}`;
      const notificacion = paginaAdministrador
        .locator('#panel-notificaciones')
        .getByRole('listitem')
        .filter({ hasText: titulo });
      await paginaAdministrador.getByRole('button', { name: /Notificaciones:/ }).click();
      await expect(notificacion).toBeVisible({ timeout: 45_000 });
      await expect(notificacion).toContainText('Cliente E2E solicitó un aviso');
      await expect(
        paginaAdministrador.getByRole('button', { name: `Eliminar notificación: ${titulo}` }),
      ).toHaveCount(0);

      await paginaAdministrador.reload();
      await expect(paginaAdministrador).toHaveURL(/\/admin\/panel/);
      await paginaAdministrador.getByRole('button', { name: /Notificaciones:/ }).click();
      await expect(notificacion).toBeVisible({ timeout: 30_000 });
    });

    await test.step('el administrador repone el stock del producto', async () => {
      await paginaAdministrador.goto('/admin/panel/stock');
      await expect(paginaAdministrador.getByRole('heading', { name: 'Consultar stock' })).toBeVisible();
      const filaProducto = paginaAdministrador.getByRole('row').filter({ hasText: codigo });
      await expect(filaProducto).toBeVisible();
      await filaProducto.getByRole('button', { name: 'Editar' }).click();
      await filaProducto.getByLabel('Existencias').fill('5');
      await filaProducto.getByLabel('Motivo del cambio').fill(`Reposición E2E ${idEjecucion}`);

      const actualizacion = paginaAdministrador.waitForResponse(response =>
        response.url().includes('/api/Productos/')
        && response.url().endsWith('/stock')
        && response.request().method() === 'PATCH',
      );
      await filaProducto.getByRole('button', { name: 'Guardar' }).click();
      expect((await actualizacion).status(), 'La reposición debe guardarse').toBe(200);
      await expect(filaProducto).toContainText('5 unidades');
    });

    await test.step('el cliente recibe el correo y la solicitud queda cerrada', async () => {
      const asuntoEsperado = `Producto disponible nuevamente: ${nombre}`;
      let mensaje: MensajeMailpit | undefined;

      await expect.poll(async () => {
        const busquedaRespuesta = await request.get(
          `${urlMailpit}/api/v1/search?query=to:${encodeURIComponent(identificadorCliente)}`,
        );
        if (!busquedaRespuesta.ok()) return 'Mailpit API no disponible';
        const busqueda: BusquedaMailpit = await busquedaRespuesta.json();
        const id = busqueda.messages.find(item => item.Subject.includes(asuntoEsperado))?.ID;
        if (!id) return 'Correo todavía no recibido';
        const detalle = await request.get(`${urlMailpit}/api/v1/message/${id}`);
        if (!detalle.ok()) return 'Mensaje aún no disponible';
        mensaje = await detalle.json();
        return mensaje.To.some(destinatario =>
          destinatario.Address.toLowerCase() === identificadorCliente.toLowerCase(),
        )
          ? 'correo recibido'
          : 'destinatario incorrecto';
      }, {
        message: 'El procesador de avisos de reposición envía el correo aproximadamente cada minuto',
        timeout: 150_000,
        intervals: [2_000, 5_000],
      }).toBe('correo recibido');

      expect(mensaje?.Subject).toContain(asuntoEsperado);
      expect(`${mensaje?.Text ?? ''}\n${mensaje?.HTML ?? ''}`).toContain(nombre);

      await expect.poll(async () => {
        const respuesta = await paginaAdministrador.context().request.get(`${urlApi}/api/AvisosReposicion`);
        if (!respuesta.ok()) return 'No se pudieron consultar las solicitudes';
        const avisos: AvisoReposicionE2E[] = await respuesta.json();
        return avisos.find(aviso => aviso.idAviso === avisoReposicionId)?.estado ?? 'No se encontró la solicitud';
      }, {
        message: 'La solicitud debe cerrarse después del envío exitoso del correo',
        timeout: 30_000,
        intervals: [1_000, 2_000],
      }).toBe('Cerrada');
    });
  } finally {
    await contextoCliente.close();
  }
});
