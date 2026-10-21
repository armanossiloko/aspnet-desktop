import { type ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideDesktopHttpClient } from 'aspnetdesktop-client/angular';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Real HTTP: the standard HttpClient, untouched by the desktop host.
    provideHttpClient(),
    // In-process ASP.NET Core over ipc://localhost: a separate client, not an interceptor.
    provideDesktopHttpClient(),
  ],
};
