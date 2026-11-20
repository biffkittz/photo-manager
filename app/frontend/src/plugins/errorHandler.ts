import type { App } from 'vue'
import { useNotificationsStore } from '@/stores/notifications'

/**
 * Hook for an external error reporter (e.g. Sentry). Not wired by default: sending errors to a third party is a
 * privacy decision (errors can include photo metadata). See docs/operations.md.
 */
export type ErrorReporter = (error: unknown, context: string) => void

let reporter: ErrorReporter | null = null

export function setErrorReporter(next: ErrorReporter | null): void {
  reporter = next
}

/** Global handler for errors not caught by a component boundary: log, report, and tell the user. */
export const errorHandlerPlugin = {
  install(app: App): void {
    app.config.errorHandler = (error, _instance, info) => {
      console.error('[photo-manager]', info, error)
      reporter?.(error, info)
      useNotificationsStore().notify('error', 'Something went wrong. Please try again.')
    }

    window.addEventListener('unhandledrejection', (event) => {
      console.error('[photo-manager] unhandled promise rejection', event.reason)
      reporter?.(event.reason, 'unhandledrejection')
    })
  },
}
