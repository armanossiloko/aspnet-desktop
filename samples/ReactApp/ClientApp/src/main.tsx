import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { DesktopHttpProvider } from 'aspnetdesktop-client/react';
import { App } from './App';
import './styles.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {/* useDesktopHttpClient() below this calls the in-process ASP.NET Core API over ipc://localhost. */}
    <DesktopHttpProvider>
      <App />
    </DesktopHttpProvider>
  </StrictMode>,
);
