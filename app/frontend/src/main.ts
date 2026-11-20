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

// When a session ends (sign-out, expiry, or in another tab), nothing of the previous user's may linger in
// memory: cached photo data is dropped, queued uploads are cancelled, and a page that needs a session is left
// for the sign-in page.
auth.onSessionEnded(() => {
  queryClient.clear()
  useUploadsStore().reset()
  const current = router.currentRoute.value
  if (!current.meta.public) {
    void router.push({ name: 'login', query: { returnUrl: current.fullPath } })
  }
})

configureHttp({
  csrfToken: () => auth.csrfToken,
  unauthorized: () => auth.clear(),
  refreshCsrf: () => auth.refresh(),
})

// A tab coming back to the foreground re-checks the session (it may have expired, or ended in a tab that was
// closed meanwhile) and picks up a fresh anti-CSRF token.
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible' && auth.isAuthenticated) {
    void auth.refresh()
  }
})

app.use(router)
app.use(errorHandlerPlugin)
app.directive('autofocus', vAutofocus)
app.mount('#app')
