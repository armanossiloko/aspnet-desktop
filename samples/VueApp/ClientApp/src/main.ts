import { createApp } from 'vue';
import { createDesktopHttp } from 'aspnetdesktop-client/vue';
import App from './App.vue';
import './styles.css';

// useDesktopHttpClient() in components calls the in-process ASP.NET Core API over ipc://localhost.
createApp(App).use(createDesktopHttp()).mount('#app');
