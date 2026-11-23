import { AutenticacionAdminService } from '../../shared/services/autenticacion-admin.service';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { catchError, EMPTY, exhaustMap, interval, startWith } from 'rxjs';
import { BuscarProductoService } from '../buscar-producto/buscar-producto.service';
import { NotificacionesAdminService } from '../../shared/services/notificaciones-admin.service';

@Component({
  selector: 'app-admin-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './admin.html',
  styleUrl: './admin.css',
})
export class AdminLayoutComponent {
  protected readonly sesion = inject(AutenticacionAdminService);
  private readonly router = inject(Router);
  private readonly productosService = inject(BuscarProductoService);
  protected readonly servicioNotificaciones = inject(NotificacionesAdminService);
  protected readonly errorSesion = signal('');
  protected readonly notificacionesAbiertas = signal(false);
  protected readonly errorInventario = signal(false);
  protected readonly notificaciones = this.servicioNotificaciones.notificaciones;
  protected readonly cantidadNotificaciones = this.servicioNotificaciones.cantidad;

  constructor() {
    interval(15_000)
      .pipe(
        startWith(0),
        exhaustMap(() =>
          !this.sesion.puede('inventario')
            ? EMPTY
            : this.productosService.consultarStockBajo().pipe(
                catchError(() => {
                  this.errorInventario.set(true);
                  return EMPTY;
                }),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((productos) => {
        this.errorInventario.set(false);
        this.servicioNotificaciones.actualizarAlertasStockBajo(productos);
      });
  }

  protected cerrarMenu(event: Event): void {
    (event.target as HTMLElement).closest('details')?.removeAttribute('open');
  }

  protected alternarNotificaciones(): void {
    this.notificacionesAbiertas.update((abiertas) => !abiertas);
  }

  protected eliminarNotificacion(id: number): void {
    this.servicioNotificaciones.eliminar(id);
  }

  protected salir(): void {
    this.sesion.cerrarSesion().subscribe({
      next: () => {
        void this.router.navigate(['/admin']);
      },
      error: () => this.errorSesion.set('No se pudo cerrar la sesión. Inténtalo nuevamente.'),
    });
  }
}
