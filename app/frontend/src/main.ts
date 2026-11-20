import './styles/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'

import App from './App.vue'
import { ApiError, configureHttp } from './api/http'
import { vAutofocus } from './directives/vAutofocus'
import { errorHandlerPlugin } from './plugins/errorHandler'
import { router } from './router'
import { useAuthStore } from './stores/auth'
import { useUploadsStore } from './stores/uploads'
import { persistPlugin } from './stores/plugins/persist'

const app = createApp(App)

const pinia = createPinia()
pinia.use(persistPlugin)
app.use(pinia)

// Server state lives in Vue Query (caching, background revalidation, request cancellation); Pinia holds
// client state (session, upload queue, preferences).
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) =>
        !(error instanceof ApiError && error.isClientError) && failureCount < 2,
    },
  },
})
app.use(VueQueryPlugin, { queryClient })

const auth = useAuthStore()

// When a session ends (sign-out or expiry), nothing of the previous user's may linger in memory: cached photo
// data is dropped and queued uploads are cancelled.
auth.onSessionEnded(() => {
  queryClient.clear()
  useUploadsStore().reset()
})

configureHttp({
  csrfToken: () => auth.csrfToken,
  unauthorized: () => {
    if (auth.isAuthenticated) {
      auth.clear()
      void router.push({ name: 'login', query: { returnUrl: router.currentRoute.value.fullPath } })
    }
  },
})

app.use(router)
app.use(errorHandlerPlugin)
app.directive('autofocus', vAutofocus)
app.mount('#app')
