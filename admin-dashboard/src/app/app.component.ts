import { Component, Injector } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: '<router-outlet></router-outlet>',
})
export class AppComponent {
  static InjectorInstance: Injector;

  constructor(private injector: Injector) {
    AppComponent.InjectorInstance = this.injector;
  }
}
