import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AutenticacionClienteService } from '../../shared/services/autenticacion-cliente.service';

// Pantalla del enlace de invitación: el cliente creado por el personal define su contraseña.
@Component({
  selector: 'app-activar-cuenta-cliente',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './activar-cuenta-cliente.html',
  styleUrls: ['../confirmar-cuenta-cliente/confirmar-cuenta-cliente.css', './activar-cuenta-cliente.css'],
})
export class ActivarCuentaClienteComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly autenticacion = inject(AutenticacionClienteService);

  readonly estado = signal<'formulario' | 'exito' | 'sin-token'>('formulario');
  readonly mensaje = signal('');
  readonly error = signal('');
  readonly enviando = signal(false);
  readonly mostrarContrasena = signal(false);

  nuevaContrasena = '';
  confirmarContrasena = '';
  private token = '';

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token')?.trim() ?? '';
    if (!this.token) {
      this.estado.set('sin-token');
      this.mensaje.set('El enlace no contiene un token de activación. Pide en la óptica que te envíen una nueva invitación.');
    }
  }

  activar(): void {
    if (this.enviando()) return;
    if (!this.contrasenaValida(this.nuevaContrasena)) {
      this.error.set('La contraseña debe tener al menos 8 caracteres e incluir letras, números y un símbolo.');
      return;
    }
    if (this.nuevaContrasena !== this.confirmarContrasena) {
      this.error.set('Las contraseñas no coinciden.');
      return;
    }

    this.error.set('');
    this.enviando.set(true);
    this.autenticacion.activarCuenta(this.token, this.nuevaContrasena, this.confirmarContrasena).subscribe({
      next: (respuesta) => {
        this.enviando.set(false);
        this.nuevaContrasena = '';
        this.confirmarContrasena = '';
        this.estado.set('exito');
        this.mensaje.set(respuesta.mensaje);
      },
      error: (error: HttpErrorResponse) => {
        this.enviando.set(false);
        this.error.set(typeof error.error?.mensaje === 'string'
          ? error.error.mensaje
          : 'No se pudo activar la cuenta. Inténtalo nuevamente más tarde.');
      }
    });
  }

  irAlLogin(): void {
    void this.router.navigateByUrl('/cliente/login');
  }

  // Misma regla que el registro y la recuperación de contraseña.
  private contrasenaValida(valor: string): boolean {
    return valor.length >= 8 && valor.length <= 255
      && /\p{L}/u.test(valor) && /\p{N}/u.test(valor) && /[^\p{L}\p{N}\s]/u.test(valor);
  }
}
